namespace HadesMatrixBridge.Tests
{
    public class MentionSeparatorTests
    {
        [Theory]
        [InlineData("Bob: hello", "Bob hello")]
        [InlineData("Bob (Away): hello", "Bob hello")]
        [InlineData("Bob: see https://example.com :)", "Bob see https://example.com :)")]
        [InlineData("Bob: meet at 10:30?", "Bob meet at 10:30?")]
        [InlineData("Bob (Away): note: bring cake", "Bob note: bring cake")]
        [InlineData("@bob:example.org: hello", "@bob:example.org hello")]
        [InlineData("Bob Smith: hello", "Bob Smith hello")]
        [InlineData("Bob:", "Bob")]
        public void Removes_Only_The_Mention_Separator(string input, string expected)
        {
            Assert.Equal(expected, HadesBridgeWorker.StripMentionSeparator(input));
        }
    }
}
