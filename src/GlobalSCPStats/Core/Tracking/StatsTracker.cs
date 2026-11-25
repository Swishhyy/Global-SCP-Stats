using System;
using System.Collections.Generic;
using System.Linq;
using GlobalSCPStats.Core.Models;
using GlobalSCPStats.Core.Experience;

namespace GlobalSCPStats.Core.Tracking;

// The runtime serializes access to this tracker, including checkpoint creation.
public sealed class StatsTracker(ExperienceOptions? options = null)
{
    private readonly Dictionary<string, Session> sessions = [];
    private readonly Dictionary<string, PlayerStats> pending = [];
    private readonly HashSet<string> participants = [];
    private readonly HashSet<string> escaped = [];
    private bool roundActive;
    private readonly ExperienceAwards experience = new(options ?? new ExperienceOptions());

    public int OnlinePlayers => sessions.Count;

    public static bool IsSupportedUser(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        int separator = userId!.IndexOf('@');
        if (separator < 1 || separator > 20) return false;
        string provider = userId.Substring(separator);
        return (provider == "@steam" || provider == "@discord") &&
            userId.Take(separator).All(c => c >= '0' && c <= '9');
    }

    public void Join(string? userId, double now, string collectionId = "test")
    {
        if (!IsSupportedUser(userId) || sessions.ContainsKey(userId!)) return;
        sessions.Add(userId!, new Session(userId!, now, collectionId));
    }

    public void Leave(string? userId, double now)
    {
        if (userId == null || !sessions.TryGetValue(userId, out Session? session)) return;
        Advance(session, now);
        sessions.Remove(userId);
    }

    public void Forget(string userId)
    {
        sessions.Remove(userId);
        pending.Remove(userId);
        participants.Remove(userId);
        escaped.Remove(userId);
        experience.Forget(userId);
    }

    public void StartRound(IEnumerable<string> activePlayers, double now = 0)
    {
        if (roundActive) return;
        participants.Clear();
        escaped.Clear();
        roundActive = true;
        foreach (string userId in activePlayers) Participate(userId, now);
    }

    public void EndRound()
    {
        roundActive = false;
        participants.Clear();
        escaped.Clear();
    }

    public void Participate(string? userId, double now = 0)
    {
        if (!roundActive || userId == null || !sessions.ContainsKey(userId) || !participants.Add(userId)) return;
        Change(userId, new PlayerStats { RoundsPlayed = 1 });
        Action(userId, "round_played", now);
    }

    public void Death(string? victimId, string? attackerId, double now = 0)
    {
        if (!roundActive) return;
        Change(victimId, new PlayerStats { Deaths = 1 });
        Action(victimId, "death", now);
        if (attackerId != victimId)
        {
            Change(attackerId, new PlayerStats { Kills = 1 });
            Action(attackerId, "kill", now);
        }
    }

    public void Escape(string? userId, double now = 0)
    {
        if (roundActive && userId != null && sessions.ContainsKey(userId) && escaped.Add(userId))
        {
            Change(userId, new PlayerStats { Escapes = 1 });
            Action(userId, "escape", now);
        }
    }

    public void Action(string? userId, string action, double now)
    {
        if (!roundActive || userId == null || !sessions.ContainsKey(userId)) return;
        if (string.IsNullOrWhiteSpace(action) || action.Length > 96) throw new ArgumentException("Invalid action key.", nameof(action));
        Change(userId, new PlayerStats
        {
            Actions = new Dictionary<string, long> { [action] = 1 },
            XpEarned = experience.Award(userId, action, now),
        });
    }

    public void Damage(string? victimId, string? attackerId, double amount, double now)
    {
        if (!roundActive || double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0) return;
        Change(victimId, new PlayerStats { DamageReceived = amount });
        Action(victimId, "damage_received", now);
        if (victimId == attackerId) return;
        Change(attackerId, new PlayerStats { DamageDealt = amount });
        Action(attackerId, "damage_dealt", now);
    }

    public void Heal(string? userId, double amount, double now)
    {
        if (!roundActive || double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0) return;
        Change(userId, new PlayerStats { HealthRestored = amount });
        Action(userId, "healed", now);
    }

    public List<PlayerStats> Snapshot(double now)
    {
        experience.PruneIdle(now);
        foreach (Session session in sessions.Values) Advance(session, now);
        return [.. pending.Values.Where(p => p.HasChanges).Select(p => p.Copy())];
    }

    // Called only after the snapshot has been written to the durable outbox.
    public void CommitSnapshot() => pending.Clear();

    public PlayerStats? GetSession(string? userId, double now)
    {
        if (userId == null || !sessions.TryGetValue(userId, out Session? session)) return null;
        Advance(session, now);
        return session.Totals.Copy();
    }

    private void Advance(Session session, double now)
    {
        double elapsed = Math.Max(0, now - session.LastUpdated);
        session.LastUpdated = Math.Max(session.LastUpdated, now);
        if (elapsed > 0) Change(session.UserId, new PlayerStats { PlaytimeSeconds = elapsed });
    }

    private void Change(string? userId, PlayerStats change)
    {
        if (userId == null || !sessions.TryGetValue(userId, out Session? session)) return;
        if (!pending.TryGetValue(userId, out PlayerStats? value))
            pending.Add(userId, value = new PlayerStats
            {
                UserId = userId, CollectionId = session.CollectionId, ContributionId = Guid.NewGuid().ToString("D"),
            });
        value.Add(change);
        session.Totals.Add(change);
    }

    private sealed class Session
    {
        public Session(string userId, double now, string collectionId)
        {
            UserId = userId;
            LastUpdated = now;
            CollectionId = collectionId;
            Totals = new PlayerStats { UserId = userId };
        }

        public string UserId { get; }
        public string CollectionId { get; }
        public double LastUpdated { get; set; }
        public PlayerStats Totals { get; }
    }
}
