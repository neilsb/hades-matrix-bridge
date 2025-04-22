using HadesMatrixBridge;
using HadesMatrixBridge.Configuration;
using MatrixBridgeSdk.Services;
using MatrixBridgeSdk;
using MatrixBridgeSdk.Configuration;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

var switchMappings = new Dictionary<string, string>
{
    { "--server-url", "Matrix:ServerUrl" },
    { "--access-token", "Matrix:AccessToken" },
    { "--authorization-token", "Matrix:AuthorizationToken" },
    { "--port", "Matrix:ListenPort" },
    { "--bind", "Matrix:BindAddress" },
    { "--connection-string", "Database:ConnectionString" },
    { "--hades-server", "Hades:Server" },
    { "--hades-port", "Hades:Port" },
    { "--hades-auto-login", "Hades:AutoLogin" },
    { "--prevent-idle", "Hades:PreventIdle" },
    { "--telnet-port", "Telnet:Port" }
};

// Add registration file configuration (will be overridden by other sources if they exist)
builder.Configuration.AddRegistrationFile(Constants.BridgeName);

// Add command-line configuration source
builder.Configuration.AddCommandLine(args, switchMappings);

builder.Logging.AddSeq(builder.Configuration.GetSection("Seq"));


builder.Services.UseMatrixServices(builder.Configuration);

// Register Hades and Telnet configuration
builder.Services.Configure<HadesConfig>(builder.Configuration.GetSection("Hades"));
builder.Services.Configure<TelnetConfig>(builder.Configuration.GetSection("Telnet"));

builder.Services.AddHostedService<HadesBridgeWorker>();

// Skip validation when generating YAML
bool isGeneratingYaml = args.Contains("--generate-yaml");

if (isGeneratingYaml)
{
    // Get MatrixBridge from DI
    var matrixBridge = builder.Services.BuildServiceProvider().GetRequiredService<MatrixBridge>();
    matrixBridge.GenerateRegistrationFile();
    return; // Exit the application
}

var host = builder.Build();

// Validate configuration before starting the application
if (!isGeneratingYaml)
{
    using var scope = host.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var matrixConfig = scope.ServiceProvider.GetRequiredService<IOptions<MatrixConfig>>();
    var hadesConfig = scope.ServiceProvider.GetRequiredService<IOptions<HadesConfig>>();

    // Validate required configuration settings
    ConfigurationValidator.ValidateConfiguration(matrixConfig, hadesConfig, logger);
}

host.Run();
