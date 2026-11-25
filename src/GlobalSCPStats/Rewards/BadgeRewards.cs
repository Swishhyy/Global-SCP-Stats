using System.Collections.Generic;
using System.Linq;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using GlobalSCPStats.Core;
using MEC;

namespace GlobalSCPStats.Rewards;

public sealed class BadgeRewards
{
    private readonly StatsRuntime runtime;
    private readonly List<RewardBadge> definitions;
    private readonly bool preserveExisting;
    private readonly Dictionary<string, OwnedBadge> owned = new Dictionary<string, OwnedBadge>();
    private CoroutineHandle loop;

    public BadgeRewards(StatsRuntime runtime, List<RewardBadge> definitions, bool preserveExisting)
    {
        this.runtime = runtime;
        this.definitions = definitions;
        this.preserveExisting = preserveExisting;
    }

    public void Start()
    {
        Exiled.Events.Handlers.Player.Left += OnLeft;
        loop = Timing.RunCoroutine(Refresh());
    }

    public void Stop()
    {
        Timing.KillCoroutines(loop);
        Exiled.Events.Handlers.Player.Left -= OnLeft;
        foreach (Player player in Player.List.ToList()) Restore(player);
        owned.Clear();
    }

    private IEnumerator<float> Refresh()
    {
        while (true)
        {
            foreach (Player player in Player.List.ToList())
            {
                if (!Events.StatsEvents.IsEligible(player)) continue;
                Events.StatsEvents.SynchronizePrivacy(runtime, player);
                var profile = runtime.GetProgress(player.UserId);
                RewardBadge? reward = profile?.ServerStats == null ? null : definitions
                    .Where(d => d.IsEarned(profile.ServerStats!))
                    .OrderByDescending(d => d.Priority).ThenBy(d => d.RewardId, System.StringComparer.Ordinal).FirstOrDefault();
                if (reward == null) { Restore(player); continue; }

                owned.TryGetValue(player.UserId, out OwnedBadge? previous);
                if (previous != null && (player.RankName != previous.AppliedName || player.RankColor != previous.AppliedColor))
                {
                    // Another plugin or an admin changed the badge. Leave their update intact.
                    owned.Remove(player.UserId);
                    continue;
                }
                if (previous == null && preserveExisting && !string.IsNullOrWhiteSpace(player.RankName)) continue;
                if (previous == null)
                {
                    previous = new OwnedBadge(player.RankName, player.RankColor);
                    owned.Add(player.UserId, previous);
                }
                player.RankName = reward.Name;
                player.RankColor = reward.Color;
                previous.AppliedName = reward.Name;
                previous.AppliedColor = reward.Color;
            }
            yield return Timing.WaitForSeconds(2);
        }
    }

    private void Restore(Player player)
    {
        if (!owned.TryGetValue(player.UserId, out OwnedBadge? badge)) return;
        if (player.RankName == badge.AppliedName && player.RankColor == badge.AppliedColor)
        {
            player.RankName = badge.OriginalName;
            player.RankColor = badge.OriginalColor;
        }
        owned.Remove(player.UserId);
    }

    private void OnLeft(LeftEventArgs ev) => owned.Remove(ev.Player.UserId);

    private sealed class OwnedBadge
    {
        public OwnedBadge(string name, string color) { OriginalName = name; OriginalColor = color; }
        public string OriginalName { get; }
        public string OriginalColor { get; }
        public string AppliedName { get; set; } = string.Empty;
        public string AppliedColor { get; set; } = string.Empty;
    }
}
