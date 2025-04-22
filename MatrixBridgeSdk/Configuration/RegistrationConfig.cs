using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace MatrixBridgeSdk.Configuration
{
    public class RegistrationConfig
    {
        public string Id { get; set; } = string.Empty;
        
        [YamlMember(Alias = "hs_token")]
        public string HomeserverToken { get; set; } = string.Empty;
        
        [YamlMember(Alias = "as_token")]
        public string AppServiceToken { get; set; } = string.Empty;
        
        public string Url { get; set; } = string.Empty;
        
        [YamlMember(Alias = "sender_localpart")]
        public string SenderLocalPart { get; set; } = string.Empty;
        
        public NamespaceConfig Namespaces { get; set; } = new();
    }

    public class NamespaceConfig
    {
        public List<UserNamespace> Users { get; set; } = new();
    }

    public class UserNamespace
    {
        public bool Exclusive { get; set; }
        public string Regex { get; set; } = string.Empty;
    }
}