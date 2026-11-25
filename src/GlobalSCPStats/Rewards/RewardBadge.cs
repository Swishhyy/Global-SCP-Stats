using System;
using GlobalSCPStats.Core.Rewards;
using GlobalSCPStats.Core.Models;
using System.Linq;

namespace GlobalSCPStats.Rewards;

public sealed class RewardBadge
{
    public string RewardId { get; set; } = "level_10";
    public string Metric { get; set; } = "level";
    public double Threshold { get; set; } = 10;
    public bool IsEarned(PlayerStats stats) => new MilestoneRule { RewardId = RewardId, Metric = Metric, Threshold = Threshold }.IsEarned(stats, (int)Math.Floor(Math.Sqrt(stats.XpEarned / 250d)) + 1);
    public string Name { get; set; } = "Level 10";
    public string Color { get; set; } = "cyan";
    public int Priority { get; set; } = 10;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RewardId) || RewardId.Length > 96 ||
            string.IsNullOrWhiteSpace(Name) || Name.Length > 32 || Name.Any(c => char.IsControl(c) || c == '<' || c == '>'))
            throw new ArgumentException("Invalid reward badge ID or display name.");
        IsEarned(new PlayerStats());
        string[] allowed = { "pink", "red", "brown", "silver", "light_green", "crimson", "cyan", "aqua", "deep_pink", "tomato", "yellow", "magenta", "blue_green", "orange", "lime", "green", "emerald", "carmine", "nickel", "mint", "army_green", "pumpkin", "white" };
        if (!allowed.Contains(Color)) throw new ArgumentException("Unsupported reward badge color.");
    }
}
