namespace MatrixBridgeSdk.Models
{
    public class RemoteUser
    {
        /// <summary>
        /// Remote User Id - Unique for remote system (So unique within Puppet Id)
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Remote Users Display Name
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Puppet Remote User belongs to
        /// </summary>
        public int PuppetId { get; set; }
    }
}
