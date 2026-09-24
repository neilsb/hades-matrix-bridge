using HadesMatrixBridge.HadesClient;

namespace HadesMatrixBridge.Tests
{
    public class HadesParserTests
    {
        private readonly HadesParser _parser = new();

        [Fact]
        public void Parses_Say()
        {
            var msg = Assert.Single(_parser.Parse("Bob says: hello there\n"));

            Assert.Equal("Bob", msg.User);
            Assert.Equal("says", msg.Action);
            Assert.Equal("hello there", msg.Text);
            Assert.False(msg.Private);
        }

        [Fact]
        public void Parses_Every_Line_When_Several_Arrive_Together()
        {
            var msgs = _parser.Parse("Bob says: one\nAlice asks: two?\nCarol waves\n");

            Assert.Collection(msgs,
                m => { Assert.Equal("Bob", m.User); Assert.Equal("says", m.Action); Assert.Equal("one", m.Text); },
                m => { Assert.Equal("Alice", m.User); Assert.Equal("asks", m.Action); Assert.Equal("two?", m.Text); },
                m => { Assert.Equal("Carol", m.User); Assert.Equal("emote", m.Action); Assert.Equal("waves", m.Text); });
        }

        [Fact]
        public void Handles_Carriage_Returns_And_Blank_Lines()
        {
            // Telnet control characters are removed before parsing, but blank lines remain
            var msgs = _parser.Parse("\n\nBob says: one\n\n\nAlice says: two\n");

            Assert.Equal(new[] { "one", "two" }, msgs.Select(m => m.Text));
        }

        [Fact]
        public void Parses_Emote()
        {
            var msg = Assert.Single(_parser.Parse("Bob waves at everyone"));

            Assert.Equal("Bob", msg.User);
            Assert.Equal("emote", msg.Action);
            Assert.True(msg.Emote);
            Assert.Equal("waves at everyone", msg.Text);
        }

        [Fact]
        public void Parses_Private_Messages()
        {
            var msgs = _parser.Parse(">>Bob tells you: psst\n>Bob grins at you\n");

            Assert.Collection(msgs,
                m => { Assert.True(m.Private); Assert.Equal("Bob", m.User); Assert.Equal("psst", m.Text); },
                m => { Assert.True(m.Private); Assert.Equal("emote", m.Action); Assert.Equal("grins at you", m.Text); });
        }

        [Fact]
        public void Parses_Directed_Say_To_You()
        {
            var msg = Assert.Single(_parser.Parse("Bob says to you: hi"));

            Assert.Equal("dsay", msg.Action);
            Assert.True(msg.Directed);
            Assert.Equal("@YOU", msg.DirectedTarget);
            Assert.Equal("hi", msg.Text);
        }

        [Fact]
        public void Parses_Directed_Say_To_Someone_Else()
        {
            var msg = Assert.Single(_parser.Parse("Bob says to Alice: hi"));

            Assert.Equal("dsay", msg.Action);
            Assert.Equal("Alice", msg.DirectedTarget);
        }

        [Fact]
        public void Parses_Url_Without_Mangling_It()
        {
            var msg = Assert.Single(_parser.Parse("[URL] Bob: https://example.com/path?q=1"));

            Assert.Equal("url", msg.Action);
            Assert.Equal("Bob", msg.User);
            Assert.Equal("https://example.com/path?q=1", msg.Text);
        }

        [Fact]
        public void Parses_Shout()
        {
            var msg = Assert.Single(_parser.Parse("!!Bob HELLO"));

            Assert.Equal("shouts", msg.Action);
            Assert.Equal("Bob", msg.User);
            Assert.Equal("(Shouting) HELLO", msg.Text);
        }

        [Fact]
        public void Converts_Smileys_To_Emoji()
        {
            var msg = Assert.Single(_parser.Parse("Bob says: hi :) ;)"));

            Assert.Equal("hi 🙂 😉", msg.Text);
        }

        [Fact]
        public void Strips_Ansi_Codes()
        {
            var msg = Assert.Single(_parser.Parse("\u001b[1mBob\u001b[0m says: hello"));

            Assert.Equal("Bob", msg.User);
            Assert.Equal("hello", msg.Text);
        }

        [Theory]
        [InlineData("-> BONG! The time is now 12:00")]
        [InlineData("-> 17:00 - Home time!")]
        [InlineData("-> You have returned from being away")]
        [InlineData("-> You are already in the Styx")]
        [InlineData("+----------------------------+")]
        public void Ignores_Housekeeping_Lines(string line)
        {
            var msgs = _parser.Parse($"Bob says: before\n{line}\nBob says: after\n");

            Assert.Equal(new[] { "before", "after" }, msgs.Select(m => m.Text));
        }

