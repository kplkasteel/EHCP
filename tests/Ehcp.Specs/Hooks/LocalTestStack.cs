using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Azure.Storage.Queues;
using Ehcp.Infrastructure;
using Microsoft.Azure.Cosmos;

namespace Ehcp.Specs.Hooks;

// Starts external hosts, not replacement application services. Each scenario still uses HTTP and Azurite.
internal static class LocalTestStack
{
    private static readonly List<Process> Owned = [];
    private static readonly object Gate = new();
    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
    { Timeout = TimeSpan.FromSeconds(3) };
    private static string root = "";
    private static string logs = "";
    private static FileStream? runLock;
    private static Process? processingHost;
    private static bool started;
    internal static string DatabaseName { get; private set; } = "";
    private static string queueName = "";

    public static async Task StartAsync()
    {
        root = FindRoot();
        logs = Path.Combine(root, "tests", "Ehcp.Specs", "TestResults", "local-stack", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Environment.ProcessId);
        Directory.CreateDirectory(logs);
        // Fixed mock responses and ports cannot safely serve concurrent test runs.
        string lockPath = Path.Combine(Path.GetTempPath(), "ehcp-team-shared-1-stack-5100.lock");
        try { runLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new InvalidOperationException("Another EHCP local test run is active. Wait for it to finish.", ex); }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
        try
        {
            Configure();
            bool queueRunning = await PortOpen(10001);
            bool blobRunning = await PortOpen(10000);
            if (queueRunning != blobRunning)
                throw new InvalidOperationException(
                    $"Azurite is partially running: queue 10001={queueRunning}, blob 10000={blobRunning}. " +
                    "Stop the existing Azurite instance and rerun the test. " +
                    "The hook will start both endpoints together; it will not kill an externally started process.");

            Process? azurite = null;
            if (!queueRunning && !blobRunning)
            {
                // Avoid a late failure when a leftover table-only process owns Azurite's third port.
                if (await PortOpen(10002))
                    throw new InvalidOperationException("Port 10002 is already occupied. Stop the existing Azurite/table service before running tests.");
                azurite = Launch(
                    "azurite", root, "azurite",
                    "--silent", "--skipApiVersionCheck",
                    "--blobHost", "127.0.0.1", "--blobPort", "10000",
                    "--queueHost", "127.0.0.1", "--queuePort", "10001",
                    "--tableHost", "127.0.0.1", "--tablePort", "10002",
                    "--location", Path.Combine(Path.GetTempPath(), "ehcp-specs-azurite"));
            }

            // UseDevelopmentStorage targets IPv4. Probe those exact interfaces too.
            // Monitor the process while waiting so a crashed host fails immediately with its log path.
            await Wait("Azurite blob endpoint", () => PortOpen(10000), azurite);
            var queue = new QueueServiceClient("UseDevelopmentStorage=true", new QueueClientOptions
            { Retry = { MaxRetries = 0, NetworkTimeout = TimeSpan.FromSeconds(3) } });
            await Wait("Azurite queue", async () => { await queue.GetPropertiesAsync(); return true; }, azurite);

            if (!await PortOpen(8081))
            {
                string emulator = Environment.GetEnvironmentVariable("EHCP_COSMOS_EMULATOR_EXE")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Azure Cosmos DB Emulator", "CosmosDB.Emulator.exe");
                if (!OperatingSystem.IsWindows() || !File.Exists(emulator))
                    throw new InvalidOperationException("Install the Windows Cosmos DB emulator, or start your local emulator before testing. Set EHCP_COSMOS_EMULATOR_EXE for a custom install path.");
                // The emulator is a machine-level, self-managed process; leave it running after tests.
                using var launcher = Process.Start(new ProcessStartInfo(emulator) { UseShellExecute = true });
            }
            using var cosmos = new CosmosClient("https://localhost:8081", Environment.GetEnvironmentVariable("Cosmos__Key"),
                new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway, RequestTimeout = TimeSpan.FromSeconds(5) });
            await Wait("Cosmos emulator (including trusted TLS certificate)", async () => { await cosmos.ReadAccountAsync(); return true; });

            await StartHost("API", "Ehcp.Api", 5100, false);
            await StartHost("MockGraph", "Ehcp.MockGraph", 7101, true);
            await StartHost("MockExtraction", "Ehcp.MockExtraction", 7102, true);
            await StartHost("Processing", "Ehcp.Processing", 7103, true);
            started = true;
        }
        catch (Exception ex)
        {
            Stop();
            throw new InvalidOperationException($"EHCP local stack failed to start. Logs: {logs}. {ex.Message}", ex);
        }
    }

    private static void Configure()
    {
        // Apply before any scenario bindings are constructed; child processes inherit these values.
        string runId = Guid.NewGuid().ToString("N");
        DatabaseName = "ehcp-specs-" + runId;
        queueName = "ehcp-specs-" + runId;
        var values = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["AZURE_FUNCTIONS_ENVIRONMENT"] = "Development",
            ["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated",
            ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
            ["ProcessingQueueName"] = queueName,
            ["Cosmos__Endpoint"] = "https://localhost:8081",
            ["Cosmos__Database"] = DatabaseName,
            ["Cosmos__Initialize"] = "true",
            ["Ehcp__BaseUrl"] = "http://localhost:5100",
            ["MockGraph__BaseUrl"] = "http://localhost:7101",
            ["MockExtraction__BaseUrl"] = "http://localhost:7102",
            ["Processing__Graph__BaseUrl"] = "http://localhost:7101",
            ["Processing__Extraction__Endpoint"] = "http://localhost:7102/api/extract",
            ["Processing__Graph__Mailboxes__ehcp-team-shared-1"] = "ehcp-team-shared-1@example.com",
            ["Cosmos__Mailboxes__ehcp-team-shared-1__EmailsContainer"] = "ehcp-team-emails",
            ["Cosmos__Mailboxes__ehcp-team-shared-1__CasesContainer"] = "ehcp-team-cases"
        };
        foreach (var item in values) Environment.SetEnvironmentVariable(item.Key, item.Value);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Cosmos__Key")))
            Environment.SetEnvironmentVariable("Cosmos__Key", new CosmosSettings().Key);
    }

    private static async Task StartHost(string name, string project, int port, bool function, bool buildProject = true)
    {
        // Refuse occupied application ports: a manually running host can have incompatible settings.
        if (await PortOpen(port)) throw new InvalidOperationException($"Port {port} is already in use. Stop the manually started {name} host before running tests.");
        string directory = Path.Combine(root, "src", project);
        if (buildProject)
        {
            Process build = Launch("build-" + name, directory, "dotnet", "build", project + ".csproj",
                "--configuration", "Debug", "--framework", "net10.0", "--nologo");
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
                await build.WaitForExitAsync(timeout.Token);
            if (build.ExitCode != 0) throw new InvalidOperationException($"{name} build failed; see build-{name}.log.");
        }
        string functionRoot = Path.Combine(directory, "bin", "Debug", "net10.0");
        string[] expectedFunctions = project switch
        {
            "Ehcp.MockGraph" => ["SeedGraphMessage", "GetGraphMessage"],
            "Ehcp.MockExtraction" => ["SeedExtraction", "Extract"],
            "Ehcp.Processing" => ["ProcessEmail"],
            _ => []
        };
        if (function)
        {
            foreach (string required in new[] { "host.json", "worker.config.json", "functions.metadata" })
                if (!File.Exists(Path.Combine(functionRoot, required)))
                    throw new InvalidOperationException($"{name} build output is missing {required}: {functionRoot}");
            if (!Directory.Exists(Path.Combine(functionRoot, ".azurefunctions")))
                throw new InvalidOperationException($"{name} build output is missing .azurefunctions: {functionRoot}");
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(functionRoot, "functions.metadata")));
            foreach (string expected in expectedFunctions)
                if (!metadata.RootElement.EnumerateArray().Any(f =>
                    f.TryGetProperty("name", out var n) && n.GetString() == expected))
                    throw new InvalidOperationException($"{name}: {expected} is absent from generated functions.metadata. See build-{name}.log.");
        }
        // With --no-build, Core Tools must run at the compiled function root, not the source folder.
        Process host = function
            ? Launch(name, functionRoot, "func", "start", "--dotnet-isolated", "--no-build",
                "--script-root", functionRoot, "--port", port.ToString())
            : Launch(name, directory, "dotnet", "run", "--no-build", "--no-launch-profile", "--urls", $"http://localhost:{port}");
        if (project == "Ehcp.Processing") processingHost = host;
        await Wait(name, async () =>
        {
            using var response = await Http.GetAsync($"http://localhost:{port}/" + (function ? "admin/host/status" : ""));
            if (!response.IsSuccessStatusCode) return false;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!function)
                return json.RootElement.TryGetProperty("name", out var value) && value.GetString() == "EHCP API";
            if (!json.RootElement.TryGetProperty("state", out var state) || state.GetString() != "Running")
                return false;

            // A Running host can have zero functions. Verify each expected registration as well.
            foreach (string expected in expectedFunctions)
            {
                using var status = await Http.GetAsync($"http://localhost:{port}/admin/functions/{expected}/status");
                if (!status.IsSuccessStatusCode) return false;
                using var details = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
                if (details.RootElement.TryGetProperty("errors", out var errors)
                    && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                    throw new InvalidOperationException($"{name}/{expected} failed to load: {errors}");
            }
            return true;
        }, host);
    }

    private static Process Launch(string name, string directory, string executable, params string[] args)
    {
        string tool = ResolveTool(executable);
        var info = new ProcessStartInfo
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows() && (tool.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || tool.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            // npm installs Azurite/Core Tools as command shims on Windows.
            info.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            static string Quote(string text)
            {
                if (text.IndexOfAny(['"', '%', '\r', '\n']) >= 0) throw new InvalidOperationException("Unsupported character in command shim path/argument.");
                return "\"" + text + "\"";
            }
            info.Arguments = "/d /s /c \"" + string.Join(" ", new[] { tool }.Concat(args).Select(Quote)) + "\"";
        }
        else
        {
            info.FileName = tool;
            foreach (string arg in args) info.ArgumentList.Add(arg);
        }
        string file = Path.Combine(logs, name + ".log");
        var logGate = new object();
        void Capture(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (logGate) File.AppendAllText(file, e.Data + Environment.NewLine);
        }
        var process = new Process { StartInfo = info };
        process.OutputDataReceived += Capture;
        process.ErrorDataReceived += Capture;
        process.Start();
        lock (Gate) Owned.Add(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static string ResolveTool(string name)
    {
        string? custom = Environment.GetEnvironmentVariable("EHCP_" + name.ToUpperInvariant() + "_EXE");
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return Path.GetFullPath(custom);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).ToList();
        if (OperatingSystem.IsWindows()) dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
        foreach (string dir in dirs.Where(d => !string.IsNullOrWhiteSpace(d)))
            foreach (string suffix in OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat" } : new[] { "" })
            {
                string path = Path.Combine(dir.Trim('"'), name + suffix);
                if (File.Exists(path)) return Path.GetFullPath(path);
            }
        if (OperatingSystem.IsWindows() && name.Equals("azurite", StringComparison.OrdinalIgnoreCase))
        {
            string? bundled = FindVisualStudioAzurite();
            if (bundled is not null) return bundled;
        }
        throw new InvalidOperationException($"Cannot find {name}. Install it and restart Visual Studio, or set EHCP_{name.ToUpperInvariant()}_EXE to its full path.");
    }

    private static string? FindVisualStudioAzurite()
    {
        // Discover editions/versions (including VS 2026) instead of hard-coding VS 2022.
        const string relative = "Common7/IDE/Extensions/Microsoft/Azure Storage Emulator/azurite.exe";
        string? vsDirectory = Environment.GetEnvironmentVariable("VSINSTALLDIR");
        if (!string.IsNullOrWhiteSpace(vsDirectory))
        {
            string candidate = Path.Combine(vsDirectory, relative);
            if (File.Exists(candidate)) return candidate;
        }

        var options = new EnumerationOptions { IgnoreInaccessible = true };
        foreach (string programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string installations = Path.Combine(programFiles, "Microsoft Visual Studio");
            if (!Directory.Exists(installations)) continue;
            foreach (string version in Directory.EnumerateDirectories(installations, "*", options).OrderByDescending(p => p))
                foreach (string edition in Directory.EnumerateDirectories(version, "*", options))
                {
                    string candidate = Path.Combine(edition, relative);
                    if (File.Exists(candidate)) return candidate;
                }
        }
        return null;
    }

    private static async Task Wait(string name, Func<Task<bool>> ready, Process? process = null)
    {
        var watch = Stopwatch.StartNew();
        Exception? last = null;
        while (watch.Elapsed < TimeSpan.FromMinutes(2))
        {
            if (process?.HasExited == true) throw new InvalidOperationException($"{name} exited with code {process.ExitCode}. See {name}.log.");
            try { if (await ready()) return; }
            catch (Azure.RequestFailedException ex) when (ex.Status == 400 && ex.ErrorCode == "InvalidHeaderValue")
            {
                throw new InvalidOperationException(
                    $"{name} rejected the storage request: {ex.Message} " +
                    "If an existing Azurite instance was reused, stop it and rerun so --skipApiVersionCheck is applied.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CosmosException or Azure.RequestFailedException or JsonException) { last = ex; }
            await Task.Delay(500);
        }
        throw new TimeoutException($"{name} was not ready within two minutes. {last?.Message}", last);
    }

    private static async Task<bool> PortOpen(int port)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token); return true; }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return false; }
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Ehcp.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate Ehcp.slnx above the test output directory. Run tests from the extracted repository.");
    }

    internal static void StopProcessing()
    {
        lock (Gate)
        {
            if (processingHost is null) return;
            if (!processingHost.HasExited)
            {
                processingHost.Kill(entireProcessTree: true);
                if (!processingHost.WaitForExit(5000))
                    throw new InvalidOperationException("The processing host did not stop before scenario cleanup.");
            }
            Owned.Remove(processingHost);
            processingHost.Dispose();
            processingHost = null;
        }
    }

    internal static Task StartProcessingAsync() => StartHost("Processing", "Ehcp.Processing", 7103, true, buildProject: false);

    internal static async Task StopAsync()
    {
        try
        {
            if (!started) return;
            StopProcessing();
            var queues = new QueueServiceClient("UseDevelopmentStorage=true");
            await queues.GetQueueClient(queueName).DeleteIfExistsAsync();
            await queues.GetQueueClient(queueName + "-poison").DeleteIfExistsAsync();
            using var cosmos = new CosmosClient("https://localhost:8081", Environment.GetEnvironmentVariable("Cosmos__Key"),
                new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
            try { await cosmos.GetDatabase(DatabaseName).DeleteAsync(); }
            catch (CosmosException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
        }
        finally
        {
            started = false;
            Stop();
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            foreach (Process process in Owned.AsEnumerable().Reverse())
            {
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); } }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Console.Error.WriteLine(ex.Message); }
                process.Dispose();
            }
            Owned.Clear();
            processingHost = null;
            runLock?.Dispose();
            runLock = null;
        }
    }
}
