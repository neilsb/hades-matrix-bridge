using HadesMatrixBridge.Configuration;
using HadesMatrixBridge.Models;
using MatrixBridgeSdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using EmojiToolkit;

namespace HadesMatrixBridge.HadesClient
{
    class Client
    {

        private static readonly Regex AnsiRegex = new Regex(@"\x1B\[([0-9]{1,2}(;[0-9]{1,2})?)?[m|K]", RegexOptions.Compiled);
        private static readonly Regex GotIncompleteMessageRegEx = new Regex(@"^\s*(?:\[ Conversation (?!.*(----------\+).*?\1)|\[ Users on Hades(?!.*Total of [0-9]+ users online\.))", RegexOptions.Compiled | RegexOptions.Singleline);

        // How long to wait for the rest of a line before processing a partial line anyway
        private static readonly TimeSpan PartialLineFlushDelay = TimeSpan.FromMilliseconds(250);

        // Stop waiting for an incomplete multi-line block once this much data is buffered
        private const int MaxPendingLength = 64 * 1024;

        private readonly HadesParser _parser;

        // Data received but not yet processed (e.g. the start of a line whose end hasn't arrived yet)
        private string _pending = string.Empty;

        private NetworkStream? _stream;
        private readonly object _streamWriteLock = new();

        private string _host { get; init; }
        private int _port { get; init; }
        private int _puppetId { get; init; }
        private string _username { get; init; }
        private string _password { get; init; }
        private string _matrixName { get; set; }

        private bool IsLoggedIn { get; set; }

        private MatrixBridgeSdk.MatrixBridge _bridge { get; init; }

        private readonly TelnetConfig _telnetConfig;
        private TelnetProxy? _telnetProxy;

        private readonly List<TimeRange> _preventIdleRanges = new();
        private bool ShouldPreventIdle()
        {
            if (string.IsNullOrEmpty(_hadesConfig.PreventIdle))
                return false;

            var currentTime = TimeOnly.FromDateTime(DateTime.Now);
            return _preventIdleRanges.Any(range => range.Contains(currentTime));
        }

        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;

        private readonly HadesConfig _hadesConfig;

        private DateTime _passThroughSystemMessagesUntil = DateTime.MinValue;

        public void ShowSystemMessages(TimeSpan timeSpan)
        {
            _passThroughSystemMessagesUntil = DateTime.Now.Add(timeSpan);
        }
        
        private void HandleCommand1(string obj)
        {
            throw new NotImplementedException();
        }

        public Client(MatrixBridgeSdk.MatrixBridge bridge, int puppetId, string username = "", 
            string password = "", string hadesName = "", 
            IOptions<HadesConfig> hadesConfig = null, ILoggerFactory loggerFactory = null,
            IOptions<TelnetConfig>? telnetConfig = null)
        {
            _hadesConfig = hadesConfig?.Value ?? new HadesConfig();
            _telnetConfig = telnetConfig?.Value ?? new TelnetConfig();
            _host = _hadesConfig.Server;
            _port = _hadesConfig.Port;
            _puppetId = puppetId;
            _username = username;
            _password = password;
            _bridge = bridge;
            _matrixName = hadesName;
            _logger = loggerFactory?.CreateLogger<Client>() ?? NullLogger<Client>.Instance;
            _loggerFactory = loggerFactory;

            // Parse the prevent idle ranges
            if (!string.IsNullOrEmpty(_hadesConfig.PreventIdle))
            {
                foreach (var rangeStr in _hadesConfig.PreventIdle.Split(','))
                {
                    var range = TimeRange.Parse(rangeStr.Trim());
                    if (range != null)
                    {
                        _preventIdleRanges.Add(range);
                    }
                    else
                    {
                        _logger.LogWarning("Invalid time range format: {Range}", rangeStr);
                    }
                }
            }

            IsLoggedIn = !_hadesConfig.AutoLogin;

            _parser = new HadesParser(loggerFactory?.CreateLogger<HadesParser>());
        }

        public async Task Start()
        {
            _logger.LogInformation("Starting Hades Client");

            if (_telnetConfig.Enabled)
            {
                StartTelnetProxy();
            }

            // Reconnect when stopped
            while (true)        // Zoit - Set a break
            {

                await Connect();

                // TODO: Ensure primary user is in room
            }
            //            _ = UpdateUsers();

        }

