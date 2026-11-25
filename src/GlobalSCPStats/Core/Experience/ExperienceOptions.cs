using System;
using System.Collections.Generic;

namespace GlobalSCPStats.Core.Experience;

public sealed class ExperienceOptions
{
    public bool Enabled { get; set; } = true;
    public int DefaultReward { get; set; } = 1;
    public double DefaultCooldownSeconds { get; set; } = 2;
    public int MaxXpPerMinute { get; set; } = 1000;

    public Dictionary<string, int> Rewards { get; set; } = new Dictionary<string, int>
    {
        ["kill"] = 50, ["death"] = 1, ["escape"] = 100, ["round_played"] = 20,
        ["damage_dealt"] = 2, ["damage_received"] = 1, ["healed"] = 5,
        ["scp049.recall_finishing"] = 50, ["scp079.level_gaining"] = 25,
    };

    public Dictionary<string, double> Cooldowns { get; set; } = new Dictionary<string, double>
    {
        ["kill"] = 0, ["death"] = 0, ["escape"] = 0, ["round_played"] = 0,
        ["player.door_interacting"] = 10, ["player.item_added"] = 10,
        ["player.shot"] = 5, ["player.jumping"] = 10,
    };

    public void Validate()
    {
        if (DefaultReward < 0 || DefaultReward > 100000 || MaxXpPerMinute < 1 || MaxXpPerMinute > 1000000 ||
            double.IsNaN(DefaultCooldownSeconds) || double.IsInfinity(DefaultCooldownSeconds) || DefaultCooldownSeconds < 0)
            throw new ArgumentException("Invalid default XP reward, cooldown, or per-minute cap.");
        foreach (var reward in Rewards)
            if (reward.Value < 0 || reward.Value > 100000) throw new ArgumentException("XP rewards must be between 0 and 100000.");
        foreach (var cooldown in Cooldowns)
            if (double.IsNaN(cooldown.Value) || double.IsInfinity(cooldown.Value) || cooldown.Value < 0)
                throw new ArgumentException("XP cooldowns must be finite, non-negative seconds.");
    }
}
