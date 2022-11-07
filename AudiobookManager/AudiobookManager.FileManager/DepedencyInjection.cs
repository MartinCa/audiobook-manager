using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.FileManager;
public static class DepedencyInjection
{
    public static IServiceCollection SetupFileManager(this IServiceCollection services)
    {
        services
        .AddScoped<IAudiobookTagHandler, AudiobookTagHandler>();

        services.AddSingleton<IAtlLogging>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new AtlLogging(loggerFactory);
        });

        return services;
    }
}
