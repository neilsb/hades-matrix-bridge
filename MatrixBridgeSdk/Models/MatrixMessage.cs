namespace MatrixBridgeSdk.Models
{
    public class MatrixMessage
    {
        public string Body { get; set; }
        public string? FormattedBody { get; set; }
        public bool Emote { get; set; }
        public bool Notice { get; set; }
        public string? EventId { get; set; }
    }
}