        private async Task Connect()
        {
            using TcpClient client = new TcpClient();
            _logger.LogInformation("Connecting to {Host}:{Port}...", _host, _port);

            try
            {

                await client.ConnectAsync(_host, _port);
                _logger.LogInformation("Connected to Hades server");

            }
            catch (SocketException ex)
            {
                _logger.LogError("Error connecting to Hades server: {ErrorMessage}", ex.Message);
                return;
            }

            using NetworkStream stream = client.GetStream();
            var rawLog = _hadesConfig.DebugRawLogging
                ? new RawDataLog(_hadesConfig.RawLogDirectory, _puppetId, _logger)
                : null;

            _stream = stream;
            _pending = string.Empty;

            // Use a single decoder so multi-byte characters split across reads are decoded correctly
            var decoder = Encoding.UTF8.GetDecoder();
            byte[] buffer = new byte[4096];
            char[] chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
            Task<int>? readTask = null;

            // Read data continuously until the server closes the connection
            while (true)
            {
                readTask ??= stream.ReadAsync(buffer, 0, buffer.Length);

                // If part of a line is waiting, give the rest of it a short time to arrive, then process it anyway
                if (IsLoggedIn && _pending.Length > 0 &&
                    await Task.WhenAny(readTask, Task.Delay(PartialLineFlushDelay)) != readTask)
                {
                    await ProcessPending(flush: true);
                    continue;
                }

                int bytesRead = await readTask;
                readTask = null;

                if (bytesRead == 0)
                {
                    _logger.LogInformation("Disconnected from Hades server");
                    _stream = null;
                    break;
                }

                // Capture before decoding, filtering or handling login/telnet data.
                if (rawLog is not null)
                {
                    await rawLog.WriteAsync(buffer.AsMemory(0, bytesRead));
                }

                // Pass everything to telnet proxy clients exactly as received
                _telnetProxy?.Broadcast(buffer.AsSpan(0, bytesRead));

                if (bytesRead == 7 &&
                   buffer[0] == 239 &&
                   buffer[1] == 191 &&
                   buffer[2] == 189 &&
                   buffer[3] == 239 &&
                   buffer[4] == 191 &&
                   buffer[5] == 189 &&
                   (buffer[6] == 5 || buffer[6] == 1))
                    continue;

                var readData = new string(chars, 0, decoder.GetChars(buffer, 0, bytesRead, chars, 0));

                string cleaned = Regex.Replace(readData, @"[^\x20-\x7E\n]", "");

                // Remove colour codes
                readData = Regex.Replace(cleaned, @"\[[0-9]{1,2}m", "");

                if (readData.Length == 0)
                {
                    continue;
                }

                _pending += readData;
                await ProcessPending(flush: false);
            }
        }

