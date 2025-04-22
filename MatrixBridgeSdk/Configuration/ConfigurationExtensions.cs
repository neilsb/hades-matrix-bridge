using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace MatrixBridgeSdk.Configuration
{
    public static class ConfigurationExtensions
    {
        public static IConfigurationBuilder AddRegistrationFile(
            this IConfigurationBuilder builder,
            string bridgeName)
        {
            var registrationPath = Path.Combine(
                AppContext.BaseDirectory, 
                "data", 
                $"{bridgeName}-registration.yaml");

            return builder.Add(new RegistrationConfigurationSource(registrationPath));
        }
    }
}