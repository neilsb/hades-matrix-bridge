using System.Collections.Generic;
namespace MatrixBridgeSdk.Models
{
    /// <summary>
    /// Transaction from Homeserver
    /// </summary>
    internal class HSTransaction
    {
        /// <summary>
        /// List of ephemeral data, if the receive_ephemeral setting was enabled in the registration file.
        /// There are only three event types that can currently occur in this list: m.presence, m.typing, and m.receipt.Room-scoped ephemeral data (m.typing and m.receipt) MUST include a room_id property to identify the room that they were sent in.
        /// This property can be omitted if it would be empty.
        /// </summary>
        public IEnumerable<HSEvent>? ephemeral { get; set; }

        /// <summary>
        /// List of events, formatted as per the Client-Server API.
        /// </summary>
        public IEnumerable<HSClientEvent> events { get; set; }
    }
}
