namespace MatrixBridgeSdk.Models
{
    internal class HSClientEvent : HSEvent
    {
        /// <summary>
        /// Globally unique identifier for this event.
        /// </summary>
        public string event_id { get; set; }

        /// <summary>
        /// Timestamp (in milliseconds since the unix epoch) on originating homeserver when this event was sent.
        /// </summary>
        public long origin_server_ts { get; set; }

        /// <summary>
        /// ID of the room associated with this event.
        /// </summary>
        public string room_id { get; set; }

        /// <summary>
        /// Fully-qualified ID of the user who sent this event.
        /// </summary>
        public string sender { get; set; }

        /// <summary>
        /// Present if, and only if, this event is a state event. The key making this piece of state unique in the room. Note that it is often an empty string.
        /// </summary>
        /// <remarks>
        /// State keys starting with an @ are reserved for referencing user IDs, such as room members.With the exception of a few events, state events set with a given user’s ID as the state key MUST only be set by that user.
        /// </remarks>
        public string? state_key { get; set; }

        /// <summary>
        /// Type of the event.
        /// </summary>
        public string type { get; set; }

        /// <summary>
        /// Optional extra information about the event.
        /// </summary>
        public HSUnsignedData? UnsignedData { get; set; }
    }
}
