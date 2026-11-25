using System;
using System.Linq;
using System.Collections.Generic;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Roles;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using GlobalSCPStats.Core;
using GlobalSCPStats.Core.Tracking;
using MEC;
using PlayerEvents = Exiled.Events.Handlers.Player;
using ServerEvents = Exiled.Events.Handlers.Server;

namespace GlobalSCPStats.Events;

public sealed class StatsEvents(StatsRuntime runtime)
{
    private readonly StatsRuntime runtime = runtime;
    private readonly Dictionary<string, PlayerSample> samples = [];
    private CoroutineHandle privacyLoop;

    // Anything faster than this in one second is a teleport (elevator, pocket dimension, SCP-106), not movement.
    private const float MaxMetersPerSecond = 20f;
    private const double MaxSampleGapSeconds = 5;
    private const int PlayersPerFrame = 8;

    public static bool IsEligible(Player? player) => player != null && player.IsVerified && !player.IsNPC && !player.IsHost && StatsTracker.IsSupportedUser(player.UserId);

    public void Register()
    {
        PlayerEvents.Verified += OnVerified;
        PlayerEvents.Left += OnLeft;
        PlayerEvents.Spawned += OnSpawned;
        PlayerEvents.Died += OnDied;
        PlayerEvents.Escaped += OnEscaped;
        PlayerEvents.UsedItem += OnUsedItem;
        PlayerEvents.ItemAdded += OnItemAdded;
        PlayerEvents.ThrownProjectile += OnThrownProjectile;
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
        PlayerEvents.UsedItem -= OnUsedItem;
        PlayerEvents.ItemAdded -= OnItemAdded;
        PlayerEvents.ThrownProjectile -= OnThrownProjectile;
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
        if (!doNotTrack) runtime.Join(player.UserId, player.Nickname);
    }

    /// <summary>
    /// Runs once a second, spread over several frames: each frame handles at most <see cref="PlayersPerFrame"/>
    /// players and hands their samples to the runtime in one call, so a full server never does all of its
    /// stats work in a single tick. Nothing is sent from here; stats upload in batches from the background worker.
    /// </summary>
    private IEnumerator<float> WatchPrivacy()
    {
        var pending = new List<StatsRuntime.Measurement>();
        while (true)
        {
            List<Player> players = Player.List.ToList();
            for (int i = 0; i < players.Count; i++)
            {
                try
                {
                    Player player = players[i];
                    if (IsEligible(player))
                    {
                        SynchronizePrivacy(runtime, player);
                        if (player.IsAlive) runtime.Participate(player.UserId);
                        Sample(player, pending);
                    }
                }
                catch (Exception ex)
                {
                    // One bad player must not stop the loop for everyone else.
                    Log.Debug("Global SCP Stats skipped a player sample: " + ex.Message);
                }
                if ((i + 1) % PlayersPerFrame == 0)
                {
                    runtime.MeasureAll(pending);
                    pending.Clear();
                    yield return Timing.WaitForOneFrame;
                }
            }
            runtime.MeasureAll(pending);
            pending.Clear();
            yield return Timing.WaitForSeconds(1);
        }
    }

    /// <summary>
    /// Time spent as each role and in each zone, and distance moved split by sprinting, walking and crouching
    /// (and by SCP, for the SCP pages). Uses the real time since this player's last sample, since samples are
    /// spread across frames.
    /// </summary>
    private void Sample(Player player, List<StatsRuntime.Measurement> pending)
    {
        double now = Timing.LocalTime;
        if (!player.IsAlive || !Round.IsStarted || Round.IsEnded)
        {
            samples.Remove(player.UserId);
            return;
        }
        Vector3 position = player.Position;
        if (!samples.TryGetValue(player.UserId, out PlayerSample? last))
        {
            samples[player.UserId] = new PlayerSample(position, now);
            return;
        }
        double seconds = Math.Min(now - last.Time, MaxSampleGapSeconds);
        float meters = Vector3.Distance(position, last.Position);
        samples[player.UserId] = new PlayerSample(position, now);
        if (seconds <= 0) return;

        string id = player.UserId;
        string role = StatsTracker.KeyPart(player.Role.Type);
        pending.Add(new(id, "time.as." + role, seconds));
        if (player.Zone != ZoneType.Unspecified) pending.Add(new(id, "time.in." + StatsTracker.KeyPart(player.Zone), seconds));

        if (meters > 0.05f && meters <= MaxMetersPerSecond * seconds)
        {
            string state = player.Role is FpcRole fpc ? fpc.MoveState switch
            {
                PlayerMovementState.Sprinting => "sprinted",
                PlayerMovementState.Crouching or PlayerMovementState.Sneaking => "crouched",
                _ => "walked",
            } : "walked";
            pending.Add(new(id, "move." + state + "_m", meters));
            if (player.IsScp) pending.Add(new(id, "move.as." + role + "_m", meters));
        }
    }

