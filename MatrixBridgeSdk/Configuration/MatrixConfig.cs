namespace MatrixBridgeSdk.Configuration
{
    public class MatrixConfig
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string AuthorizationToken { get; set; } = string.Empty;
        public int ListenPort { get; set; } = 9000;
        public string BindAddress { get; set; } = "0.0.0.0";
    }
}
