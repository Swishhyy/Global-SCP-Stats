using System;
using GlobalSCPStats.Core.Models;

namespace GlobalSCPStats.Core.Rewards;

// Server-owned reward evaluation using lifetime stats returned by the API.
public sealed class MilestoneRule
{
    public string RewardId { get; set; } = string.Empty;
    public string Metric { get; set; } = "xp";
    public double Threshold { get; set; }

    public bool IsEarned(PlayerStats lifetimeStats, int level)
    {
        if (string.IsNullOrWhiteSpace(RewardId) || double.IsNaN(Threshold) || double.IsInfinity(Threshold) || Threshold < 0)
            throw new ArgumentException("A reward ID and a finite, non-negative threshold are required.");
        double value;
        switch (Metric)
        {
            case "xp": value = lifetimeStats.XpEarned; break;
            case "level": value = level; break;
            case "kills": value = lifetimeStats.Kills; break;
            case "deaths": value = lifetimeStats.Deaths; break;
            case "escapes": value = lifetimeStats.Escapes; break;
            case "rounds_played": value = lifetimeStats.RoundsPlayed; break;
            case "playtime_seconds": value = lifetimeStats.PlaytimeSeconds; break;
            default:
                if (!Metric.StartsWith("action:", StringComparison.Ordinal)) throw new ArgumentException("Unknown reward metric.");
                lifetimeStats.Actions.TryGetValue(Metric.Substring(7), out long count);
                value = count;
                break;
        }
        return value >= Threshold;
    }
}
