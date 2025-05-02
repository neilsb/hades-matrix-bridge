using HadesMatrixBridge;
using HadesMatrixBridge.Configuration;
using MatrixBridgeSdk.Services;
using MatrixBridgeSdk;
using MatrixBridgeSdk.Configuration;
using Microsoft.Extensions.Options;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Define the path to the data folder
var dataFolderPath = Path.Combine(AppContext.BaseDirectory, "data");

// Create the data folder if it doesn't exist
if (!Directory.Exists(dataFolderPath))
{
    Directory.CreateDirectory(dataFolderPath);
}

// Create the logs folder if it doesn't exist
string logFolderPath = Path.Combine(dataFolderPath, "logs");
if (!Directory.Exists(logFolderPath))
{
    Directory.CreateDirectory(logFolderPath);
}


// Add configuration sources in order of increasing priority

// "appsettings.json" file in the data folder it exists
var dataFolderConfigPath = Path.Combine(dataFolderPath, "appsettings.json");
if (File.Exists(dataFolderConfigPath))
{
    builder.Configuration.AddJsonFile(dataFolderConfigPath, optional: false, reloadOnChange: true);
    Console.WriteLine($"Loaded configuration from {dataFolderConfigPath}");
}

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

// Add registration file configuration
// Get the bridge name from configuration
var matrixConfig = builder.Configuration.GetSection("Matrix").Get<MatrixConfig>();
builder.Configuration.AddRegistrationFile(matrixConfig?.BridgeName ?? "hades-dev-bridge");

// Add command-line configuration
builder.Configuration.AddCommandLine(args, switchMappings);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logFolderPath, "log_.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 28,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Add Serilog to the logging pipeline
builder.Logging.AddSerilog(Log.Logger);

// Add Seq if configured
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
    var matrixCfg = scope.ServiceProvider.GetRequiredService<IOptions<MatrixConfig>>();
    var hadesConfig = scope.ServiceProvider.GetRequiredService<IOptions<HadesConfig>>();

    // Validate required configuration settings
    ConfigurationValidator.ValidateConfiguration(matrixCfg, hadesConfig, logger);
}

try
{
    host.Run();
}
finally
{
    // Ensure to flush and stop internal timers/threads before exiting the application
    Log.CloseAndFlush();
}
