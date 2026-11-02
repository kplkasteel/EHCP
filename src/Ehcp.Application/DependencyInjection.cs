using Ehcp.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Ehcp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddEhcpProcessing(this IServiceCollection services)
    {
        services.AddScoped<IEmailProcessor, EmailProcessor>();
        services.AddScoped<IMatchingService, MatchingService>();
        return services;
    }
}
