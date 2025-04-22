using System;
using System.Collections.Generic;

namespace MatrixBridgeSdk.Models
{
    public record Puppet
    {
        public int Id { get; set; }
        public string Owner { get; set; } = string.Empty;
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public bool Deleted { get; set; }
        public Dictionary<string, string?> Data { get; set; } = new Dictionary<string, string?>();
    }
}
