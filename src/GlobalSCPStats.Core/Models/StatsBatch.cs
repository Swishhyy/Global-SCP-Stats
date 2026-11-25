using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GlobalSCPStats.Core.Models;

[DataContract]
public sealed class StatsBatch
{
    [DataMember(Name = "schema_version", Order = 1)]
    public int SchemaVersion { get; set; } = 2;

    [DataMember(Name = "batch_id", Order = 2)]
    public string BatchId { get; set; } = Guid.NewGuid().ToString("D");

    [DataMember(Name = "server_id", Order = 3)]
    public string ServerId { get; set; } = string.Empty;

    [DataMember(Name = "created_at", Order = 4)]
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    [DataMember(Name = "players", Order = 5)]
    public List<PlayerStats> Players { get; set; } = new List<PlayerStats>();
}
