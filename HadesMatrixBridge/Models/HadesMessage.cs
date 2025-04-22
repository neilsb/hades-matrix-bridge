namespace HadesMatrixBridge.Models
{
    internal class HadesMessage
    {
        public string User { get; set; } = "system";
        public string Action { get; set; } = string.Empty;
        public bool Emote { get; set; }
        public bool SysMessage { get; set; }
        public bool Directed { get; set; }
        public string DirectedTarget { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public bool Private { get; set; }
        public bool Ignore { get; set; }
    }
}
