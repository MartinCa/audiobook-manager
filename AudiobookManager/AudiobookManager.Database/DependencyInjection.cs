using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Database;
public static class DependencyInjection
{
    public static IServiceCollection SetupDatabase(this IServiceCollection services)
    {
        return services.AddDbContext<DatabaseContext>();
    }
}
