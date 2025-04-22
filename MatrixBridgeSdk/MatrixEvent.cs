using MatrixBridgeSdk.Models;
using System;

namespace MatrixBridgeSdk
{
    public class MatrixEventArgs : EventArgs
    {
        public MatrixMessage Message { get; }
        public RemoteRoom RemoteRoom { get; }
        
        public MatrixEventArgs(MatrixMessage message, RemoteRoom room)
        {
            Message = message;
            RemoteRoom = room;
        }
    }
}
