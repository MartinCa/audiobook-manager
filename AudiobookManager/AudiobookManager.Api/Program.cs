using AudiobookManager.Database;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;

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

        builder.Services.AddControllers();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.SetupServiceLayer();

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

        using (var scope = builder.Services.BuildServiceProvider().CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<DatabaseContext>().Database.Migrate();
        }

        app.Run();
    }
}
