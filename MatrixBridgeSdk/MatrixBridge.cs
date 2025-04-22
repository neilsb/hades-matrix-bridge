﻿﻿using Markdig;
using MatrixBridgeSdk.Configuration;
using MatrixBridgeSdk.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LiteDB;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace MatrixBridgeSdk
{
    public class Msg
    {
        public string body { get; set; }
        public bool isEmote { get; set; }
    }

    public partial class MatrixBridge
    {
        public string Domain { get; private set; } = string.Empty;

        public event EventHandler<PuppetEventArgs> PuppetNew;
        public event EventHandler<PuppetEventArgs>? PuppetUpdate;
        public event EventHandler<PuppetEventArgs>? PuppetUnlinked;

        private ConcurrentDictionary<int, Puppet> _puppets = new ConcurrentDictionary<int, Puppet>();

        // Declare the Connected event using EventHandler.
        public event EventHandler Connected;

        // Declare the Message event using EventHandler<T> with MessageEventArgs.
        public event EventHandler<MatrixEventArgs> Message;

        private WebApplication _matrixWebApp;

        private string _matrixServerUrl;
        private string _accessToken;
        private string _authorizationToken;
        private readonly int _listenPort;
        private readonly string _webServiceBindAddress;

        private ILiteDatabase _database;

        private ILogger<MatrixBridge> _logger;

        private ConcurrentDictionary<string, string> _pendingInvites = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<string, Queue<Msg>> _pendingMessages = new ConcurrentDictionary<string, Queue<Msg>>();

        private readonly HttpClient _httpClient;

        public MatrixBridge(
            ILiteDatabase liteDatabase,
            IHttpClientFactory httpClientFactory,
            ILogger<MatrixBridge> logger,
            IOptions<MatrixConfig> matrixConfig)
        {
            var config = matrixConfig.Value;
            _matrixServerUrl = config.ServerUrl;
            _accessToken = config.AccessToken;
            _authorizationToken = config.AuthorizationToken;
            _listenPort = config.ListenPort;
            _webServiceBindAddress = config.BindAddress;

            _httpClient = httpClientFactory.CreateClient();
            _database = liteDatabase;
            _logger = logger;
        }

        // Protected virtual method to raise the Connected event.
        protected virtual void OnConnected()
        {
            // Only raise the event if there are any subscribers.
            Connected?.Invoke(this, EventArgs.Empty);
        }

        // Protected virtual method to raise the Message event.
        protected virtual void OnMessage(MatrixMessage message, RemoteRoom room)
        {
            Message?.Invoke(this, new MatrixEventArgs(message, room));
        }

        public async Task Connect()
        {
            // Perform connection logic...
            _logger.LogInformation("Connecting...");

            // Start up an API Webservice listening on configured port
            await StartWebService(_webServiceBindAddress, _listenPort);

            // TODO: Send a Ping

            // Send a WhoAmI to confirm connected and get domain
            var result = await WhoAmI();

            if (!string.IsNullOrEmpty(result) && result.Contains(":"))
            {
                Domain = result.Split(':').Last(); // Extract the domain part after the colon
            }
            else
            {
                _logger.LogWarning("Failed to extract domain from WhoAmI result.");
                Domain = string.Empty;
            }

            OnConnected(); // Raise the Connected event.

            // Check for any Puppets to start up
            foreach (var p in _database.GetCollection<Puppet>().Find(x => x.Deleted == false))
            {
                _puppets.AddOrUpdate(p.Id, p, (key, oldValue) => p);
                PuppetNew?.Invoke(this, new PuppetEventArgs(p.Id, p.Data ?? new Dictionary<string, string?>()));
            }
        }

        private async Task<bool> StopWebService()
        {
            // Stop the API server.
            _logger.LogInformation("Stopping Webservice");
            await _matrixWebApp.StopAsync();
            return true;
        }

        private async Task StartWebService(string bindAddress, int port)
        {
            var builder = WebApplication.CreateBuilder(new string[0]);

            // Configure minimal API endpoints
            _matrixWebApp = builder.Build();

            // Add the authorisation middleware
            _matrixWebApp.UseMiddleware<AuthorizationMiddleware>(Options.Create(_authorizationToken));

            // Default endpoint
            _matrixWebApp.MapGet("/", () => "");

            // Add the Status endpoint
            _matrixWebApp.MapPost("/_matrix/app/v1/ping", (HttpContext context) =>
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("{}");
            });

            // Add the "users" endpoint
            _matrixWebApp.MapPost("/_matrix/app/v1/users/{userId}", async (HttpContext context, string userId) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                _logger.LogInformation($"UserId: {userId}");
                _logger.LogInformation($"Body: {body}");
                context.Response.StatusCode = 404;
                return Task.CompletedTask;
            });

            // Transaction endpoint
            _matrixWebApp.MapPut("/_matrix/app/v1/transactions/{txnId}", async (HttpContext context, string txnId) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();

                var transaction = JsonSerializer.Deserialize<HSTransaction>(body);


                // Save Transaction (Testing)
                //if (!Directory.Exists("received_transactions"))
                //{
                //    Directory.CreateDirectory("received_transactions");
                //}
                //var fileName = $"{DateTime.UtcNow.ToString("yyyyMMdd_HHmmss.FFFFFFF")}_{txnId}";
                //File.WriteAllText($"received_transactions\\{fileName}.json", JsonSerializer.Serialize(transaction, new JsonSerializerOptions { WriteIndented = true }));
                //File.WriteAllText($"received_transactions\\{fileName}.raw.json", body);

                //_logger.LogInformation($"Received TransactionId: {txnId}  :: {fileName} ");


                // Process event
                if (transaction != null)
                {
                    foreach (var e in transaction.events)
                    {
                        await ProcessClientEvent(e);
                    }
                }

                context.Response.StatusCode = 200;
                return Task.CompletedTask;
            });

            _matrixWebApp.MapFallback((context) =>
            {
                _logger.LogWarning($"Path '{context.Request.Path}' not found");

                if (context.Request.ContentLength > 0)
                {
                    using var reader = new StreamReader(context.Request.Body);
                    var body = reader.ReadToEndAsync().Result;
                    _logger.LogInformation($"Body: {body}");
                }

                context.Response.StatusCode = 404;
                return Task.CompletedTask;
            });

            // Start the API server.
            _matrixWebApp.RunAsync($"http://{bindAddress}:{port}");
        }

        private async Task<bool> ProcessClientEvent(HSClientEvent e)
        {
            _logger.LogInformation($"Processing Event: {e.event_id}  ({e.type}");

            switch (e.type)
            {
                case "m.room.member": // Room Membership Event
                    if (e.state_key == $"@{Constants.BotUsername}:{Domain}")
                    {
                        // For Bridge Bot User
                        if (e.content.TryGetProperty("membership", out JsonElement membershipElement) &&
                            membershipElement.GetString() == "invite")
                        {
                            _logger.LogInformation("Membership is invite");

                            // Handle the invite logic here - Always join and say hello
                            if (await JoinRoom(e))
                            {
                                await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id,
                                    "Hello! I'm the Hades Bridge Bot. I'm here to help you link your Hades account with your Matrix account. Type `help` for a list of commands you can use.");
                            }
                        }

                        break;
                    }

                    if (e.state_key is not null && e.state_key.StartsWith($"@{Constants.UserPrefix}"))
                    {
                        // For other users
                        if (e.content.TryGetProperty("membership", out JsonElement membershipElement) &&
                            membershipElement.GetString() == "invite")
                        {
                            _logger.LogInformation($"User {e.state_key} invited to room {e.room_id}.   Accepting");

                            // Handle the invite logic here - Always join
                            await JoinRoom(e);

                            // Set username in room
                            bool shouldSetNameHere = true;
                            //await SetUserDisplayName(remoteUser, new MatrixRoom() { });

                            // Process any "Queued" messages

                        }

                        break;
                    }

                    break;

                case "m.room.message": // Room message
                    if (e.sender == $"@{Constants.BotUsername}:{Domain}")
                    {
                        // Ignore messages from the bot
                        return true;
                    }

                    // See if this is a known room
                    var room = _database.GetCollection<MatrixRoom>().Find(x => x.RoomId == e.room_id).FirstOrDefault();

                    if (room?.Name == "Hades")
                    {
                        room.RemoteRoomId = "hades";
                    }

                    if (room is not null)
                    {
                        // Ignore messages sent from any of the bridge bots
                        if (e.sender.StartsWith($"@{Constants.UserPrefix}{room.PuppetId}"))
                        {
                            return true;
                        }


                        // Despatch event to be handled by Puppet Client
                        MatrixMessage msg = new MatrixMessage();

                        e.content.TryGetProperty("body", out JsonElement bodyElement);
                        msg.Body = bodyElement.GetString() ?? "";

                        if (e.content.TryGetProperty("formatted_body", out JsonElement formattedBody))
                        {
                            msg.FormattedBody = formattedBody.GetString();
                        }

                        e.content.TryGetProperty("msgtype", out JsonElement typeElement);
                        msg.Emote = (typeElement.GetString() ?? "") == "m.emote";



                        OnMessage(msg, new RemoteRoom()
                        {
                            IsDirect = room.isDirect,
                            Name = room.Name,
                            PuppetId = room.PuppetId,
                            RoomId = room.RemoteRoomId,
                            Topic = room?.Topic ?? String.Empty
                        });

                        return true;
                    }


                    // Otherwise let the bot handle it
                    await HandleAdminRoomMessage(e);
                    break;



                default:
                    _logger.LogWarning($"Don't know how to handle {e.type} event");
                    break;
            }

            return true;

        }


        private async Task<bool> JoinRoom(HSClientEvent e)
        {
            var requestUrl = $"{_matrixServerUrl}/_matrix/client/v3/rooms/{e.room_id}/join?user_id={e.state_key}";

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            // Prepare the registration payload.
            request.Content = JsonContent.Create(new { });

            try
            {
                var response = await _httpClient.SendAsync(request);

                using var reader = new StreamReader(response.Content.ReadAsStream());
                var body = await reader.ReadToEndAsync();

                _logger.LogInformation($"Join Room Response: {response.StatusCode} - {body}");

                if (response.IsSuccessStatusCode)
                {
                    // Set the display name after joining
                    if (_pendingInvites.TryGetValue($"{e.room_id}//{e.state_key}", out var displayName))
                    {
                        await SetUserDisplayName(e.state_key, e.room_id, displayName);
                        _pendingInvites.TryRemove($"{e.room_id}//{e.state_key}", out _);
                    }

                    // Send any queued messaged
                    if (_pendingMessages.TryGetValue($"{e.room_id}//{e.state_key}", out var messages))
                    {
                        _logger.LogDebug($"Sending {messages.Count} queued messages");

                        foreach (var message in messages)
                        {
                            await SendMessage(e.state_key, e.room_id, message.body, message.isEmote);
                        }

                        _pendingMessages.TryRemove($"{e.room_id}//{e.state_key}", out _);
                    }
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error Joining Room: {ex.Message}");
                return false;
            }
        }

        private async Task HandleAdminRoomMessage(HSClientEvent e)
        {
            // Extract the message
            e.content.TryGetProperty("body", out JsonElement bodyElement);
            var message = bodyElement.GetString();

            _logger.LogInformation($"Message to admin: {message}");

            // TODO: Not sure about parameters being optional during edit.  Confusing to

            if (message.Trim().Equals("help", StringComparison.CurrentCultureIgnoreCase))
            {
                await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id, @"I know the following commands:
- `link <username> <password> [matrix name]` - Link a Hades account _(`Matrix Name` is optional and if set to your display name in a room will generate pings when it is mentioned in a message, etc)_
- `list` - List all your puppets and their IDs
- `edit <puppet_id> [username] [password] [matrix name]` - Edit an existing puppet. All parameters except puppet_id are optional.
- `unlink <puppet_id>` - Remove a linked account", true);
                return;
            }

            if (message.Trim().Equals("list", StringComparison.CurrentCultureIgnoreCase))
            {
                var userPuppets = _database.GetCollection<Puppet>()
                    .Find(x => x.Owner == e.sender && !x.Deleted)
                    .ToList();

                if (!userPuppets.Any())
                {
                    await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id, "You don't have any puppets.",
                        true);
                    return;
                }

                var puppetList = "Your puppets:\n";
                foreach (var puppet in userPuppets)
                {
                    puppetList +=
                        $"- ID: {puppet.Id}, Username: {puppet.Data["username"]}, Matrix Name: {puppet.Data.GetValueOrDefault("matrixName", "not set")}\n";
                }

                await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id, puppetList, true);
                return;
            }

            // Define the regex patterns
            var linkPattern = @"^link\s+(?<username>\S+)\s+(?<password>\S+)(?:\s+(?<matrixName>\S+))?$";
            var editPattern =
                @"^edit\s+(?<puppetId>\d+)(?:\s+(?<username>\S+))?(?:\s+(?<password>\S+))?(?:\s+(?<matrixName>\S+))?$";
            var linkRegex = new Regex(linkPattern, RegexOptions.IgnoreCase);
            var editRegex = new Regex(editPattern, RegexOptions.IgnoreCase);
            var unlinkRegex = new Regex(@"^unlink\s+(?<puppetId>\d+)$", RegexOptions.IgnoreCase);


            // Check if the message matches the edit pattern
            var editMatch = editRegex.Match(message);
            if (editMatch.Success)
            {
                var puppetIdStr = editMatch.Groups["puppetId"].Value;
                if (!int.TryParse(puppetIdStr, out int puppetId))
                {
                    await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id, "Invalid puppet ID format.",
                        true);
                    return;
                }

                // Find the existing puppet by ID and verify ownership
                var existingPuppet = _database.GetCollection<Puppet>()
                    .Find(x => x.Id == puppetId && !x.Deleted)
                    .FirstOrDefault();

                if (existingPuppet == null)
                {
                    await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id,
                        $"No puppet found with ID {puppetId}.", true);
                    return;
                }

                // Verify ownership
                if (existingPuppet.Owner != e.sender)
                {
                    await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id,
                        "You don't have permission to edit this puppet.", true);
                    return;
                }

                // Get optional new values
                var username = editMatch.Groups["username"].Success ? editMatch.Groups["username"].Value : null;
                var password = editMatch.Groups["password"].Success ? editMatch.Groups["password"].Value : null;
                var matrixName = editMatch.Groups["matrixName"].Success ? editMatch.Groups["matrixName"].Value : null;

                // Update only provided values
                if (username != null)
                {
                    existingPuppet.Data["username"] = username;
                }

                if (password != null)
                {
                    existingPuppet.Data["password"] = password;
                }

                if (matrixName != null)
                {
                    existingPuppet.Data["matrixName"] = matrixName;
                }

                // Update the puppet in the database
                _database.GetCollection<Puppet>().Update(existingPuppet);

                await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id,
                    $"Updated puppet {puppetId}. Changes will take effect after restart.", true);

                // Raise event to restart the puppet
                PuppetUpdate?.Invoke(this, new PuppetEventArgs(existingPuppet.Id, existingPuppet.Data));
                return;
            }

            // Check if the message matches the link pattern
            var linkMatch = linkRegex.Match(message);
            if (linkMatch.Success)
            {
                // Extract the values into variables
                var username = linkMatch.Groups["username"].Value;
                var password = linkMatch.Groups["password"].Value;
                var matrixName = linkMatch.Groups["matrixName"].Success ? linkMatch.Groups["matrixName"].Value : null;

                _logger.LogInformation($"Username: {username}");
                _logger.LogInformation($"Password: {password}");
                _logger.LogInformation($"Matrix Name: {matrixName}");

                // Handle the link logic here
                await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id,
                    $"Linking account for user {username} with matrix name {matrixName ?? "not provided"}.", true);

                // Get current Max Puppet Id
                var puppetId = 0;
                try
                {
                    _database.GetCollection<Puppet>().Max(x => x.Id);
                }
                catch
                {
                }

                var puppet = new Puppet()
                {
                    Id = puppetId++,
                    Owner = e.sender,
                    Data = new Dictionary<string, string?>()
                    {
                        { "username", username },
                        { "password", password },
                        { "matrixName", matrixName }
                    }
                };

                _database.GetCollection<Puppet>().Insert(puppet);

                return;
            }

            // Handle the "unlink" logic here
            var unlinkMatch = unlinkRegex.Match(message);
            if (unlinkMatch.Success)
            {
                var puppetIdStr = unlinkMatch.Groups["puppetId"].Value;
                if (!int.TryParse(puppetIdStr, out int puppetId))
                {
                    await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id, "Invalid puppet ID format.");
                    return;
                }

                var existingPuppet = _database.GetCollection<Puppet>()
                    .Find(x => x.Id == puppetId)
                    .FirstOrDefault();

                if (existingPuppet is null)
                {
                    await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id, "Invalid puppet ID.");
                    return;
                }

                // Verify ownership
                if (existingPuppet.Owner != e.sender)
                {
                    await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id,
                        "You don't have permission to edit this puppet.");
                    return;
                }

                // Check if already deleted
                if (existingPuppet.Deleted)
                {
                    await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id,
                        "This puppet is already unlinked.");
                    return;
                }

                // Otherwise Unlink
                existingPuppet.Deleted = true;
                _database.GetCollection<Puppet>()
                    .Update(existingPuppet);

                // Raise event to restart the puppet
                PuppetUnlinked?.Invoke(this, new PuppetEventArgs(existingPuppet.Id, existingPuppet.Data));

                await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id,
                    $"Puppet *{puppetId}* unlinked.", true);

            }

            await SendMessage($"@{Constants.BotUsername}:{Domain}", e.room_id,
                "I'm sorry, I don't know how to handle that command. Type `help` for a list of commands you can use.");

        }

        private async Task<bool> GetJoinedRoomsAsync()
        {
            var requestUrl = $"{_matrixServerUrl}/_matrix/client/v3/joined_rooms";

            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            try
            {
                var response = await _httpClient.SendAsync(request);

                using var reader = new StreamReader(response.Content.ReadAsStream());
                var body = await reader.ReadToEndAsync();

                _logger.LogInformation($"Send Message Response: {response.StatusCode} - {body}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error Sending message: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendMessage(RemoteRoom remoteRoom, RemoteUser remoteUser, string message,
            bool isEmote = false)
        {
            // Lookup Matrix User
            var matrixUserId = $"@{Constants.UserPrefix}{remoteUser.PuppetId}_{remoteUser.UserId}:{Domain}";
            MatrixUser? destUser = _database.GetCollection<MatrixUser>()
                .Find(x => x.UserId == matrixUserId && x.PuppetId == remoteUser.PuppetId).FirstOrDefault();

            if (destUser is null)
            {
                if (await CreateUser(remoteUser))
                {
                    destUser = _database.GetCollection<MatrixUser>()
                        .Find(x => x.UserId == matrixUserId && x.PuppetId == remoteUser.PuppetId).FirstOrDefault();
                }
            }

            // Lookup Room
            var destRoom = _database.GetCollection<MatrixRoom>()
                .Find(x => x.Name == remoteRoom.RoomId && x.PuppetId == remoteRoom.PuppetId).FirstOrDefault();

            // Create Room if not found
            if (destRoom is null)
            {
                _logger.LogInformation($"Creating room '{remoteRoom.RoomId}' for Puppet {remoteRoom.PuppetId}");

                // Create Room
                //await CreateMatrixRoomAsync(remoteRoom.PuppetId, new List<string>() { remoteUser.UserId }, remoteRoom.Name, remoteRoom.Topic, !remoteRoom.IsDirect);
                await CreateMatrixRoomAsync(remoteRoom, new List<string>() { remoteUser.UserId });
                destRoom = _database.GetCollection<MatrixRoom>()
                    .Find(x => x.Name == remoteRoom.RoomId && x.PuppetId == remoteRoom.PuppetId).FirstOrDefault();
            }

            // TODO: Ideally Only do this if changed...
            await SetUserDisplayName(remoteUser, destRoom);

            // Send Message &
            // Check for User Error
            if (!await SendMessage(destUser.UserId, destRoom.RoomId, message, isEmote: isEmote))
            {
                // Handle Error
                int i = 1;

                // Save the message to the queue for resending when the user joins the room
                var key = $"{destRoom.RoomId}//{destUser.UserId}";
                var queue = _pendingMessages.GetOrAdd(key, _ => new Queue<Msg>());

                queue.Enqueue(new Msg()
                {
                    body = message,
                    isEmote = isEmote
                });

                // Try adding user to the room and sending again
                await InviteUserToRoom(destRoom.RoomId, $"{destUser.UserId}", remoteUser.Name);

                await SendMessage(destUser.UserId, remoteRoom.RoomId, message);
            }

            // Resend Message
            return true;
        }

        private async Task<bool> SendMessage(string userId, string roomId, string message, bool markdown = false,
            bool isEmote = false)
        {
            if (!Helpers.MatrixIdValidator.IsValidMatrixId(userId))
            {
                throw new ArgumentException("Invalid Matrix ID format.");
            }

            // TODO: Remove
            if (!userId.EndsWith(Domain))
            {
                userId = $"@{Constants.UserPrefix}3_{userId.ToLower().Trim()}:{Domain}";
            }

            var requestUrl =
                $"{_matrixServerUrl}/_matrix/client/v3/rooms/{roomId}/send/m.room.message/{DateTime.UtcNow.Ticks}?user_id={userId}";

            var request = new HttpRequestMessage(HttpMethod.Put, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            // Prepare the registration payload.
            if (!markdown)
            {
                request.Content = JsonContent.Create(new
                {
                    body = message,
                    msgtype = isEmote ? "m.emote" : "m.text"
                });
            }
            else
            {
                request.Content = JsonContent.Create(new
                {
                    body = message,
                    format = "org.matrix.custom.html",
                    msgtype = isEmote ? "m.emote" : "m.text",
                    formatted_body = Markdown.ToHtml(message)
                });
            }

            try
            {
                var response = await _httpClient.SendAsync(request);

                using var reader = new StreamReader(response.Content.ReadAsStream());
                var body = await reader.ReadToEndAsync();

                // Way to detect if the user is not in the room and then invite
                if (!response.IsSuccessStatusCode)
                {
                    // TODO: bool inviteUserToRoom = true;

                    // Store Content in Dictionary

                    // Invite user to room

                }

                _logger.LogInformation($"Send Message Response: {response.StatusCode} - {body}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error Sending message: {ex.Message}");
                return false;
            }
        }


        private async Task<bool> InviteUserToRoom(string roomId, string userId, string? displayName = null)
        {
            _logger.LogInformation($"Inviting User '{userId}' to room {roomId}");

            string requestUrl = $"{_matrixServerUrl}/_matrix/client/v3/rooms/{roomId}/invite";

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            request.Content = JsonContent.Create(new
            {
                user_id = userId
            });

            // If a DisplayName was passed, store in pending invites so we can set the Display Name after joining
            if (!string.IsNullOrEmpty(displayName))
            {
                _pendingInvites.AddOrUpdate($"{roomId}//{userId}", displayName, (key, oldValue) => displayName);
            }

            try
            {
                var response = await _httpClient.SendAsync(request);

                using var reader = new StreamReader(response.Content.ReadAsStream());
                var body = await reader.ReadToEndAsync();

                _logger.LogInformation($"Invite User Response: {response.StatusCode} - {body}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error Inviting User to Room : {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetUserDisplayName(RemoteUser user, MatrixRoom room)
            => await SetUserDisplayName($"@{Constants.UserPrefix}{user.PuppetId}_{user.UserId}:{Domain}", room.RoomId,
                user.Name);

        private async Task<bool> SetUserDisplayName(string userId, string roomId, string displayName)
        {

            _logger.LogInformation($"Setting user Display Name for `{userId}`");


            var requestUrl =
                $"{_matrixServerUrl}/_matrix/client/v3/rooms/{roomId}/state/m.room.member/{userId}?user_id={userId}";


            var request = new HttpRequestMessage(HttpMethod.Put, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            request.Content = JsonContent.Create(new
            {
                membership = "join", // Required to ensure the user remains in the room
                displayname = displayName
            });

            try
            {
                var response = await _httpClient.SendAsync(request);

                using var reader = new StreamReader(response.Content.ReadAsStream());
                var body = await reader.ReadToEndAsync();


                _logger.LogInformation($"Set user Display Name: {response.StatusCode} - {body}");


                if (!response.IsSuccessStatusCode)
                {
                    int i = 1;
//                    await CreateUser(userId);

                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error Sending message: {ex.Message}");
                return false;
            }

        }

        public bool GenerateRegistrationFile()
        {
            var yamlFilePath = Path.Combine(AppContext.BaseDirectory, "data",
                $"{Constants.BridgeName}-registration.yaml");

            // Ensure the directory exists
            var directoryName = Path.GetDirectoryName(yamlFilePath);
            if (directoryName != null)
            {
                Directory.CreateDirectory(directoryName);
            }

            // Backup existing YAML file if it exists
            if (File.Exists(yamlFilePath))
            {
                var backupFilePath = yamlFilePath + ".backup_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Move(yamlFilePath, backupFilePath);
            }

            // Generate secure random tokens
            var hsToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var asToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

            // Retrieve the server's primary IP address
            var hostName = Dns.GetHostName();
            var ipAddress = Dns.GetHostAddresses(hostName)
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";

            // Construct the URL
            var url = $"http://{ipAddress}:{_listenPort}/";

            // Create registration config object
            var registrationConfig = new RegistrationConfig
            {
                Id = Constants.BridgeName,
                HomeserverToken = hsToken,
                AppServiceToken = asToken,
                Url = url,
                SenderLocalPart = Constants.BotUsername,
                Namespaces = new NamespaceConfig
                {
                    Users = new List<UserNamespace>
                    {
                        new UserNamespace
                        {
                            Exclusive = true,
                            Regex = $"@{Constants.UserPrefix}.*"
                        }
                    }
                }
            };

            // Serialize to YAML
            var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var yaml = serializer.Serialize(registrationConfig);

            // Write the YAML file
            File.WriteAllText(yamlFilePath, yaml);

            _logger.LogInformation($"YAML file generated at: {yamlFilePath}");
            Console.WriteLine($"YAML file generated at: {yamlFilePath}");
            return true;
        }

    }
}
