﻿namespace HadesMatrixBridge.Configuration
{
    public class HadesConfig
    {
        public string DefaultServer { get; set; }
        public int DefaultPort { get; set; }
        public string DefaultUsername { get; set; }
        public string PreventIdle { get; set; } = "";  // Format: "09:00-12:00,13:00-18:00"
        public bool EnableTelnetRelay { get; set; } = false;
        public bool AutoLogin { get; set; } = true;
    }
}
