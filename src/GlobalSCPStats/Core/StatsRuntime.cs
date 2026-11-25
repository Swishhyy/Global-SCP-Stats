using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GlobalSCPStats.Core.Models;
using GlobalSCPStats.Core.Storage;
using GlobalSCPStats.Core.Tracking;
using GlobalSCPStats.Core.Transport;
using GlobalSCPStats.Core.Experience;
using GlobalSCPStats.Core.Rewards;
using GlobalSCPStats.Core.Privacy;

namespace GlobalSCPStats.Core;

public sealed partial class StatsRuntime : IDisposable
{
    private readonly object gate = new();
    private readonly StatsTracker tracker;
    private readonly DurableOutbox outbox;
    private readonly string serverId;
    private readonly double checkpointSeconds;
    private readonly StatsApiClient? api;
    private readonly Action<string> log;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private Task? worker;
    private volatile bool uploadsBlocked;
    private bool disposed;
    private readonly HashSet<string> online = [];
    private readonly Queue<string> profileRequests = new();
    private readonly HashSet<string> requestedProfiles = [];
    private readonly Dictionary<string, CachedProfile> profiles = [];

    public StatsRuntime(string serverId, DurableOutbox outbox, PrivacyPreferences privacy, TimeSpan checkpointInterval, StatsApiClient? api, Action<string> log, ExperienceOptions? experience = null)
    {
        if (string.IsNullOrWhiteSpace(serverId)) throw new ArgumentException("A server ID is required.", nameof(serverId));
        if (checkpointInterval < TimeSpan.FromSeconds(1)) throw new ArgumentOutOfRangeException(nameof(checkpointInterval));
        this.serverId = serverId;
        this.outbox = outbox;
        this.privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
        checkpointSeconds = checkpointInterval.TotalSeconds;
        this.api = api;
        this.log = log;
        tracker = new StatsTracker(experience);
    }

    public int QueuedBatches => outbox.Count;
    public string UploadStatus => api == null ? "not configured" : privacyBlocked || PrivacyCleanupPending ? "paused for privacy cleanup" : uploadsBlocked ? "paused; reload after correcting the API/configuration" : "enabled";
    public int OnlinePlayers { get { lock (gate) return tracker.OnlinePlayers; } }

    public void Start()
    {
        if (worker != null) throw new InvalidOperationException("The runtime has already started.");
        worker = Task.Run(RunAsync);
    }

    public void Join(string? id)
    {
        lock (gate)
        {
            string? collectionId = privacy.GetCollectionId(id);
            if (collectionId == null) return;
            tracker.Join(id, clock.Elapsed.TotalSeconds, collectionId);
            if (StatsTracker.IsSupportedUser(id) && online.Add(id!)) RequestProfile(id!);
        }
    }

    public void Leave(string? id)
    {
        lock (gate)
        {
            tracker.Leave(id, clock.Elapsed.TotalSeconds);
            if (id == null) return;
            RemoveConnected(id);
        }
    }
    public void StartRound(IEnumerable<string> activePlayers) { lock (gate) tracker.StartRound(activePlayers, clock.Elapsed.TotalSeconds); }
    public void EndRound() { lock (gate) tracker.EndRound(); }
    public void Participate(string? id) { lock (gate) tracker.Participate(id, clock.Elapsed.TotalSeconds); }
    public void Death(string? victim, string? attacker) { lock (gate) tracker.Death(victim, attacker, clock.Elapsed.TotalSeconds); }
    public void Escape(string? id) { lock (gate) tracker.Escape(id, clock.Elapsed.TotalSeconds); }
    public void Action(string? id, string action) { lock (gate) tracker.Action(id, action, clock.Elapsed.TotalSeconds); }
    public void Damage(string? victim, string? attacker, double amount) { lock (gate) tracker.Damage(victim, attacker, amount, clock.Elapsed.TotalSeconds); }
    public void Heal(string? id, double amount) { lock (gate) tracker.Heal(id, amount, clock.Elapsed.TotalSeconds); }
    public PlayerStats? GetSession(string? id) { lock (gate) return tracker.GetSession(id, clock.Elapsed.TotalSeconds); }

    public ProgressProfile? GetProgress(string? id)
    {
        lock (gate)
        {
            if (id == null || !profiles.TryGetValue(id, out CachedProfile? cached)) return null;
            if (clock.Elapsed.TotalSeconds - cached.Updated < 120) return cached.Profile.Copy();
            profiles.Remove(id);
            return null;
        }
    }

    private void RequestProfile(string id)
    {
        if (api?.ProfilesEnabled == true && requestedProfiles.Add(id)) profileRequests.Enqueue(id);
    }

