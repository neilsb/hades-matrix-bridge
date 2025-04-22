using System.IO;
using Microsoft.Extensions.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MatrixBridgeSdk.Configuration
{
    public class RegistrationConfigurationSource : IConfigurationSource
    {
        private readonly string _registrationPath;

        public RegistrationConfigurationSource(string registrationPath)
        {
            _registrationPath = registrationPath;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new RegistrationConfigurationProvider(_registrationPath);
        }
    }

    public class RegistrationConfigurationProvider : ConfigurationProvider
    {
        private readonly string _registrationPath;

        public RegistrationConfigurationProvider(string registrationPath)
        {
            _registrationPath = registrationPath;
        }

        public override void Load()
        {
            if (!File.Exists(_registrationPath))
            {
                return;
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance) 
                .Build();

            var yaml = File.ReadAllText(_registrationPath);
            var registration = deserializer.Deserialize<RegistrationConfig>(yaml);

            Data["Matrix:AccessToken"] = registration.AppServiceToken;
            Data["Matrix:AuthorizationToken"] = registration.HomeserverToken;
        }
    }
}