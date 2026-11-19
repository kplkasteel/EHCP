using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;

namespace Ehcp.Logging;

public enum EhcpLoggingDestination
{
    LocalSerilog,
    AzureApplicationInsights
}

/// <summary>
/// Keeps the provider choice out of application services. All consumers inject ILogger<T>.
/// A Release build running in Development still writes locally; Azure hosts use Application Insights.
/// </summary>
public static class LoggingExtensions
{
    public static EhcpLoggingDestination ResolveDestination(
        string? functionsEnvironment,
        string? dotnetEnvironment,
        string? aspNetCoreEnvironment,
        string? fallbackEnvironment)
    {
        // AZURE_FUNCTIONS_ENVIRONMENT wins when it is explicitly configured.
        string environment = FirstNonBlank(
            functionsEnvironment,
            aspNetCoreEnvironment,
            dotnetEnvironment,
            fallbackEnvironment) ?? "Production";

        return environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
            || environment.Equals("Testing", StringComparison.OrdinalIgnoreCase)
            ? EhcpLoggingDestination.LocalSerilog
            : EhcpLoggingDestination.AzureApplicationInsights;
    }

    public static EhcpLoggingDestination AddEhcpLogging(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("A service name is required.", nameof(serviceName));

        EhcpLoggingDestination destination = ResolveDestination(
            builder.Configuration["AZURE_FUNCTIONS_ENVIRONMENT"],
            builder.Configuration["DOTNET_ENVIRONMENT"],
            builder.Configuration["ASPNETCORE_ENVIRONMENT"],
            builder.Environment.EnvironmentName);

        if (destination == EhcpLoggingDestination.LocalSerilog)
        {
            // No Azure subscription or connection string is required for local tests.
            // Keep console and JSON files even when Seq is not installed/running.
            string logFile = FirstNonBlank(builder.Configuration["EHCP_LOG_FILE"])
                ?? Path.Combine(AppContext.BaseDirectory, "logs", $"{serviceName}-.clef");

            var configuration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ServiceName", serviceName)
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    logFile,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 100_000_000,
                    rollOnFileSizeLimit: true,
                    shared: true);

            // Enabled by default for developers running Seq at localhost:5341.
            // Set EHCP_SEQ_ENABLED=false to disable; file/console logging still works.
            bool seqEnabled = !bool.TryParse(
                builder.Configuration["EHCP_SEQ_ENABLED"], out bool configured) || configured;
            if (seqEnabled)
            {
                string seqUrl = FirstNonBlank(builder.Configuration["EHCP_SEQ_URL"])
                    ?? "http://localhost:5341";
                if (!Uri.TryCreate(seqUrl, UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidOperationException("EHCP_SEQ_URL must be an absolute http(s) URL.");
                configuration.WriteTo.Seq(seqUrl);
            }

            // Avoid duplicated ILogger output from other worker-side providers in Development.
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
            builder.Logging.AddSerilog(configuration.CreateLogger(), dispose: true);
        }
        else
        {
            // Fail early, rather than silently running a deployed app without diagnostics.
            if (string.IsNullOrWhiteSpace(
                builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            {
                throw new InvalidOperationException(
                    "APPLICATIONINSIGHTS_CONNECTION_STRING is required outside Development/Testing.");
            }

            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.Services.AddOpenTelemetry()
                .UseFunctionsWorkerDefaults()
                .UseAzureMonitorExporter();
        }

        return destination;
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