        [Theory]
        [InlineData("-> Bob is away (lunch)", "is away", "lunch")]
        [InlineData("-> Bob returns", "returns", "")]
        [InlineData("-> Bob has joined Hades", "has joined Hades", "")]
        [InlineData("-> Bob has left Hades", "has left Hades", "")]
        public void Parses_Status_Changes_As_Control_Messages(string line, string status, string reason)
        {
            var msgs = _parser.Parse($"Alice says: before\n{line}\nAlice says: after\n");

            Assert.Equal(3, msgs.Count);
            var msg = msgs[1];
            Assert.True(HadesParser.IsControl(msg));
            Assert.Equal(HadesParser.StatusChangeAction, msg.Action);
            Assert.Equal("Bob", msg.User);
            Assert.Equal(status, msg.Text);
            Assert.Equal(reason, msg.Reason);
        }

        [Fact]
        public void Parses_Cannot_Talk_Here_As_Control_Message()
        {
            var msg = Assert.Single(_parser.Parse("-> You can't talk here, you are in the idle"));

            Assert.True(HadesParser.IsControl(msg));
            Assert.Equal(HadesParser.CannotTalkHereAction, msg.Action);
        }

        [Fact]
        public void Parses_Will_Be_Marked_Away_As_Control_Message()
        {
            var msg = Assert.Single(_parser.Parse("-> You will be marked as away in 10 minutes."));

            Assert.Equal(HadesParser.WillBeMarkedAwayAction, msg.Action);
        }

        [Fact]
        public void Parses_Other_System_Messages()
        {
            var msg = Assert.Single(_parser.Parse("-> Something happened"));

            Assert.Equal("sysMessage", msg.Action);
            Assert.True(msg.SysMessage);
            Assert.Equal("Something happened", msg.Text);
        }

        [Fact]
        public void Groups_Consecutive_Unrecognised_Lines()
        {
            var msgs = _parser.Parse("Bob says: before\n12345\n67890\nBob says: after\n");

            Assert.Collection(msgs,
                m => Assert.Equal("before", m.Text),
                m => { Assert.Equal(HadesParser.UnknownAction, m.Action); Assert.Equal("12345\n67890", m.Text); },
                m => Assert.Equal("after", m.Text));
        }

        [Fact]
        public void Keeps_Unrecognised_Lines_In_Order_Before_Control_Messages()
        {
            var msgs = _parser.Parse("12345\n-> Bob returns\n");

            Assert.Collection(msgs,
                m => Assert.Equal(HadesParser.UnknownAction, m.Action),
                m => Assert.Equal(HadesParser.StatusChangeAction, m.Action));
        }

        [Fact]
        public void Ignores_Conversation_Review_Block_And_Parses_What_Follows()
        {
            var input = string.Join("\n",
                "[ Conversation for Styx ]------------------------+",
                "Bob says: an old message",
                "Alice waves",
                "+-----------------------------------------------+",
                "Carol says: a new message",
                "");

            var msg = Assert.Single(_parser.Parse(input));

            Assert.Equal("Carol", msg.User);
            Assert.Equal("a new message", msg.Text);
        }

        [Fact]
        public void Ignores_User_List_Block()
        {
            var input = string.Join("\n",
                "[ Users on Hades ]-------------------------------+",
                "Bob        Styx",
                "Alice      Styx",
                "+-----------------------------------------------+",
                "Total of 2 users online.",
                "Carol says: hi",
                "");

            var msg = Assert.Single(_parser.Parse(input));

            Assert.Equal("Carol", msg.User);
        }

        [Fact]
        public void Parses_Look_And_Records_Current_Users()
        {
            var input = string.Join("\n",
                "You are in the Styx.",
                "A dark and gloomy river.",
                "You can see: Bob, Alice, Carol",
                "Bob says: hi",
                "");

            var msgs = _parser.Parse(input);

            Assert.Collection(msgs,
                m => Assert.Equal("look", m.Action),
                m => Assert.Equal("hi", m.Text));
            Assert.Equal(new[] { "Bob", "Alice", "Carol" }, _parser.CurrentUsers);
        }

        [Fact]
        public void Returns_Nothing_For_Whitespace()
        {
            Assert.Empty(_parser.Parse(" \n \n"));
        }
    }
}
