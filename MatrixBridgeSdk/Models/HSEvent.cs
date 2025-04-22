using System.Text.Json;

namespace MatrixBridgeSdk.Models
{
    internal class HSEvent
    {
        /// <summary>
        /// The body of this event, as created by the client which sent it.
        /// </summary>
        public JsonElement content { get; set; }

        /// <summary>
        /// Type of event. This SHOULD be namespaced similar to Java package naming conventions e.g. ‘com.example.subdomain.event.type’
        /// </summary>
        public string type { get; set; }
    }
}
