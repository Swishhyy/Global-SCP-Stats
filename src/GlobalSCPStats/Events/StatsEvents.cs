using System.Linq;
using System.Collections.Generic;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using GlobalSCPStats.Core;
using GlobalSCPStats.Core.Tracking;
using MEC;
using PlayerEvents = Exiled.Events.Handlers.Player;
using ServerEvents = Exiled.Events.Handlers.Server;

namespace GlobalSCPStats.Events;

public sealed class StatsEvents
{
    private readonly StatsRuntime runtime;
    private CoroutineHandle privacyLoop;
    public StatsEvents(StatsRuntime runtime) => this.runtime = runtime;

    public static bool IsEligible(Player? player) => player != null && player.IsVerified && !player.IsNPC && !player.IsHost && StatsTracker.IsSupportedUser(player.UserId);

    public void Register()
    {
        PlayerEvents.Verified += OnVerified;
        PlayerEvents.Left += OnLeft;
        PlayerEvents.Spawned += OnSpawned;
        PlayerEvents.Died += OnDied;
        PlayerEvents.Escaped += OnEscaped;
        ServerEvents.RoundStarted += OnRoundStarted;
        ServerEvents.RoundEnded += OnRoundEnded;
        ServerEvents.RestartingRound += OnRestartingRound;
        ServerEvents.WaitingForPlayers += OnRestartingRound;
        privacyLoop = Timing.RunCoroutine(WatchPrivacy());
    }

    public void Unregister()
    {
        PlayerEvents.Verified -= OnVerified;
        PlayerEvents.Left -= OnLeft;
        PlayerEvents.Spawned -= OnSpawned;
        PlayerEvents.Died -= OnDied;
        PlayerEvents.Escaped -= OnEscaped;
        ServerEvents.RoundStarted -= OnRoundStarted;
        ServerEvents.RoundEnded -= OnRoundEnded;
        ServerEvents.RestartingRound -= OnRestartingRound;
        ServerEvents.WaitingForPlayers -= OnRestartingRound;
        Timing.KillCoroutines(privacyLoop);
    }

    private void OnVerified(VerifiedEventArgs ev)
    {
        if (IsEligible(ev.Player)) TrackPlayer(ev.Player);
    }

    public void TrackPlayer(Player player)
    {
        SynchronizePrivacy(runtime, player);
        if (Plugin.Instance?.Config.Privacy.ShowJoinReminder == true && !runtime.CollectionAllowed(player.UserId))
            player.SendConsoleMessage(player.DoNotTrack
                ? "Global SCP Stats respects your in-game Do Not Track setting. No gameplay stats or XP will be collected for you."
                : "Global SCP Stats collection is off for you. Use .statsprivacy info to view the notice and local controls.", "yellow");
    }

    public static void SynchronizePrivacy(StatsRuntime runtime, Player? player)
    {
        if (!IsEligible(player)) return;
        bool doNotTrack = player!.DoNotTrack;
        runtime.ObserveDoNotTrack(player.UserId, doNotTrack);
        if (!doNotTrack) runtime.Join(player.UserId);
    }

    private IEnumerator<float> WatchPrivacy()
    {
        while (true)
        {
            foreach (Player player in Player.List.ToList())
            {
                if (!IsEligible(player)) continue;
                SynchronizePrivacy(runtime, player);
                if (player.IsAlive) runtime.Participate(player.UserId);
            }
            yield return Timing.WaitForSeconds(1);
        }
    }

    private void OnLeft(LeftEventArgs ev) => runtime.Leave(ev.Player.UserId);

    private void OnSpawned(SpawnedEventArgs ev)
    {
        if (!IsEligible(ev.Player) || !ev.Player.IsAlive) return;
        SynchronizePrivacy(runtime, ev.Player);
        runtime.Participate(ev.Player.UserId);
    }

    private void OnDied(DiedEventArgs ev)
    {
        if (!IsEligible(ev.Player)) return;
        SynchronizePrivacy(runtime, ev.Player);
        SynchronizePrivacy(runtime, ev.Attacker);
        runtime.Death(ev.Player.UserId, IsEligible(ev.Attacker) ? ev.Attacker.UserId : null);
    }

    private void OnEscaped(EscapedEventArgs ev)
    {
        if (!IsEligible(ev.Player)) return;
        SynchronizePrivacy(runtime, ev.Player);
        runtime.Escape(ev.Player.UserId);
    }

    private void OnRoundStarted()
    {
        foreach (Player player in Player.List.Where(IsEligible)) SynchronizePrivacy(runtime, player);
        runtime.StartRound(Player.List.Where(p => IsEligible(p) && p.IsAlive).Select(p => p.UserId));
    }

    private void OnRoundEnded(RoundEndedEventArgs ev) => runtime.EndRound();
    private void OnRestartingRound() => runtime.EndRound();
}
