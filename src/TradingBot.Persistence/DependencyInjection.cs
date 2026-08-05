using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TradingDatabase")
            ?? configuration.GetSection("Database")["ConnectionString"];

        services.AddDbContext<TradingDbContext>(options =>
        {
            options.UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(TradingDbContext).Assembly.FullName));
        });

        return services;
    }
}
