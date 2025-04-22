using HadesMatrixBridge;
using HadesMatrixBridge.Configuration;
using MatrixBridgeSdk.Services;
using MatrixBridgeSdk;
using MatrixBridgeSdk.Configuration;

var builder = Host.CreateApplicationBuilder(args);

var switchMappings = new Dictionary<string, string>
{
    { "--server-url", "Matrix:ServerUrl" },
    { "--access-token", "Matrix:AccessToken" },
    { "--authorization-token", "Matrix:AuthorizationToken" },
    { "--web-service-port", "Matrix:WebServicePort" },
    { "--connection-string", "Database:ConnectionString" },
    { "--default-server", "Hades:DefaultServer" },
    { "--default-port", "Hades:DefaultPort" },
    { "--default-username", "Hades:DefaultUsername" },
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

if (args.Contains("--generate-yaml"))
{
    // Get MatrixBridge from DI
    var matrixBridge = builder.Services.BuildServiceProvider().GetRequiredService<MatrixBridge>();
    matrixBridge.GenerateRegistrationFile();
    return; // Exit the application
}

var host = builder.Build();
host.Run();
