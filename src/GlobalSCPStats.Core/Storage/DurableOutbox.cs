using System;
using System.IO;
using System.Linq;
using System.Globalization;
using GlobalSCPStats.Core.Models;

namespace GlobalSCPStats.Core.Storage;

public sealed class DurableOutbox
{
    private readonly object gate = new object();
    private readonly string directory;
    private readonly EncryptedFileStore store;
    private readonly int maxBatches;
    private readonly TimeSpan maxAge;

    public DurableOutbox(string directory, int maxBatches, byte[] storageKey, TimeSpan maxAge)
    {
        if (maxBatches < 1) throw new ArgumentOutOfRangeException(nameof(maxBatches));
        if (maxAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        Directory.CreateDirectory(directory);
        this.directory = directory;
        this.maxBatches = maxBatches;
        this.maxAge = maxAge;
        store = new EncryptedFileStore(storageKey);
        // Refuse a changed key before new batches can be mixed into an existing queue.
        string? existing = Files().FirstOrDefault();
        if (existing != null) store.Read(existing);
    }

    public int Count { get { lock (gate) return Files().Length; } }

    public bool TryEnqueue(StatsBatch batch)
    {
        lock (gate)
        {
            if (Files().Length >= maxBatches) return false;
            if (batch.SchemaVersion == 2)
                foreach (PlayerStats player in batch.Players)
                    if (string.IsNullOrEmpty(player.ContributionId)) player.ContributionId = Guid.NewGuid().ToString("D");
            string path = Path.Combine(directory, $"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}.gss");
            store.WriteNew(path, BatchJson.Serialize(batch));
            return true;
        }
    }

    public QueuedBatch? Peek()
    {
        lock (gate)
        {
            string? path = Files().FirstOrDefault();
            if (path == null) return null;
            byte[] json = store.Read(path);
            return new QueuedBatch(path, BatchJson.Deserialize(json), json);
        }
    }

    public void Acknowledge(QueuedBatch batch)
    {
        lock (gate) File.Delete(batch.Path);
    }

    public int PruneExpired(DateTime utcNow)
    {
        lock (gate)
        {
            int removed = 0;
            foreach (string path in Files().Concat(Directory.GetFiles(directory, "*.gss.tmp")).Concat(Directory.GetFiles(directory, "privacy.*.state.tmp")))
            {
                DateTime created = File.GetLastWriteTimeUtc(path);
                string name = Path.GetFileName(path);
                if (name.Length >= 19 && long.TryParse(name.Substring(0, 19), NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) &&
                    ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks)
                    created = new DateTime(Math.Min(ticks, created.Ticks), DateTimeKind.Utc);
                if (utcNow - created <= maxAge) continue;
                File.Delete(path);
                removed++;
            }
            return removed;
        }
    }

    public int RemoveDisallowed(Func<PlayerStats, int, bool> isAllowed, Func<bool>? isCancelled = null)
    {
        lock (gate)
        {
            int removed = 0;
            foreach (string path in Files())
            {
                if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
                StatsBatch batch = BatchJson.Deserialize(store.Read(path));
                var remaining = batch.Players.Where(player => isAllowed(player, batch.SchemaVersion)).ToList();
                if (remaining.Count == batch.Players.Count) continue;
                if (remaining.Count > 0 && (batch.SchemaVersion != 2 || remaining.Any(p => !Guid.TryParse(p.ContributionId, out _))))
                    throw new InvalidOperationException("A mixed legacy batch cannot be rewritten safely. Upgrade/migrate it before resuming uploads.");
                int erased = batch.Players.Count - remaining.Count;
                if (remaining.Count == 0) File.Delete(path);
                else
                {
                    // Surviving contribution IDs remain unchanged; schema v2 backends deduplicate them.
                    batch.Players = remaining;
                    batch.BatchId = Guid.NewGuid().ToString("D");
                    DateTime previousWriteTime = File.GetLastWriteTimeUtc(path);
                    store.Replace(path, BatchJson.Serialize(batch));
                    File.SetLastWriteTimeUtc(path, previousWriteTime);
                }
                removed += erased;
            }
            return removed;
        }
    }

    private string[] Files() => Directory.GetFiles(directory, "*.gss").OrderBy(p => p, StringComparer.Ordinal).ToArray();
}

public sealed class QueuedBatch
{
    internal QueuedBatch(string path, StatsBatch batch, byte[] json)
    {
        Path = path;
        Batch = batch;
        Json = json;
    }

    internal string Path { get; }
    public StatsBatch Batch { get; }
    public byte[] Json { get; }
}
