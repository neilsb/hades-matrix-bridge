using HadesMatrixBridge.Configuration;
using MatrixBridgeSdk;
using Microsoft.Extensions.Options;

namespace HadesMatrixBridge
{
    public class HadesBridgeWorker : BackgroundService
    {
        private readonly ILogger<HadesBridgeWorker> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly HadesConfig _hadesConfig;
        private MatrixBridge _bridge;

        private readonly IDictionary<int, HadesClient.Client> _puppetClients = new Dictionary<int, HadesClient.Client>();

        public HadesBridgeWorker(
                        ILoggerFactory loggerFactory,
                        IConfiguration config,
                        IServiceProvider serviceProvider,
                        IOptions<HadesConfig> hadesConfig)
        {
            _logger = loggerFactory.CreateLogger<HadesBridgeWorker>();
            _loggerFactory = loggerFactory;
            //var homeServerConfig = new HomeServerConfig();
            //config.GetRequiredSection("HomeServer").Bind(homeServerConfig);

            //homeServerProviderService.GetHomeServer(homeServerConfig);

            _serviceProvider = serviceProvider;
            _hadesConfig = hadesConfig.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //while (!stoppingToken.IsCancellationRequested)
            //{
            //    if (_logger.IsEnabled(LogLevel.Information))
            //    {
            //        _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            //    }
            //    await Task.Delay(1000, stoppingToken);
            //}

            // Configure Bridge Provider
            _logger.LogInformation("Starting HadesBridgeWorker");

            // TODO: Validate Configuration 

            // Read Configuration
            _bridge = _serviceProvider.GetRequiredService<MatrixBridge>();



            _bridge.PuppetNew += OnPuppetNew;
            _bridge.PuppetUpdate += async (sender, e) =>
            {
                if (_puppetClients.TryGetValue(e.PuppetId, out var client))
                {
                    await client.Stop();
                    _puppetClients.Remove(e.PuppetId);
                }

                // Create new client with updated credentials
                HadesClient.Client hadesClient = new(_bridge,
                    e.PuppetId,
                    e.Data["username"],
                    e.Data["password"],
                    e.Data.GetValueOrDefault("matrixName"),
                    Options.Create(_hadesConfig),
                    _loggerFactory);
                
                _puppetClients[e.PuppetId] = hadesClient;
                _ = hadesClient.Start();
            };

            _bridge.PuppetUnlinked += async (sender, e) =>
            {
                if (_puppetClients.TryGetValue(e.PuppetId, out var client))
                {
                    await client.Stop();
                    _puppetClients.Remove(e.PuppetId);
                }
            };

            //_bridge.newPuppy = (PuppetEventArgs args) =>
            //{
            //    _logger.LogInformation("New Puppy: {PuppetId}", args.PuppetId);
            //};


            // Subscribe to the Connected event.
            _bridge.Connected += (sender, e) =>
            {
                _logger.LogInformation("Connected event triggered!");
            };

            // Subscribe to the Message event.
            _bridge.Message += (sender, e) =>
            {
                _logger.LogInformation("Message event triggered with message: " + e.Message.Body);


                // this is called every time we receive a message from matrix and need to
                // forward it to the remote protocol.

                // First we check if the puppet exists
                if (!_puppetClients.ContainsKey(e.RemoteRoom.PuppetId))
                {
                    return;
                }

                var p = _puppetClients[e.RemoteRoom.PuppetId];

                _logger.LogDebug("Sending message to room {RoomId}", e.RemoteRoom.RoomId);
                _logger.LogDebug("Message content: {MessageBody}", e.Message.Body);

                var directed = false;
                var message = e.Message.Body;

                // Check for Directed Message
                if (e.Message.FormattedBody?.StartsWith("<a href=\"https://matrix.to") ?? false)
                {
                    directed = true;
                    message = message.Replace("(Away):", "");       // Handle users that are AFK
                    message = message.Replace(":", "");
                }

                if (string.IsNullOrEmpty(e.RemoteRoom.RoomId) || e.RemoteRoom.RoomId == "hades")
                {

                    if (e.Message.Emote)
                    {
                        p.SendMessage((directed ? ".emoteto " : ".emote ") + message);
                        return;
                    }

                    if (message.StartsWith('/'))
                    {
                        p.SendMessage($".{message.Substring(1)}");
                        //this.showSystemMessages = true;
                        //this.delay(20000).then(() => {
                        //    this.showSystemMessages = false;
                        //});
                        return;
                    }


                    p.SendMessage((directed ? ".sayto " : ".say ") + message);

                }
                else
                {
                    // It's a private item
                    if (e.Message.Emote)
                    {
                        p.SendMessage($".temote {e.RemoteRoom.RoomId} {message}");
                        return;
                    }

                    p.SendMessage($".tell {e.RemoteRoom.RoomId} {message}");
                }
            };

            _logger.LogInformation("Connecting to bridge...");

            await _bridge.Connect();

            // Check for user
            _logger.LogInformation("Checking for user");


//            if (!(await _bridge.UserExists(Constants.BotUsername)))
//            {
//            _logger.LogInformation("Creating user");
////            await _bridge.CreateUser(Constants.BotUsername);
//            }
//            else
//            {
//                _logger.LogInformation("User exists");
//            }

            // Ensure main hades room is created
            //await _bridge.CreateMatrixRoomAsync("hades_main", "Hades Main Room", false);

            _logger.LogInformation("Everything Started - Just chilling");
        }

        private void OnPuppetNew(object? sender, PuppetEventArgs e)
        {
            _logger.LogInformation($"New Puppet:  {e.PuppetId}");

            if (_puppetClients.ContainsKey(e.PuppetId))
            {
                _logger.LogWarning($"Warning:  Puppet with Id {e.PuppetId} already exists ");
            }

            // Check the main Hades Room exists in Matrix

            // Start Hades Client
            HadesClient.Client hadesClient = new(_bridge,
                e.PuppetId,
                e.Data["username"],       // Hades Username
                e.Data["password"],
                e.Data.GetValueOrDefault("matrixName"),
                Options.Create(_hadesConfig),
                _loggerFactory);
            _puppetClients[e.PuppetId] = hadesClient;
            _ = hadesClient.Start();
        }
    }
}
