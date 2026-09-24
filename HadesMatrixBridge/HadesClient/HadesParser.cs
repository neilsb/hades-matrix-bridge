using System.Text.RegularExpressions;
using EmojiToolkit;
using HadesMatrixBridge.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HadesMatrixBridge.HadesClient
{
    /// <summary>
    /// Parses text received from Hades into messages.  Input is expected to be complete lines with telnet
    /// control characters already removed (ANSI colour codes are stripped here).
    /// </summary>
    internal class HadesParser
    {
        // Actions for messages that the client must act on, rather than relay to Matrix
        public const string CannotTalkHereAction = "cannotTalkHere";
        public const string WillBeMarkedAwayAction = "willBeMarkedAway";
        public const string StatusChangeAction = "statusChange";
        public const string UnknownAction = "Unknown";

        private static readonly Regex AnsiRegex = new Regex(@"\x1B\[([0-9]{1,2}(;[0-9]{1,2})?)?[m|K]", RegexOptions.Compiled);

        // Multi-line blocks, matched against the start of the remaining text
        private static readonly Regex SysLookRegex = new Regex(@"^You are in the.*?You can see: ([^\n]*)", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex RoomRevRegex = new Regex(@"^\s*\[ Conversation for.*-\+.*--\+", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex UserListRegex = new Regex(@"^\s*\[ Users on Hades.*---\+\s+Total of [0-9]+ users online.", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex ConnectedHostsRegex = new Regex(@"^\s*\[ Connected Hosts.*--\+.*--\+\s+[0-9]+ connected hosts", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex ConnectedHostsExtractionRegex = new Regex(@"^[0-9]+\s+(\S*)\s+\S+\s+[0-9\.]+\s+\S+$", RegexOptions.Compiled | RegexOptions.Multiline);

        // Single lines that are handled without being relayed
        private static readonly Regex ReturnFromAfk = new Regex(@"^-> You have returned.*", RegexOptions.Compiled);
        private static readonly Regex AlreadyInTheStyx = new Regex(@"^-> You are already in.*", RegexOptions.Compiled);
        private static readonly Regex TimeAlertRegex = new Regex(@"^-> (BONG! The time is now \d{2}:\d{2}|\d{2}:\d{2} - .+!)", RegexOptions.Compiled);
        private static readonly Regex CannotTalkHere = new Regex(@"^-> You can't talk here.*", RegexOptions.Compiled);
        private static readonly Regex WillBeMarkedAwayRegex = new Regex(@"^-> You will be marked as away in 10 minutes.", RegexOptions.Compiled);
        private static readonly Regex StatusChangeRegex = new Regex(@"^-> (.*) (is away|returns|has joined Hades|has left Hades|enters)\s*\(?([^\)]*)\)?$", RegexOptions.Compiled);
        private static readonly Regex SeparatorLine = new Regex(@"^\+-+\+$", RegexOptions.Compiled);

        // Single lines that are relayed
        private static readonly Regex MyRegexp = new Regex(@"^(>?>?)(\S*) (.*): (.*)", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new Regex(@"\[URL\] ([^:]*): (.*)", RegexOptions.Compiled);
        private static readonly Regex DsayRegEx = new Regex(@"says to (.*)", RegexOptions.Compiled);
        private static readonly Regex EchoRegEx = new Regex(@"^(\(.+\)|-) (.*)", RegexOptions.Compiled);
        private static readonly Regex SysMessageRegEx = new Regex(@"^-> (.*)", RegexOptions.Compiled);
        private static readonly Regex StatusChangeRegexOld = new Regex(@"^-> (.*) (is away|returns)", RegexOptions.Compiled);
        private static readonly Regex EmoteRegex = new Regex(@"^(>?>?)([a-zA-Z]*) (.*)", RegexOptions.Compiled);
        private static readonly Regex ShoutRegex = new Regex(@"^(!!)([a-zA-Z]*) (.*)", RegexOptions.Compiled);
        private static readonly Regex MovedToIdle = new Regex(@"^(You are in the idle).*", RegexOptions.Compiled);

        private readonly ILogger _logger;

        private readonly List<(Regex, Func<Match, HadesMessage?>)> _blockHandlers;
        private readonly List<(Regex, Func<Match, HadesMessage?>)> _lineHandlers;

        /// <summary>
        /// Users last seen in a "look" or "hosts" listing
        /// </summary>
        public List<string> CurrentUsers { get; } = new();

        public HadesParser(ILogger? logger = null)
        {
            _logger = logger ?? NullLogger.Instance;

            _blockHandlers = new List<(Regex, Func<Match, HadesMessage?>)>
            {
                (RoomRevRegex, m => Ignore(m, "Room Rev")),
                (UserListRegex, m => Ignore(m, "Who")),
                (ConnectedHostsRegex, HandleHosts),
                (SysLookRegex, HandleLook),
            };

            _lineHandlers = new List<(Regex, Func<Match, HadesMessage?>)>
            {
                (ReturnFromAfk, m => Ignore(m)),
                (AlreadyInTheStyx, m => Ignore(m)),
                (TimeAlertRegex, m => Ignore(m)),
                (CannotTalkHere, _ => Control(CannotTalkHereAction)),
                (WillBeMarkedAwayRegex, _ => Control(WillBeMarkedAwayAction)),
                (StatusChangeRegex, HandleStatusChange),
                (SeparatorLine, m => Ignore(m)),
            };
        }

        /// <summary>
        /// Returns true if the message is an instruction for the client rather than something to relay
        /// </summary>
        public static bool IsControl(HadesMessage msg)
            => msg.Action is CannotTalkHereAction or WillBeMarkedAwayAction or StatusChangeAction;

        public IReadOnlyList<HadesMessage> Parse(string input)
        {
            var result = new List<HadesMessage>();
            var unknownLines = new List<string>();

            var text = Normalise(input);

            while (text.Length > 0)
            {
                // Multi-line blocks
                if (TryHandle(_blockHandlers, text, out var blockMsg, out var consumed))
                {
                    FlushUnknown(unknownLines, result);
                    AddIfNotNull(result, blockMsg);
                    text = text.Substring(consumed).TrimStart();
                    continue;
                }

                // Otherwise, take the next line
                var newlineIndex = text.IndexOf('\n');
                var line = (newlineIndex == -1 ? text : text.Substring(0, newlineIndex)).Trim();
                text = newlineIndex == -1 ? string.Empty : text.Substring(newlineIndex + 1).TrimStart();

                if (line.Length == 0)
                {
                    continue;
                }

                if (TryHandle(_lineHandlers, line, out var lineMsg, out _))
                {
                    FlushUnknown(unknownLines, result);
                    AddIfNotNull(result, lineMsg);
                    continue;
                }

                var msg = ParseLine(line);
                if (msg is null)
                {
                    // Group consecutive unrecognised lines (e.g. command output) into a single message
                    unknownLines.Add(line);
                    continue;
                }

                FlushUnknown(unknownLines, result);
                result.Add(msg);
            }

            FlushUnknown(unknownLines, result);
            return result;
        }

        private static string Normalise(string input)
        {
            // Strip Ansi
            var cleanText = AnsiRegex.Replace(input, "").Trim();

            // Emojify the input text
            cleanText = Emoji.Emojify(cleanText);

            // Handle smileys
            return cleanText
                .Replace(":)", "🙂")
                .Replace(":(", "☹️")
                .Replace(":|", "😐️")
                .Replace(";)", "😉")
                .Replace(":o", "😲")
                .Replace(" :/", " 😕")      // Space is a quick fix to prevent replacement in URLs.  Needs improving
                .Replace(":p", "😛")
                .Replace("}:8", "🐮");
        }

        /// <summary>
        /// Try each handler against the start of the text
        /// </summary>
        /// <param name="msg">Message produced by the handler, if any</param>
        /// <param name="consumed">Length of text handled</param>
        private static bool TryHandle(List<(Regex, Func<Match, HadesMessage?>)> handlers, string text,
            out HadesMessage? msg, out int consumed)
        {
            foreach (var (pattern, handler) in handlers)
            {
                var m = pattern.Match(text);
                if (m.Success && m.Index == 0)
                {
                    msg = handler(m);
                    consumed = m.Length;
                    return true;
                }
            }

            msg = null;
            consumed = 0;
            return false;
        }

        private static void AddIfNotNull(List<HadesMessage> result, HadesMessage? msg)
        {
            if (msg is not null)
            {
                result.Add(msg);
            }
        }

        private void FlushUnknown(List<string> unknownLines, List<HadesMessage> result)
        {
            if (unknownLines.Count == 0)
            {
                return;
            }

            var text = string.Join("\n", unknownLines);
            unknownLines.Clear();

            _logger.LogWarning("Unable to handle message: {Text} (Length: {Length})", text, text.Length);

            result.Add(new HadesMessage
            {
                Action = UnknownAction,
                Ignore = true,
                User = "system",
                SysMessage = true,
                Text = text
            });
        }

        /// <summary>
        /// Parse a single line of conversation.  Returns null if the line isn't recognised.
        /// </summary>
        private HadesMessage? ParseLine(string line)
        {
            var msg = new HadesMessage();

            // Handle URLs
            var match = UrlRegex.Match(line);
            if (match.Success)
            {
                msg.Action = "url";
                msg.Text = match.Groups[2].Value;
                msg.User = match.Groups[1].Value;
                return msg;
            }

            // Shout
            match = ShoutRegex.Match(line);
            if (match.Success)
            {
                _logger.LogDebug("Got a shout!");
                msg.User = match.Groups[2].Value;
                msg.Action = "shouts";
                msg.Emote = true;
                msg.Text = "(Shouting) " + match.Groups[3].Value;
                return msg;
            }

            match = MyRegexp.Match(line);
            if (match.Success)
            {
                msg.Private = match.Groups[1].Value.Length > 0;
                msg.User = match.Groups[2].Value;
                msg.Action = match.Groups[3].Value;
                msg.Text = match.Groups[4].Value;

                // Dsay
                var dsayMatch = DsayRegEx.Match(match.Groups[3].Value);
                if (dsayMatch.Success)
                {
                    msg.Action = "dsay";
                    msg.Directed = true;
                    msg.DirectedTarget = dsayMatch.Groups[1].Value == "you" ? "@YOU" : dsayMatch.Groups[1].Value;
                }

                return msg;
            }

            // Echo
            match = EchoRegEx.Match(line);
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
            match = StatusChangeRegexOld.Match(line);
            if (match.Success)
            {
                msg.Private = false;
                msg.User = match.Groups[1].Value;
                msg.Action = match.Groups[2].Value == "is away" ? "away" : "returns";
                msg.Text = "";
                return msg;
            }

            // Moved to Idle
            match = MovedToIdle.Match(line);
            if (match.Success)
            {
                msg.Action = "Moved to Idle";
                msg.Text = "";
                msg.SysMessage = true;
                return msg;
            }

            // System Message
            match = SysMessageRegEx.Match(line);
            if (match.Success)
            {
                msg.Action = "sysMessage";
                msg.Text = match.Groups[1].Value;
                msg.SysMessage = true;
                return msg;
            }

            // Emote
            match = EmoteRegex.Match(line);
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

            return null;
        }

        private HadesMessage? Ignore(Match match, string? description = null)
        {
            _logger.LogDebug("Ignoring {Description}", description ?? match.Value);
            return null;
        }

        private static HadesMessage Control(string action)
            => new HadesMessage { Action = action, SysMessage = true, Ignore = true };

        private HadesMessage? HandleHosts(Match match)
        {
            _logger.LogDebug("Got Hosts command");

            CurrentUsers.Clear();
            foreach (Match user in ConnectedHostsExtractionRegex.Matches(match.Value))
            {
                CurrentUsers.Add(user.Groups[1].Value);
            }

            return null;
        }

        private HadesMessage HandleLook(Match match)
        {
            CurrentUsers.Clear();
            foreach (var user in match.Groups[1].Value.Trim().Split(','))
            {
                CurrentUsers.Add(user.Trim());
            }

            return new HadesMessage { Action = "look", SysMessage = true };
        }

        private static HadesMessage HandleStatusChange(Match match)
            => new HadesMessage
            {
                Action = StatusChangeAction,
                User = match.Groups[1].Value,
                Text = match.Groups[2].Value,
                Reason = match.Groups[3].Value,
                SysMessage = true,
                Ignore = true
            };
    }
}
