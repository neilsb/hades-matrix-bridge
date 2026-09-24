﻿namespace HadesMatrixBridge.Configuration
{
    /// <summary>
    /// Telnet proxy, allowing telnet clients to share the bridge's connection to Hades
    /// </summary>
    public class TelnetConfig
    {
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Port for the first puppet's proxy.  Each puppet listens on Port + (puppet id - 1)
        /// </summary>
        public int Port { get; set; } = 7000;

        public string BindAddress { get; set; } = "0.0.0.0";

        /// <summary>
        /// Ignore anything sent by telnet clients
        /// </summary>
        public bool ReadOnly { get; set; } = true;
    }
}
