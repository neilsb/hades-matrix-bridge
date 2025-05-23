﻿namespace MatrixBridgeSdk.Configuration
{
    public class MatrixConfig
    {
        public string ServerUrl { get; init; } = string.Empty;
        public string AccessToken { get; init; } = string.Empty;
        public string AuthorizationToken { get; init; } = string.Empty;
        public int ListenPort { get; init; } = 9000;
        public string BindAddress { get; init; } = "0.0.0.0";

        public string BridgeName { get; init; } = "hades-bridge";
        public string BotUsername { get; init; } = "hadesbot";
        public string BotDisplayName { get; init; } = "HadesBridgeBot";
        public string UserPrefix { get; init; } = "_hades_";
    }
}
