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

    public void Join(string? userId, double now, string collectionId = "test", string? name = null)
    {
        if (!IsSupportedUser(userId)) return;
        if (sessions.TryGetValue(userId!, out Session? existing))
        {
            existing.Name = CleanName(name) ?? existing.Name;
            return;
        }
        sessions.Add(userId!, new Session(userId!, now, collectionId) { Name = CleanName(name) });
        Count(userId, "session.started", requireRound: false);
    }

    /// <summary>Trims a nickname to what the site accepts: up to 32 characters with no control characters.</summary>
    public static string? CleanName(string? name)
    {
        if (name == null) return null;
        string clean = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > 32) clean = clean.Substring(0, char.IsHighSurrogate(clean[31]) ? 31 : 32).TrimEnd();
        return clean.Length == 0 ? null : clean;
    }

    public void Leave(string? userId, double now)
    {
        if (userId == null || !sessions.TryGetValue(userId, out Session? session)) return;
        Advance(session, now);
        Change(userId, new PlayerStats { LongestSessionSeconds = Math.Max(0, now - session.Started) });
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

    /// <summary>
    /// Adds one to a stat-only counter such as "kill.with.crossvec". Unlike <see cref="Action"/>, it never awards XP,
    /// so new stats don't change the XP economy.
    /// </summary>
    public void Count(string? userId, string key, bool requireRound = true)
    {
        if ((requireRound && !roundActive) || userId == null || !sessions.ContainsKey(userId)) return;
        if (!ValidKey(key)) throw new ArgumentException("Invalid stat key.", nameof(key));
        Change(userId, new PlayerStats { Actions = new Dictionary<string, long> { [key] = 1 } });
    }

    /// <summary>
    /// Adds a measured amount (meters moved, seconds as a role) to a stat-only counter. Fractions carry over
    /// between calls so slow, steady movement still adds up to whole meters.
    /// </summary>
    public void Measure(string? userId, string key, double amount)
    {
        if (!roundActive || userId == null || amount <= 0 || !sessions.TryGetValue(userId, out Session? session)) return;
        if (!ValidKey(key)) throw new ArgumentException("Invalid stat key.", nameof(key));
        session.Fractions.TryGetValue(key, out double carried);
        double total = carried + amount;
        long whole = (long)Math.Floor(total);
        session.Fractions[key] = total - whole;
        if (whole > 0) Change(userId, new PlayerStats { Actions = new Dictionary<string, long> { [key] = whole } });
    }

    /// <summary>Turns an enum name such as "Scp173" or "E11Sr" into a stat key part such as "scp173" or "e11sr".</summary>
    public static string KeyPart(object? value)
    {
        string text = new string((value?.ToString() ?? "unknown").ToLowerInvariant().Where(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_').ToArray());
        if (text.Length == 0) return "unknown";
        return text.Length > 40 ? text.Substring(0, 40) : text;
    }

    private static bool ValidKey(string key) =>
        key.Length is > 0 and <= 96 && key[0] >= 'a' && key[0] <= 'z' &&
        key.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.');

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
        value.Name = session.Name;
        value.Add(change);
        session.Totals.Add(change);
    }

    private sealed class Session
    {
        public Session(string userId, double now, string collectionId)
        {
            UserId = userId;
            LastUpdated = now;
            Started = now;
            CollectionId = collectionId;
            Totals = new PlayerStats { UserId = userId };
        }

        public string UserId { get; }
        public string CollectionId { get; }
        public double Started { get; }
        public string? Name { get; set; }
        public double LastUpdated { get; set; }
        public PlayerStats Totals { get; }
        public Dictionary<string, double> Fractions { get; } = [];
    }
}
