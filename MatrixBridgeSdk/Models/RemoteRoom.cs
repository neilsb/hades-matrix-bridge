namespace MatrixBridgeSdk.Models
{
    public class RemoteRoom
    {
        /// <summary>
        /// Remote Room  Id - Unique for remote system (So unique within Puppet Id)
        /// </summary>
        public string RoomId { get; set; }
        /// <summary>
        /// Remote Room Display Name
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Topic in Remote Room
        /// </summary>
        public string Topic { get; set; }
        
        /// <summary>
        /// Is a direct conversation, rather than a public room
        /// </summary>
        public bool IsDirect { get; set; }

        /// <summary>
        /// Puppet Remote Room belongs to
        /// </summary>
        public int PuppetId { get; set; }

    }
}
