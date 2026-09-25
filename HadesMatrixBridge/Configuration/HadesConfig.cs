namespace HadesMatrixBridge.Configuration
{
    public class HadesConfig
    {
        public string Server { get; set; } = "hades-talker.org";
        public int Port { get; set; } = 6660;
        public string PreventIdle { get; set; } = "";  // Format: "09:00-12:00,13:00-18:00"
        public bool AutoLogin { get; set; } = true;
        public bool DebugRawLogging { get; set; } = false;
        public string RawLogDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "logs", "hades-raw");
    }
}
