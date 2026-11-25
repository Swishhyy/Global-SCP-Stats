using System.Collections.Generic;
using GlobalSCPStats.Core.Models;
using System.Runtime.Serialization;

namespace GlobalSCPStats.Core.Rewards;

[DataContract]
public sealed class ProgressProfile
{
    [DataMember(Name = "user_id")]
    public string UserId { get; set; } = string.Empty;

    [DataMember(Name = "total_xp")]
    public long TotalXp { get; set; }

    [DataMember(Name = "level")]
    public int Level { get; set; } = 1;

    [DataMember(Name = "revision")]
    public long Revision { get; set; }

    [DataMember(Name = "server_stats")]
    public PlayerStats? ServerStats { get; set; }

    // Accepted for older API responses; reward decisions use server-local rules.
    [DataMember(Name = "reward_ids")]
    public List<string> RewardIds { get; set; } = new List<string>();

    public ProgressProfile Copy() => new ProgressProfile
    {
        UserId = UserId, TotalXp = TotalXp, Level = Level, Revision = Revision,
        RewardIds = new List<string>(RewardIds), ServerStats = ServerStats?.Copy(),
    };
}
