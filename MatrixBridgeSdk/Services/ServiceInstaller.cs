using LiteDB;
using MatrixBridgeSdk.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.IO;

namespace MatrixBridgeSdk.Services
{
    public static class ServiceInstaller
    {
        public static IServiceCollection UseMatrixServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Register configuration
            services.Configure<MatrixConfig>(configuration.GetSection("Matrix"));
            services.Configure<DatabaseConfig>(configuration.GetSection("Database"));

            services.AddSingleton<ILiteDatabase>((sp) =>
            {
                var dbConfig = sp.GetRequiredService<IOptions<DatabaseConfig>>().Value;
                
                // Ensure the directory exists
                var directory = Path.GetDirectoryName(dbConfig.Path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Construct the connection string
                var connectionString = new ConnectionString
                {
                    Filename = dbConfig.Path,
                    Connection = ConnectionType.Direct,
                };

                var db = new LiteDatabase(connectionString);
                db.UtcDate = true;
                return db;
            });
            services.AddSingleton<MatrixBridge>();
            services.AddHttpClient();

            return services;
        }
    }
}