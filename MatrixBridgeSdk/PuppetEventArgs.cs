using System.Collections.Generic;

namespace MatrixBridgeSdk
{
    public class PuppetEventArgs
    {
        public int PuppetId { get; }
        public Dictionary<string, string?> Data{ get; }

        public PuppetEventArgs(int puppetId, Dictionary<string, string?> data)
        {
            PuppetId = puppetId;
            Data = data;
        }
    }
}

