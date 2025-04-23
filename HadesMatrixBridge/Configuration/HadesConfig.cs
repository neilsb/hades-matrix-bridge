﻿namespace HadesMatrixBridge.Configuration
{
    public class HadesConfig
    {
        public string Server { get; set; } = "hades-talker.org";
        public int Port { get; set; } = 6660;
        public string PreventIdle { get; set; } = "";  // Format: "09:00-12:00,13:00-18:00"
        public bool EnableTelnetRelay { get; set; } = false;
        public bool AutoLogin { get; set; } = true;
    }
}
