using System.Text.RegularExpressions;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Workers;
using AudiobookManager.Database;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

internal partial class SlugifyParameterTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value)
    {
        if (value == null)
        {
            return null;
        }

        return KebabCaseRegex().Replace(value.ToString()!, "$1-$2").ToLowerInvariant();
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex KebabCaseRegex();
}

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        builder.Services.Configure<AudiobookManagerSettings>(builder.Configuration);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        builder.Services.AddRouting(options => options.LowercaseUrls = true);

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(
                policy =>
                {
                    policy.WithOrigins("http://localhost:3000")
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
                });
        });

        // Add services to the container.

        builder.Services.AddSignalR();

        builder.Services.AddControllers(options =>
        {
            options.Conventions.Add(new RouteTokenTransformerConvention(new SlugifyParameterTransformer()));
        });
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.SetupServiceLayer();

        builder.Services.AddSingleton<IOperationStatusRegistry, OperationStatusRegistry>();

        builder.Services.AddHostedService<OrganizeWorker>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // app.UseHttpsRedirection();

        app.UseCors();

        // Serve the frontend app
        var defaultFileOptions = new DefaultFilesOptions();
        defaultFileOptions.DefaultFileNames.Clear();
        defaultFileOptions.DefaultFileNames.Add("index.html");
        app.UseDefaultFiles(defaultFileOptions);
        app.UseStaticFiles();

        app.UseAuthorization();

        app.MapControllers();

        app.MapHub<OrganizeHub>("/hubs/organize");

        // Use the application's own provider, not a second one built from the service
        // collection: BuildServiceProvider() here would create a duplicate, never-disposed set of
        // singletons, so the HardcoverRateLimiter validated below would not be the instance the
        // app actually rate-limits with, and its replenishment timer would leak.
        using (var scope = app.Services.CreateScope())
        {
            // Before anything touches the disk: the import path, the library path and the
            // database's directory are what the application is built on, so a missing one is a
            // startup failure with a message naming the setting - not a 500 from whichever
            // screen happens to reach for it first.
            SettingsValidation.EnsureRequiredPathsAreUsable(
                scope.ServiceProvider.GetRequiredService<IOptions<AudiobookManagerSettings>>().Value);

            scope.ServiceProvider.GetRequiredService<DatabaseContext>().Database.Migrate();

            // Resolving the limiter validates the configured Hardcover burst/per-minute
            // numbers against the API's documented ceiling - fail fast at startup rather
            // than silently exceeding the limits at runtime.
            scope.ServiceProvider.GetRequiredService<HardcoverRateLimiter>();
        }

        app.Run();
    }
}
