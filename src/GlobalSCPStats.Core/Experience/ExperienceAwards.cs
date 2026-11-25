using System;
using System.Collections.Generic;
using System.Linq;

namespace GlobalSCPStats.Core.Experience;

public sealed class ExperienceAwards
{
    private readonly ExperienceOptions options;
    private readonly Dictionary<string, Budget> budgets = new Dictionary<string, Budget>();

    public ExperienceAwards(ExperienceOptions options)
    {
        options.Validate();
        this.options = options;
    }

    public long Award(string userId, string action, double now)
    {
        if (!options.Enabled) return 0;
        int reward = options.Rewards.TryGetValue(action, out int configured) ? configured : options.DefaultReward;
        if (reward == 0) return 0;
        if (!budgets.TryGetValue(userId, out Budget? budget)) budgets.Add(userId, budget = new Budget());
        while (budget.Awards.Count > 0 && now - budget.Awards.Peek().Time >= 60)
            budget.Total -= budget.Awards.Dequeue().Amount;
        double cooldown = options.Cooldowns.TryGetValue(action, out double seconds) ? seconds : options.DefaultCooldownSeconds;
        if (budget.LastAward.TryGetValue(action, out double last) && now - last < cooldown) return 0;
        if (budget.Total + reward > options.MaxXpPerMinute) return 0;

        budget.LastAward[action] = now;
        budget.Awards.Enqueue(new Awarded(now, reward));
        budget.Total += reward;
        budget.LastAwarded = now;
        return reward;
    }

    public void PruneIdle(double now)
    {
        double retention = Math.Max(60, Math.Max(options.DefaultCooldownSeconds,
            options.Cooldowns.Count == 0 ? 0 : options.Cooldowns.Values.Max()));
        foreach (string id in budgets.Where(pair => now - pair.Value.LastAwarded >= retention).Select(pair => pair.Key).ToArray())
            budgets.Remove(id);
    }

    public void Forget(string userId) => budgets.Remove(userId);

    private sealed class Budget
    {
        public Dictionary<string, double> LastAward { get; } = new Dictionary<string, double>();
        public Queue<Awarded> Awards { get; } = new Queue<Awarded>();
        public int Total { get; set; }
        public double LastAwarded { get; set; }
    }

    private sealed class Awarded
    {
        public Awarded(double time, int amount) { Time = time; Amount = amount; }
        public double Time { get; }
        public int Amount { get; }
    }
}
