using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.FileManager;
public static class DependencyInjection
{
    public static IServiceCollection SetupFileManager(this IServiceCollection services)
    {
        services
        .AddScoped<IFileOperations, FileOperations>()
        .AddScoped<IAudiobookFileHandler, AudiobookFileHandler>()
        .AddScoped<IAudiobookTagHandler, AudiobookTagHandler>()
        // Stateless: a shared singleton avoids re-resolving it per request for a pure walk.
        .AddSingleton<ILibraryTreeWalker, LibraryTreeWalker>();

        services.AddSingleton<IAtlLogging>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new AtlLogging(loggerFactory);
        });

        return services;
    }
}
