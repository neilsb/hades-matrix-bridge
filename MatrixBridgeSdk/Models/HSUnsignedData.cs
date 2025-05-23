﻿namespace MatrixBridgeSdk.Models
{
    internal class HSUnsignedData
    {
        /// <summary>
        /// Time in milliseconds that has elapsed since the event was sent.This field is generated by the local homeserver, and may be incorrect if the local time on at least one of the two servers is out of sync, which can cause the age to either be negative or greater than it actually is.
        /// </summary>
        public int age { get; set; }

        /// <summary>
        /// Room membership of the user making the request, at the time of the event.
        /// </summary>
        /// <remarks>
        /// This property is the value of the membership property of the requesting user’s m.room.member state at the point of the event, including any changes caused by the event. If the user had yet to join the room at the time of the event (i.e, they have no m.room.member state), this property is set to leave.
        /// Homeservers SHOULD populate this property wherever practical, but they MAY omit it if necessary (for example, if calculating the value is expensive, servers might choose to only implement it in encrypted rooms). The property is not normally populated in events pushed to application services via the application service transaction API (where there is no clear definition of “requesting user”).
        /// Added in v1.11
        /// </remarks>
        public string membership { get; set; }

        /// <summary>
        /// Previous content for this event. This field is generated by the local homeserver, and is only returned if the event is a state event, and the client has permission to see the previous content.
        /// </summary>
        public string prev_content { get; set; }

        /// <summary>
        /// Event that redacted this event, if any.
        /// </summary>
        public string redacted_because { get; set; }

        /// <summary>
        /// Client-supplied transaction ID, for example, provided via PUT /_matrix/client/v3/rooms/{roomId
        /// </summary>
        public string transaction_id { get; set; }
    }
}