    private sealed record PlayerSample(Vector3 Position, double Time);

    private void OnUsedItem(UsedItemEventArgs ev)
    {
        if (IsEligible(ev.Player) && ev.Item != null) runtime.Count(ev.Player.UserId, "item.used." + StatsTracker.KeyPart(ev.Item.Type));
    }

    private void OnItemAdded(ItemAddedEventArgs ev)
    {
        if (IsEligible(ev.Player) && ev.Item != null) runtime.Count(ev.Player.UserId, "item.received." + StatsTracker.KeyPart(ev.Item.Type));
    }

    private void OnThrownProjectile(ThrownProjectileEventArgs ev)
    {
        if (IsEligible(ev.Player) && ev.Item != null) runtime.Count(ev.Player.UserId, "item.thrown." + StatsTracker.KeyPart(ev.Item.Type));
    }

    private void OnLeft(LeftEventArgs ev)
    {
        samples.Remove(ev.Player.UserId);
        runtime.Leave(ev.Player.UserId);
    }

    private void OnSpawned(SpawnedEventArgs ev)
    {
        if (!IsEligible(ev.Player) || !ev.Player.IsAlive) return;
        SynchronizePrivacy(runtime, ev.Player);
        runtime.Participate(ev.Player.UserId);
        runtime.Count(ev.Player.UserId, "role.spawned." + StatsTracker.KeyPart(ev.Player.Role.Type));
    }

    private void OnDied(DiedEventArgs ev)
    {
        if (!IsEligible(ev.Player)) return;
        SynchronizePrivacy(runtime, ev.Player);
        SynchronizePrivacy(runtime, ev.Attacker);
        string? attacker = IsEligible(ev.Attacker) ? ev.Attacker.UserId : null;
        runtime.Death(ev.Player.UserId, attacker);
        // Stat-only counters (no XP): who you were and what got you, and the killer's role and weapon.
        string cause = StatsTracker.KeyPart(ev.DamageHandler.Type);
        runtime.Count(ev.Player.UserId, "death.as." + StatsTracker.KeyPart(ev.TargetOldRole));
        runtime.Count(ev.Player.UserId, "death.by." + cause);
        if (attacker != null && attacker != ev.Player.UserId)
        {
            runtime.Count(attacker, "kill.as." + StatsTracker.KeyPart(ev.Attacker.Role.Type));
            runtime.Count(attacker, "kill.with." + cause);
            runtime.Count(attacker, "kill.target." + StatsTracker.KeyPart(ev.TargetOldRole));
        }
    }

    private void OnEscaped(EscapedEventArgs ev)
    {
        if (!IsEligible(ev.Player)) return;
        SynchronizePrivacy(runtime, ev.Player);
        runtime.Escape(ev.Player.UserId);
        runtime.Count(ev.Player.UserId, "escape.as." + StatsTracker.KeyPart(ev.EscapeScenario));
    }

    private void OnRoundStarted()
    {
        foreach (Player player in Player.List.Where(IsEligible)) SynchronizePrivacy(runtime, player);
        runtime.StartRound(Player.List.Where(p => IsEligible(p) && p.IsAlive).Select(p => p.UserId));
    }

    private void OnRoundEnded(RoundEndedEventArgs ev)
    {
        // Count round results before the round closes; stats only count while a round is active.
        foreach (Player player in Player.List.Where(p => IsEligible(p) && p.IsAlive))
        {
            runtime.Count(player.UserId, "round.survived");
            if (Won(player.Role.Side, ev.LeadingTeam)) runtime.Count(player.UserId, "round.won");
        }
        runtime.EndRound();
    }

    private static bool Won(Side side, LeadingTeam team) => (side, team) switch
    {
        (Side.Mtf, LeadingTeam.FacilityForces) => true,
        (Side.ChaosInsurgency, LeadingTeam.ChaosInsurgency) => true,
        (Side.Scp, LeadingTeam.Anomalies) => true,
        _ => false,
    };
    private void OnRestartingRound() => runtime.EndRound();
}
