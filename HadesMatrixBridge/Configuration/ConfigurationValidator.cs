using MatrixBridgeSdk.Configuration;
using Microsoft.Extensions.Options;
using System.Text;

/// <summary>
/// Configuration can be provided from multiple sources, in order of increasing priority:
/// 1. Default appsettings.json in the application root (lowest priority)
/// 2. Registration YAML file in the data folder
/// 3. Custom appsettings.json in the data folder
/// 4. Command-line arguments (highest priority)
/// </summary>

namespace HadesMatrixBridge.Configuration
{
    public static class ConfigurationValidator
    {
        public static void ValidateConfiguration(
            IOptions<MatrixConfig> matrixConfig,
            IOptions<HadesConfig> hadesConfig,
            ILogger logger)
        {
            var errors = new StringBuilder();
            var matrix = matrixConfig.Value;
            var hades = hadesConfig.Value;

            // Validate Matrix configuration
            if (string.IsNullOrWhiteSpace(matrix.ServerUrl))
            {
                errors.AppendLine("Matrix:ServerUrl is required");
            }

            if (string.IsNullOrWhiteSpace(matrix.AccessToken))
            {
                errors.AppendLine("Matrix:AccessToken is required (or valid yaml registration file)");
            }

            if (string.IsNullOrWhiteSpace(matrix.AuthorizationToken))
            {
                errors.AppendLine("Matrix:AuthorizationToken is required (or valid yaml registration file)");
            }

            // Validate Hades configuration
            if (string.IsNullOrWhiteSpace(hades.Server))
            {
                errors.AppendLine("Hades:Server is required");
            }

            if (hades.Port <= 0)
            {
                errors.AppendLine("Hades:Port must be a positive number");
            }

            // If there are any errors, log them and exit
            if (errors.Length > 0)
            {
                logger.LogError("Configuration validation failed with the following errors:");
                foreach (var line in errors.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                {
                    logger.LogError(line);
                }

                // Exit with error code
                Environment.Exit(1);
            }

            // Log the configuration values being used
            logger.LogInformation("Configuration validation successful");
            logger.LogInformation("Using the following configuration:");
            logger.LogInformation($"Matrix Server URL: {matrix.ServerUrl}");
            logger.LogInformation($"Hades Default Server: {hades.Server}");
            logger.LogInformation($"Hades Default Port: {hades.Port}");
            logger.LogInformation($"Matrix Web Service Port: {matrix.ListenPort}");
        }
    }
}
