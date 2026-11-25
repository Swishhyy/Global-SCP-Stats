using System;
using System.Runtime.Serialization;
using System.Collections.Generic;

namespace GlobalSCPStats.Core.Models;

[DataContract]
public sealed class PlayerStats
{
    [DataMember(Name = "user_id", Order = 1)]
    public string UserId { get; set; } = string.Empty;

    [DataMember(Name = "playtime_seconds", Order = 2)]
    public double PlaytimeSeconds { get; set; }

    [DataMember(Name = "rounds_played", Order = 3)]
    public long RoundsPlayed { get; set; }

    [DataMember(Name = "kills", Order = 4)]
    public long Kills { get; set; }

    [DataMember(Name = "deaths", Order = 5)]
    public long Deaths { get; set; }

    [DataMember(Name = "escapes", Order = 6)]
    public long Escapes { get; set; }

    [DataMember(Name = "actions", Order = 7)]
    public Dictionary<string, long> Actions { get; set; } = [];

    [DataMember(Name = "xp_earned", Order = 8)]
    public long XpEarned { get; set; }

    [DataMember(Name = "damage_dealt", Order = 9)]
    public double DamageDealt { get; set; }

    [DataMember(Name = "damage_received", Order = 10)]
    public double DamageReceived { get; set; }

    [DataMember(Name = "health_restored", Order = 11)]
    public double HealthRestored { get; set; }

    [DataMember(Name = "contribution_id", Order = 12)]
    public string ContributionId { get; set; } = string.Empty;

    [DataMember(Name = "collection_id", Order = 13)]
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>The player's in-game name when the contribution was recorded, shown on the public leaderboards.</summary>
    [DataMember(Name = "name", Order = 14, EmitDefaultValue = false)]
    public string? Name { get; set; }

    /// <summary>Longest single connection finished in this contribution. Merged with Math.Max, not summed.</summary>
    [DataMember(Name = "longest_session_seconds", Order = 15, EmitDefaultValue = false)]
    public double LongestSessionSeconds { get; set; }

    /// <summary>The site accepts at most this many distinct action keys per contribution.</summary>
    public const int MaxActionKeys = 256;

    public bool HasChanges => PlaytimeSeconds > 0 || RoundsPlayed > 0 || Kills > 0 || Deaths > 0 || Escapes > 0 || Actions.Count > 0 || LongestSessionSeconds > 0;

    public PlayerStats Copy()
    {
        var copy = (PlayerStats)MemberwiseClone();
        copy.Actions = new Dictionary<string, long>(Actions);
        return copy;
    }

    public void Add(PlayerStats value)
    {
        PlaytimeSeconds += value.PlaytimeSeconds;
        RoundsPlayed += value.RoundsPlayed;
        Kills += value.Kills;
        Deaths += value.Deaths;
        Escapes += value.Escapes;
        XpEarned += value.XpEarned;
        DamageDealt += value.DamageDealt;
        DamageReceived += value.DamageReceived;
        HealthRestored += value.HealthRestored;
        LongestSessionSeconds = Math.Max(LongestSessionSeconds, value.LongestSessionSeconds);
        foreach (var action in value.Actions)
        {
            bool known = Actions.TryGetValue(action.Key, out long count);
            if (!known && Actions.Count >= MaxActionKeys) continue;
            Actions[action.Key] = count + action.Value;
        }
    }
}
