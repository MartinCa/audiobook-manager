using System.Diagnostics;
using System.Text.RegularExpressions;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Security;
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

        // Deliberately no CORS policy. The frontend is served from this application's own origin in
        // production, and the Vite dev server proxies /api and /hubs to it (see
        // client/vite.config.ts), so the browser only ever makes same-origin requests and nothing
        // here needs cross-origin access. The policy that used to sit here allowed
        // http://localhost:3000 with AllowCredentials - a dev-server leftover that the proxy had
        // already made unnecessary. Having no policy at all is also what makes
        // CrossSiteRequestGuardMiddleware effective: a cross-site request that is forced to
        // preflight now fails the preflight outright.

        // Add services to the container.

        builder.Services.AddSignalR();

        // Makes every error response - the framework's own, ControllerBase.Problem(), and the
        // exception handler below - a single shape: RFC 9457 application/problem+json. The client
        // has always documented and parsed that shape (client/src/lib/api.ts), but nothing was
        // producing it: a bare-string BadRequest(ex.Message) serializes as text/plain, and the
        // client's error parser reads a body only when the content type says json. Every
        // hand-written server message was being dropped and shown as "Request failed with status
        // 400".
        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                // A correlation id, so an operator can tie the stable message the caller sees to
                // the full exception in the container log. MVC's ProblemDetailsFactory already
                // adds this for responses produced inside a controller; the exception handler's
                // path does not, and that is the one where the detail is deliberately withheld.
                context.ProblemDetails.Extensions.TryAdd(
                    "traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
            });

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
            app.UseSwaggerUI(options =>
            {
                // Swagger UI issues its "Try it out" requests with its own fetch, which does not
                // set X-Requested-With - so every write from it would be refused by
                // CrossSiteRequestGuardMiddleware. Adding the header here keeps the guard applied
                // uniformly in development rather than carving out an exception for it (which
                // would leave the guard untested in the environment it is developed in).
                options.UseRequestInterceptor(
                    $"(request) => {{ request.headers['{CrossSiteRequestGuardMiddleware.RequiredHeader}'] = "
                    + $"'{CrossSiteRequestGuardMiddleware.RequiredHeaderValue}'; return request; }}");
            });
        }

        // app.UseHttpsRedirection();

        // First, so it wraps everything after it. Without this an unhandled exception returned an
        // empty 500 with no body at all, which the client could only render as a bare status code.
        // Reachable without anything going wrong: organizing the same file twice violates
        // QueuedOrganizeTask's primary key and throws DbUpdateException out of the repository.
        app.UseExceptionHandler();

        // Before the static files and the SPA fallback, so a refused API write never falls through
        // to index.html.
        app.UseCrossSiteRequestGuard();

        // Serve the frontend app
        var defaultFileOptions = new DefaultFilesOptions();
        defaultFileOptions.DefaultFileNames.Clear();
        defaultFileOptions.DefaultFileNames.Add("index.html");
        app.UseDefaultFiles(defaultFileOptions);
        app.UseStaticFiles();

        app.UseAuthorization();

        app.MapControllers();

        app.MapHub<OrganizeHub>("/hubs/organize");

        // The SPA uses browser (path-based) routing, so a direct navigation or refresh on a
        // nested route (e.g. /library/book/42) has to fall back to index.html and let the
        // client-side router take over. Controllers and the SignalR hub are mapped above and
        // take precedence, so this only ever catches requests nothing else matched.
        app.MapFallbackToFile("index.html");

        try
        {
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FATAL] Audiobook Manager startup failed: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
            throw;
        }
    }
}
