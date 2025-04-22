namespace MatrixBridgeSdk.Models
{
    public record MatrixRoom
    {
        public int PuppetId { get; set; }
        
        /// <summary>
        /// Matrix Room Id
        /// </summary>
        public string RoomId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Puppets Remote Room Id
        /// </summary>
        public string RemoteRoomId { get; set; } = string.Empty;

        public string? Topic { get; set; }
        public bool isDirect { get; set; }
    }
}