    public bool Checkpoint()
    {
        lock (gate)
        {
            int expired = outbox.PruneExpired(DateTime.UtcNow);
            if (expired > 0) log($"Removed {expired} expired outbox batches under the configured local retention policy.");
            var players = tracker.Snapshot(clock.Elapsed.TotalSeconds);
            if (players.Count == 0) return true;
            if (!outbox.TryEnqueue(new StatsBatch { ServerId = serverId, Players = players })) return false;
            tracker.CommitSnapshot();
            return true;
        }
    }

    private async Task RunAsync()
    {
        // Apply retention before any old queued batch can be uploaded after a restart.
        double nextCheckpoint = 0;
        double nextUpload = 0;
        int failures = 0;
        double nextProfileRefresh = 60;
        CancellationToken cancellationToken = lifetime.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                double now = clock.Elapsed.TotalSeconds;
                bool privacyReady = SynchronizePrivacy(cancellationToken);
                if (now >= nextProfileRefresh)
                {
                    lock (gate) foreach (string id in online) RequestProfile(id);
                    nextProfileRefresh = now + 60;
                }
                if (now >= nextCheckpoint)
                {
                    try
                    {
                        if (!Checkpoint()) log("The outbox is full. New stats remain in memory; restore API connectivity before stopping the server.");
                    }
                    catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
                    {
                        log("Could not checkpoint stats to disk. New stats remain in memory. Check available space and permissions.");
                    }
                    nextCheckpoint = clock.Elapsed.TotalSeconds + checkpointSeconds;
                }

                if (privacyReady && !privacyBlocked && !PrivacyCleanupPending && api != null && !uploadsBlocked && now >= nextUpload)
                {
                    try
                    {
                        // Bound each pass so checkpointing can run while draining a backlog.
                        for (int sent = 0; sent < 1; sent++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            QueuedBatch? batch = outbox.Peek();
                            if (batch == null) break;
                            if (!BatchAllowed(batch.Batch)) break;
                            UploadResult result = await api.SendAsync(batch, cancellationToken).ConfigureAwait(false);
                            if (result.Outcome == UploadOutcome.Accepted)
                            {
                                outbox.Acknowledge(batch);
                                lock (gate)
                                    foreach (var player in batch.Batch.Players)
                                        if (online.Contains(player.UserId)) RequestProfile(player.UserId);
                                failures = 0;
                                continue;
                            }

                            log(result.Message);
                            uploadsBlocked = result.Outcome == UploadOutcome.Blocked;
                            failures = Math.Min(failures + 1, 7);
                            double backoff = Math.Min(300, Math.Pow(2, failures));
                            double requested = result.RetryAfter?.TotalSeconds ?? 0;
                            nextUpload = clock.Elapsed.TotalSeconds + Math.Max(backoff, Math.Min(86400, requested));
                            break;
                        }
                    }
                    catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException ||
                        error is System.Security.Cryptography.CryptographicException || error is System.Runtime.Serialization.SerializationException)
                    {
                        uploadsBlocked = true;
                        log("Could not read or acknowledge the encrypted outbox. Uploads paused; files preserved. Check disk access and restore the original storage key if needed.");
                    }
                }

                if (privacyReady && !privacyBlocked && !PrivacyCleanupPending && api?.ProfilesEnabled == true && !uploadsBlocked)
                {
                    string? id = null;
                    lock (gate)
                    {
                        if (profileRequests.Count > 0)
                        {
                            string candidate = profileRequests.Dequeue();
                            requestedProfiles.Remove(candidate);
                            if (online.Contains(candidate)) id = candidate;
                        }
                    }
                    if (id != null)
                    {
                        try
                        {
                            ProgressProfile? profile = await api.FetchProfileAsync(id, cancellationToken).ConfigureAwait(false);
                            lock (gate)
                            {
                                if (profile != null && online.Contains(id) &&
                                    (!profiles.TryGetValue(id, out CachedProfile? previous) || profile.Revision >= previous.Profile.Revision))
                                    profiles[id] = new CachedProfile(profile, clock.Elapsed.TotalSeconds);
                            }
                        }
                        catch (Exception error) when (error is System.Runtime.Serialization.SerializationException || error is System.IO.IOException)
                        {
                            log("Invalid or unreadable progress profile ignored; no new rewards applied.");
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        try { worker?.GetAwaiter().GetResult(); }
        finally
        {
            try
            {
                SynchronizePrivacy(CancellationToken.None, true);
                if (!Checkpoint()) log("The outbox is full. Stats since the last successful checkpoint could not be saved during shutdown.");
            }
            finally
            {
                api?.Dispose();
                lifetime.Dispose();
            }
        }
    }

    private sealed class CachedProfile(ProgressProfile profile, double updated)
    {
        public ProgressProfile Profile { get; } = profile; public double Updated { get; } = updated;
    }
}
