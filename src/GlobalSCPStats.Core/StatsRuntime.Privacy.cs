using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using GlobalSCPStats.Core.Models;
using GlobalSCPStats.Core.Privacy;

namespace GlobalSCPStats.Core;

public sealed partial class StatsRuntime
{
    private readonly PrivacyPreferences privacy;
    private long sanitizedPrivacyRevision = -1;
    private volatile bool privacyBlocked;
    private double nextPrivacyAttempt;
    private double nextDntWarning;

    public bool PrivacyCleanupPending => privacy.HasUnsavedChanges || Interlocked.Read(ref sanitizedPrivacyRevision) != privacy.Revision;
    public string PrivacyStatus => privacyBlocked ? "blocked; check storage and the server log" : PrivacyCleanupPending ? "cleanup pending" : "up to date";
    public bool CollectionAllowed(string? id) => privacy.IsAllowed(id);

    public void ObserveDoNotTrack(string id, bool enabled)
    {
        lock (gate)
        {
            if (enabled)
            {
                tracker.Forget(id);
                RemoveConnected(id);
            }
            try
            {
                if (privacy.ObserveDoNotTrack(id, enabled) && !enabled)
                {
                    tracker.Forget(id);
                    RemoveConnected(id);
                }
            }
            catch (Exception error) when (IsStorageFailure(error))
            {
                if (privacy.HasUnsavedChanges)
                {
                    privacyBlocked = true;
                    nextPrivacyAttempt = 0;
                }
                if (clock.Elapsed.TotalSeconds >= nextDntWarning)
                {
                    log("Could not save a game DNT preference. The player's collection remains blocked; check storage access.");
                    nextDntWarning = clock.Elapsed.TotalSeconds + 5;
                }
            }
        }
    }

    public bool AllowCollection(string id, bool participating)
    {
        lock (gate)
        {
            if (privacy.IsDoNotTrackEnabled(id)) return false;
            try
            {
                if (privacy.Allow(id))
                {
                    tracker.Forget(id);
                    RemoveConnected(id);
                }
                if (!privacy.IsAllowed(id)) return false;
                Join(id);
                if (participating) tracker.Participate(id, clock.Elapsed.TotalSeconds);
                return true;
            }
            catch (Exception error) when (IsStorageFailure(error))
            {
                log("Could not save a player's collection approval. No new approval was applied; check storage permissions and available space.");
                return false;
            }
        }
    }

    public bool WithdrawCollection(string id)
    {
        lock (gate)
        {
            tracker.Forget(id);
            RemoveConnected(id);
            try { privacy.Withdraw(id); return true; }
            catch (Exception error) when (IsStorageFailure(error))
            {
                privacyBlocked = true;
                nextPrivacyAttempt = 0;
                log("A collection withdrawal could not be saved. Tracking has stopped for that player and uploads are paused until preferences can be persisted.");
                return false;
            }
        }
    }

    public bool ProcessPrivacyChanges() => SynchronizePrivacy(CancellationToken.None, true);

    private bool SynchronizePrivacy(CancellationToken cancellationToken, bool force = false)
    {
        if (!force && clock.Elapsed.TotalSeconds < nextPrivacyAttempt) return false;
        long revision = privacy.Revision;
        if (!privacy.HasUnsavedChanges && Interlocked.Read(ref sanitizedPrivacyRevision) == revision) return true;
        try
        {
            privacy.Flush();
            int erased = outbox.RemoveDisallowed(privacy.AllowsContribution, () => cancellationToken.IsCancellationRequested);
            if (erased > 0) log($"Removed {erased} pending player contributions under local privacy preferences.");
            privacyBlocked = false;
            if (privacy.Revision != revision) return false;
            Interlocked.Exchange(ref sanitizedPrivacyRevision, revision);
            return true;
        }
        catch (Exception error) when (IsStorageFailure(error) || error is InvalidOperationException)
        {
            if (!privacyBlocked)
                log("Privacy cleanup could not complete. Uploads are paused. Check encrypted files, permissions, and whether a mixed schema-v1 batch needs migration.");
            privacyBlocked = true;
            nextPrivacyAttempt = clock.Elapsed.TotalSeconds + 5;
            return false;
        }
    }

    private bool BatchAllowed(StatsBatch batch) => batch.Players.All(player => privacy.AllowsContribution(player, batch.SchemaVersion));

    private void RemoveConnected(string id)
    {
        online.Remove(id);
        profiles.Remove(id);
        requestedProfiles.Remove(id);
        int queued = profileRequests.Count;
        for (int index = 0; index < queued; index++)
        {
            string waiting = profileRequests.Dequeue();
            if (waiting != id) profileRequests.Enqueue(waiting);
        }
    }

    private static bool IsStorageFailure(Exception error) => error is IOException || error is UnauthorizedAccessException ||
        error is CryptographicException || error is SerializationException;
}