        /// <summary>
        /// Process buffered data.  Once logged in, only complete lines are processed unless <paramref name="flush"/>
        /// is set, in which case any partial line is processed too.
        /// </summary>
        private async Task ProcessPending(bool flush)
        {
            if (!IsLoggedIn)
            {
                // Login prompts don't end with a newline, so process everything received
                _pending = HandleLogin(_pending);
                return;
            }

            // Wait for the rest of multi-line blocks (e.g. conversation review, who list)
            if (_pending.Length < MaxPendingLength &&
                GotIncompleteMessageRegEx.IsMatch(AnsiRegex.Replace(_pending, "")))
            {
                _logger.LogDebug("Got partial data, awaiting more");
                return;
            }

            var end = flush ? _pending.Length : _pending.LastIndexOf('\n') + 1;
            if (end == 0)
            {
                return;
            }

            var data = _pending.Substring(0, end);
            _pending = _pending.Substring(end);

            if (string.IsNullOrWhiteSpace(data))
            {
                return;
            }

            try
            {
                await ProcessData(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing data from Hades: {Data}", data);
            }
        }

        private string HandleLogin(string input)
        {
            var data = AnsiRegex.Replace(input, "").Trim();

            _logger.LogDebug("Processing login: {Data}", data);
            _logger.LogDebug("----------------------------------");

            if (data.EndsWith("name:"))
            {
                _logger.LogDebug("Sending username");
                WriteToHades(Encoding.ASCII.GetBytes(_username + "\r\n"));
                return string.Empty;
            }
            else if (data.EndsWith("your password :") || data.EndsWith("your password:"))
            {
                _logger.LogDebug("Sending password");
                WriteToHades(Encoding.ASCII.GetBytes(_password));
                return string.Empty;
            }
            else if (data.StartsWith("Greetings,") || data.StartsWith("-> You are already logged in, switching to old session..."))
            {
                // Get User List
                //var sys_lookRegex = / You can see: (.*) / g
                //var match = sys_lookRegex.exec(dataIn);
                //if (match != null)
                //{
                //    const users = match[1].trim().split(",");
                //    this.currentUsers = [];
                //    for (const user of users) {
                //        this.currentUsers.push(user.toLocaleLowerCase().trim());
                //    }
                //}
                this.IsLoggedIn = true;
                //this.emit("connected");
                return string.Empty;
            }
            else
            {
                return input;
            }

        }

        private async Task UpdateUsers()
        {
            while (true)
            {
                await Task.Delay(10000);
                _logger.LogDebug("Updating Users");
            }
        }

        private async Task ProcessData(string input)
        {
            _logger.LogDebug("Processing data: {Data}", input);
            _logger.LogDebug("----------------------------------");

            var msgs = _parser.Parse(input);
            var relayedCount = 0;

            foreach (var msg in msgs)
            {
                if (HadesParser.IsControl(msg))
                {
                    HandleControlMessage(msg);
                    continue;
                }

                relayedCount++;

                // Ignore if you were the creating user
                if (msg.User == "You" || msg.User.Equals(_username, StringComparison.InvariantCultureIgnoreCase))
                {
                    // TODO: Check this is not a message sent by the bridge before double puppeting
                    _logger.LogDebug("DoublePuppet: [{Action}] from you : {Text}", msg.Action,
                        msg.Ignore ? "Ignored" : msg.Text);
                    continue;
                }

                var room = new RemoteRoom() { RoomId = "hades", Name = "Hades", PuppetId = _puppetId };
                var user = new RemoteUser()
                    { UserId = msg.User.ToLower().Trim(), Name = msg.User, PuppetId = _puppetId };

                switch (msg.Action)
                {
                    case "hosts":
                        _ = UpdateCurrentUsers();
                        break;


                    case "emote":
                    case "say":
                    case "says":
                    case "asks":
                    case "exclaims":

                        await _bridge.SendMessage(room, user, msg.Text, msg.Action == "emote");
                        break;


                    case "dsay":

                        if (!string.IsNullOrEmpty(_matrixName))
                        {
                            if (msg.DirectedTarget == "@YOU")
                            {
                                msg.DirectedTarget = _matrixName;
                            }

                            var userReplace = new Regex(@$"\b{_username}\b", RegexOptions.IgnoreCase);

                            msg.Text = userReplace.Replace(msg.Text, _matrixName);

                        }
                        await _bridge.SendMessage(room, user, $"{msg.DirectedTarget}: {msg.Text}",
                            msg.Action == "emote");
                        break;

                    case "url":
                        await _bridge.SendMessage(room, user, msg.Text, msg.Action == "emote");
                        break;


                    default:
                        await _bridge.SendMessage(room, user, $"({msg.Action}) {msg.Text}", msg.Action == "emote");
                        _logger.LogDebug("[{Action}] from '{User}' : {Text}", msg.Action, msg.User,
                            (msg.Ignore ? "Ignored" : msg.Text));
                        break;
                }
            }

            if (relayedCount == 0)
            {
                // Throw away the data unless a command has recently been executed
                // and all response data should be piped through
                if (_passThroughSystemMessagesUntil > DateTime.Now)
                {
                    await _bridge.SendMessage(
                        new RemoteRoom() { RoomId = "hades", Name = "Hades", PuppetId =  _puppetId },
                        new RemoteUser() { UserId = "system", Name = "System", PuppetId = _puppetId }
                        , $"<pre>{input}</pre>", isMarkdown: true);
                }
            }
        }

        private async Task UpdateCurrentUsers()
        {
            // await _bridge.SetUserDisplayName(1, "aurious", "aurious");
        }

        public bool SendMessage(string data)
        {
            if (_stream is null)
            {
                _logger.LogWarning("Stream is null, cannot send message");
                return false;
            }

            // Replace some smileys with emojis
            data = (data + " ").Replace("🙂", ":)")
                .Replace("☹️", ":(")
                .Replace("😐️", ":|")
                .Replace("😉", ";)")
                .Replace("😲", ":o")
                .Replace("😕", ":/")
                .Replace("😛", ":p")
                .Replace("🐮", "}:8").Trim();

            // Catch any others, and Demojify them
            data = Emoji.Demojify(Emoji.Asciify(data));
            
            // Check if user is in the Idle first
            //if(this.userInIdle == true) {
            //    // Move to Styx before talking
            //    this.client.write(".go styx");
            //    this.userInIdle = false;
            //}:


            _logger.LogDebug("Sending: {Data}", data);

            return WriteToHades(Encoding.ASCII.GetBytes(data));
        }

        /// <summary>
        /// Write to the Hades connection.  Writes come from several threads (Matrix messages, the read loop and
        /// telnet proxy clients), so are serialised to stop them interleaving.
        /// </summary>
        /// <returns>false if not connected</returns>
        private bool WriteToHades(byte[] data)
        {
            lock (_streamWriteLock)
            {
                if (_stream is null)
                {
                    _logger.LogWarning("Not connected to Hades, cannot send data");
                    return false;
                }

                try
                {
                    _stream.Write(data);
                    return true;
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    _logger.LogWarning("Error sending data to Hades: {Error}", ex.Message);
                    return false;
                }
            }
        }

        private void StartTelnetProxy()
        {
            if (!IPAddress.TryParse(_telnetConfig.BindAddress, out var bindAddress))
            {
                _logger.LogWarning("Invalid telnet proxy bind address '{BindAddress}', using 0.0.0.0",
                    _telnetConfig.BindAddress);
                bindAddress = IPAddress.Any;
            }

            // Each puppet has its own Hades connection, so gets its own port
            var port = _telnetConfig.Port + _puppetId - 1;

            var proxy = new TelnetProxy(bindAddress, port, _telnetConfig.ReadOnly, data => WriteToHades(data),
                _loggerFactory?.CreateLogger<TelnetProxy>());

            try
            {
                proxy.Start();
                _telnetProxy = proxy;
                _logger.LogInformation("Telnet proxy for puppet {PuppetId} ({Username}) is on port {Port}",
                    _puppetId, _username, proxy.Port);
            }
            catch (SocketException ex)
            {
                _logger.LogError("Unable to start telnet proxy on {Address}:{Port}: {Error}", bindAddress, port,
                    ex.Message);
            }
        }

        private void HandleControlMessage(HadesMessage msg)
        {
            switch (msg.Action)
            {
                case HadesParser.CannotTalkHereAction:
                    HandleCannotTalkHere();
                    break;

                case HadesParser.WillBeMarkedAwayAction:
                    HandleWillBeMarkedAway();
                    break;

                case HadesParser.StatusChangeAction:
                    HandleStatusChange(msg);
                    break;
            }
        }

        private void HandleCannotTalkHere()
        {
            _logger.LogDebug("Cannot talk here (Idle)");
            WriteToHades(Encoding.ASCII.GetBytes(".go styx"));
        }

        private void HandleWillBeMarkedAway()
        {
            var shouldPrevent = ShouldPreventIdle();
            _logger.LogDebug("Will be marked away in 10 mins {PreventingIdle}", 
                shouldPrevent ? ":: Preventing Idle" : "");
            
            if (shouldPrevent)
            {
                WriteToHades(Encoding.ASCII.GetBytes(".go styx"));
            }
        }
        
        private void HandleStatusChange(HadesMessage msg)
        {
            switch (msg.Text)
            {
                case "returns":
                case "enters":
                    _logger.LogDebug("STATUS CHANGE: {User} -- ONLINE", msg.User);
                    //SetPresenceAsync
                    break;
                
                case "is away":
                    _logger.LogDebug("STATUS CHANGE: {User} -- IDLE  {Reason}", msg.User, msg.Reason);
                    break;

                default:
                    _logger.LogDebug("STATUS CHANGE: {User} -- UNKNOWN  ({Status})", msg.User, msg.Text);
                    break;
            }
        }

        internal async Task Stop()
        {
            _logger.LogInformation("Stopping Hades Client");
            if (_telnetProxy is not null)
            {
                await _telnetProxy.StopAsync();
                _telnetProxy = null;
            }

            lock (_streamWriteLock)
            {
                if (_stream is not null)
                {
                    _stream.Close();
                    _stream.Dispose();
                    _stream = null;
                }
            }

        }
    }
}
