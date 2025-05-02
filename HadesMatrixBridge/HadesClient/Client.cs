using HadesMatrixBridge.Configuration;
using HadesMatrixBridge.Models;
using MatrixBridgeSdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using EmojiToolkit;

namespace HadesMatrixBridge.HadesClient
{
    class Client
    {

        private readonly List<(Regex, Action<Match>)> _inputHandlers;
        //= new List<(Regex, Action<string>)>
        //{
        //    (new Regex(@"^COMMAND1\s+([^\n]+)", RegexOptions.Compiled | RegexOptions.Singleline), HandleRoomRev),
        //    (new Regex(@"\[ Conversation for.*-\+", RegexOptions.Compiled | RegexOptions.Singleline), HandleRoomRev)
        //};

        private static readonly Regex AnsiRegex = new Regex(@"\x1B\[([0-9]{1,2}(;[0-9]{1,2})?)?[m|K]", RegexOptions.Compiled);
        private static readonly Regex SysLookRegex = new Regex(@"^You are in the.*You can see: ([^\n]*)", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex MyRegexp = new Regex(@"^(>?>?)(\S*) (.*): (.*)", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new Regex(@"\[URL\] ([^:]*): (.*)", RegexOptions.Compiled);
        private static readonly Regex DsayRegEx = new Regex(@"says to (.*)", RegexOptions.Compiled);
        private static readonly Regex EchoRegEx = new Regex(@"^(\(.+\)|-) (.*)", RegexOptions.Compiled);
        private static readonly Regex SysMessageRegEx = new Regex(@"^-> (.*)", RegexOptions.Compiled);
        private static readonly Regex StatusChangeRegex = new Regex(@"^-> (.*) (is away|returns)", RegexOptions.Compiled);
        private static readonly Regex EmoteRegex = new Regex(@"^(>?>?)([a-zA-Z]*) (.*)", RegexOptions.Compiled);
        private static readonly Regex ShoutRegex = new Regex(@"^(!!)([a-zA-Z]*) (.*)", RegexOptions.Compiled);
        private static readonly Regex MovedToIdle = new Regex(@"^(You are in the idle).*", RegexOptions.Compiled);
        private static readonly Regex RoomRevRegex = new Regex(@"^\s*\[ Conversation for.*-\+.*--\+", RegexOptions.Compiled | RegexOptions.Singleline);                     // Done
        private static readonly Regex UserListRegex = new Regex(@"^\s*\[ Users on Hades.*---\+\s+Total of [0-9]+ users online.", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex ReturnFromAfk = new Regex(@"-> You have returned.*", RegexOptions.Compiled);
        private static readonly Regex AlreadyInTheStyx = new Regex(@"-> You are already in.*", RegexOptions.Compiled);
        private static readonly Regex TimeAlertRegex = new Regex(@"-> (BONG! The time is now 09:00)|(13:37 - it's leet o'clock!)|(17:00 - clocking out time!)|(BONG! The time is now 12:00)", RegexOptions.Compiled);
        private static readonly Regex CannotTalkHere = new Regex(@"-> You can't talk here.*", RegexOptions.Compiled);
        private static readonly Regex WillBeMarkedAwayRegex = new Regex(@"-> You will be marked as away in 10 minutes.", RegexOptions.Compiled);

        private static readonly Regex ConnectedHostsRegex = new Regex(@"^\s*\[ Connected Hosts.*--\+.*--\+\s+[0-9]+ connected hosts", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex ConnectedHostsExtractionRegex = new Regex(@"^[0-9]+\s+(\S*)\s+\S+\s+[0-9\.]+\s+\S+$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex GotIncompleteMessageRegEx = new Regex(@"^\s*(?:\[ Conversation (?!.*(----------\+).*?\1)|\[ Users on Hades(?!.*Total of [0-9]+ users online\.))", RegexOptions.Compiled | RegexOptions.Singleline);

        private NetworkStream _stream;

        private string _host { get; init; }
        private int _port { get; init; }
        private int _puppetId { get; init; }
        private string _username { get; init; }
        private string _password { get; init; }
        private string _matrixName { get; set; }

        private bool IsLoggedIn { get; set; }

        private List<string> _currentUsers { get; set; } = new List<string>();
        private MatrixBridgeSdk.MatrixBridge _bridge { get; init; }

        private TelnetRelay _telnetRelay { get; set; }

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

        private void HandleCommand1(string obj)
        {
            throw new NotImplementedException();
        }

        public Client(MatrixBridgeSdk.MatrixBridge bridge, int puppetId, string username = "", 
            string password = "", string hadesName = "", 
            IOptions<HadesConfig> hadesConfig = null, ILoggerFactory loggerFactory = null)
        {
            _hadesConfig = hadesConfig?.Value ?? new HadesConfig();
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

            _inputHandlers = new List<(Regex, Action<Match>)>
            {
                (RoomRevRegex, HandleRoomRev),
                (UserListRegex, HandleWho),
                (ConnectedHostsRegex, HandleHosts),
                (ReturnFromAfk, IgnoreMessage),
                (AlreadyInTheStyx, IgnoreMessage),
                (TimeAlertRegex, IgnoreMessage),
                (CannotTalkHere, HandleCannotTalkHere),
                (WillBeMarkedAwayRegex, HandleWillBeMarkedAway)
            };
        }

        public async Task Start()
        {
            _logger.LogInformation("Starting Hades Client");

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
            // Create TelnetRelay only if enabled in configuration
            if (_hadesConfig.EnableTelnetRelay)
            {
                _telnetRelay = new TelnetRelay(
                    Options.Create(new TelnetConfig { Port = 7000 }),
                    _username,
                    _loggerFactory?.CreateLogger<TelnetRelay>() ?? NullLogger<TelnetRelay>.Instance);

                _telnetRelay.Message += async (sender, e) =>
                {
                    if (_stream is not null)
                    {
                        await _stream.WriteAsync(Encoding.ASCII.GetBytes(e));
                    }
                };

                _ = _telnetRelay.Start();
            }

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

            _stream = stream;

            using StreamReader reader = new StreamReader(stream, Encoding.ASCII);
            string totalData = string.Empty;

            //Read data continuously
            while (!(client.Client.Poll(1, SelectMode.SelectRead) && client.Client.Available == 0))
            {

                byte[] buffer = new byte[4096];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                {

                    if (bytesRead == 7 &&
                       buffer[0] == 239 &&
                       buffer[1] == 191 &&
                       buffer[2] == 189 &&
                       buffer[3] == 239 &&
                       buffer[4] == 191 &&
                       buffer[5] == 189 &&
                       (buffer[6] == 5 || buffer[6] == 1))
                        continue;

                    var readData = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    string cleaned = Regex.Replace(readData, @"[^\x20-\x7E\n]", "");

                    // Remove colour codes
                    string cleanedColour = Regex.Replace(cleaned, @"\[[0-9]{1,2}m", "");

                    readData = cleanedColour;

                    if (!string.IsNullOrWhiteSpace(readData) && readData != "ï¿½ï¿½")
                    {
                        totalData += readData;

                        if (_telnetRelay is not null)
                        {
                            _ = _telnetRelay.Write(readData);
                        }


                        if (!IsLoggedIn)
                        {
                            totalData = HandleLogin(totalData);
                        }
                        else
                        {
                            _logger.LogDebug("Processing data: {Data}", totalData);
                            _logger.LogDebug("----------------------------------");
                            if (GotIncompleteMessageRegEx.IsMatch(AnsiRegex.Replace(totalData, "")))
                            {
                                _logger.LogDebug("Got partial data, awaiting more");
                            }
                            else
                            {
                                _ = ProcessData(totalData);
                                totalData = string.Empty;
                            }
                        }
                    }

                }
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
                _stream.Write(Encoding.ASCII.GetBytes(_username + "\r\n"));
                return string.Empty;
            }
            else if (data.EndsWith("your password :") || data.EndsWith("your password:"))
            {
                _logger.LogDebug("Sending password");
                _stream.Write(Encoding.ASCII.GetBytes(_password));
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
            _logger.LogDebug($"Received: {input}");
            var msg = ParseInput(input);

            if (msg is not null)
            {
                // Ignore if you were the creating user
                if (msg.User == "You" || msg.User.Equals(_username, StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.LogDebug($"DoublePuppet: [{msg.Action}] from you : {(msg.Ignore ? "Ignored" : msg.Text)}");
                    return;
                }

                var room = new RemoteRoom() { RoomId = "hades", Name = "Hades", PuppetId =  _puppetId };
                var user = new RemoteUser() { UserId = msg.User.ToLower().Trim(), Name = msg.User, PuppetId = _puppetId };

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


                        await _bridge.SendMessage(room, user, $"{msg.DirectedTarget}: {msg.Text}", msg.Action == "emote");
                        break;



                    default:
                        await _bridge.SendMessage(room, user, $"({msg.Action}) {msg.Text}", msg.Action == "emote");
                        _logger.LogDebug("[{Action}] from '{User}' : {Text}", msg.Action, msg.User, (msg.Ignore ? "Ignored" : msg.Text));



                        // Find Get Room
                        // Get User
                        // Send to Matrix
                        break;
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
            //}


            _logger.LogDebug("Sending: {Data}", data);

            _stream.Write(Encoding.ASCII.GetBytes(data));

            return true;
        }

        private HadesMessage ParseInput(string input)
        {
            // Strip Ansi
            var cleanText = AnsiRegex.Replace(input, "").Trim();

            // Emojify the input test
            cleanText = Emoji.Emojify(cleanText);
            
            // Handle smileys
            cleanText = cleanText
                .Replace(":)", "🙂")
                .Replace(":(", "☹️")
                .Replace(":|", "😐️")
                .Replace(";)", "😉")
                .Replace(":o", "😲")
                .Replace(":/", "😕")
                .Replace(":p", "😛")
                .Replace("}:8", "🐮");
            
            while (!string.IsNullOrWhiteSpace(cleanText))
            {
                bool matched = false;

                foreach (var (pattern, handler) in _inputHandlers)
                {
                    if (string.IsNullOrWhiteSpace(cleanText))
                    {
                        break;
                    }

                    Match m = pattern.Match(cleanText);
                    if (m.Success)
                    {

                        handler(m); // Pass full matched command to handler


                        cleanText = cleanText.Substring(m.Length).Trim(); // Remove processed command
                        matched = true;
                        break; // Restart processing from the first regex
                    }
                }

                if (!matched)
                {
                    if (cleanText == "??????\u0005")
                    {
                        return null;
                    }


                    //                    Console.WriteLine($"Unrecognized command, skipping line...  :: {cleanText}");
                    int newlineIndex = cleanText.IndexOf('\n');
                    if (newlineIndex != -1)
                        cleanText = cleanText.Substring(newlineIndex + 1).TrimStart();
                    else
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(cleanText))
            {
                _logger.LogDebug("Empty Message - Data already processed");
                return null;
            }

            var msg = new HadesMessage();

            // System Items Regex
            var match = SysLookRegex.Match(cleanText);
            if (match.Success)
            {
                var users = match.Groups[1].Value.Trim().Split(',');
                // Assuming currentUsers is a class member
                _currentUsers.Clear();
                foreach (var user in users)
                {
                    _currentUsers.Add(user.Trim());
                }

                msg.SysMessage = true;
                msg.Action = "look";
                return msg;
            }

            match = ConnectedHostsRegex.Match(cleanText);
            if (match.Success)
            {
                _logger.LogDebug("Connected Users:");
                _currentUsers.Clear();
                foreach (var line in cleanText.Split(Environment.NewLine))
                {
                    match = ConnectedHostsExtractionRegex.Match(line);
                    if (match.Success)
                    {
                        _currentUsers.Add(match.Groups[1].Value);
                        _logger.LogDebug(match.Groups[1].Value);
                    }
                }
                msg.SysMessage = true;
                msg.Action = "hosts";
                return msg;
            }


            // Handle URLs
            match = UrlRegex.Match(cleanText);
            if (match.Success)
            {
                msg.Action = "url";
                msg.Text = match.Groups[2].Value;
                msg.User = match.Groups[1].Value;
                return msg;
            }

            // Shout
            match = ShoutRegex.Match(cleanText);
            if (match.Success)
            {
                _logger.LogDebug("Got a shout!");
                msg.User = match.Groups[2].Value;
                msg.Action = "shouts";
                msg.Emote = true;
                msg.Text = "(Shouting) " + match.Groups[3].Value;
                return msg;
            }

            match = MyRegexp.Match(cleanText);
            if (match.Success)
            {
                msg.Private = match.Groups[1].Value.Length > 0;
                msg.User = match.Groups[2].Value;
                msg.Action = match.Groups[3].Value;
                msg.Text = match.Groups[4].Value;

                // Dsay
                //Console.WriteLine("Original Match Data: " + cleanText);
                //Console.WriteLine("Original Match Results: " + match.ToString());
                var dsayMatch = DsayRegEx.Match(match.Groups[3].Value);

                //Console.WriteLine("Dsay Match Results: " + dsayMatch.ToString());

                if (dsayMatch.Success)
                {
                    msg.Action = "dsay";
                    msg.Directed = true;

                    if (dsayMatch.Groups[1].Value == "you")
                    {
                        msg.DirectedTarget = "@YOU";
                    }
                    else
                    {
                        msg.DirectedTarget = dsayMatch.Groups[1].Value;
                    }
                }
                return msg;
            }

            // Echo
            match = EchoRegEx.Match(cleanText);
            if (match.Success)
            {
                msg.Action = "echo";
                msg.Text = match.Groups[2].Value;

                if (match.Groups[1].Value != "-")
                {
                    msg.User = match.Groups[1].Value;
                }

                return msg;
            }

            // Status Change
            match = StatusChangeRegex.Match(cleanText);
            if (match.Success)
            {
                msg.Private = false;
                msg.User = match.Groups[1].Value;
                msg.Action = match.Groups[2].Value == "is away" ? "away" : "returns";
                msg.Text = "";

                return msg;
            }

            // Moved to Idle
            match = MovedToIdle.Match(cleanText);
            if (match.Success)
            {
                // Assuming userInIdle is a class member
                var userInIdle = true;
                msg.Action = "Moved to Idle";
                msg.Text = "";
                msg.SysMessage = true;
                return msg;
            }

            // System Message
            match = SysMessageRegEx.Match(cleanText);
            if (match.Success)
            {
                msg.Action = "sysMessage";
                msg.Text = match.Groups[1].Value;
                msg.SysMessage = true;
                return msg;
            }

            // Room Rev
            match = RoomRevRegex.Match(cleanText);
            if (match.Success)
            {
                msg.Action = "rev";
                msg.Text = match.Groups[1].Value;
                msg.SysMessage = true;
                msg.Ignore = true;
                return msg;
            }

            // User List
            match = UserListRegex.Match(cleanText);
            if (match.Success)
            {
                msg.Action = "userList";
                msg.Text = match.Groups[1].Value;
                msg.SysMessage = true;
                msg.Ignore = true;
                return msg;
            }

            // Emote
            match = EmoteRegex.Match(cleanText);
            if (match.Success)
            {
                msg.Private = match.Groups[1].Value.Length > 0;
                msg.User = match.Groups[2].Value;
                msg.Action = "emote";
                msg.Emote = true;
                msg.Text = match.Groups[3].Value;

                // Handle special case of "fades into the background"  (Moves to Idle)
                if (!msg.Private && msg.Text == "fades into the background")
                {
                    msg.SysMessage = true;
                }

                return msg;
            }

            if (cleanText.Length > 0)
            {
                if (cleanText == "??????\u0005")
                {
                    msg.Ignore = true;
                    return msg;
                }

                _logger.LogWarning("Unable to handle message: {Text} (Length: {Length})", cleanText, cleanText.Length);
            }

            msg.Action = "Unknown";
            msg.Ignore = true;
            msg.User = "system";
            msg.SysMessage = true;
            msg.Text = cleanText;
            return msg;
        }

        private void HandleRoomRev(Match match)
        {
            _logger.LogDebug("Got Room Rev");
        }

        private void HandleWho(Match match)
        {
            _logger.LogDebug("Got Who command");
        }

        private void HandleHosts(Match match)
        {
            _logger.LogDebug("Got Hosts command");
        }

        private void IgnoreMessage(Match match)
        {
            _logger.LogDebug($"Ignoring {match.Value}");
        }

        private void HandleCannotTalkHere(Match match)
        {
            _logger.LogDebug("Cannot talk here (Idle)");
            _stream.Write(Encoding.ASCII.GetBytes(".go styx"));
        }

        private void HandleWillBeMarkedAway(Match match)
        {
            var shouldPrevent = ShouldPreventIdle();
            _logger.LogDebug("Will be marked away in 10 mins {PreventingIdle}", 
                shouldPrevent ? ":: Preventing Idle" : "");
            
            if (shouldPrevent)
            {
                _stream.Write(Encoding.ASCII.GetBytes(".go styx"));
            }
        }

        internal async Task Stop()
        {
            _logger.LogInformation("Stopping Hades Client");
            if (_telnetRelay is not null)
            {
                _telnetRelay.Stop();
            }

            if (_stream is not null)
            {
                _stream.Close();
                _stream.Dispose();
                _stream = null;
            }

        }
    }
}
