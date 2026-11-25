using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GlobalSCPStats.Core;
using GlobalSCPStats.Core.Models;
using GlobalSCPStats.Core.Privacy;
using GlobalSCPStats.Core.Storage;
using GlobalSCPStats.Core.Tracking;
using GlobalSCPStats.Core.Transport;

namespace GlobalSCPStats.Tests;

internal static partial class Program
{
    private static void RunPrivacyTests()
    {
        Run("optional manual approval persists across restart", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var preferences = Consent(temp.Path, key);
            True(!preferences.IsAllowed(Alice));
            True(preferences.Allow(Alice));
            string collection = preferences.GetCollectionId(Alice)!;
            True(Guid.TryParse(collection, out _));
            Equal(collection, Consent(temp.Path, key).GetCollectionId(Alice)!);
            True(!preferences.Allow(Alice));
        });
        Run("saved preferences contain keyed markers rather than raw player IDs", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            Consent(temp.Path, key).Allow(Alice);
            string path = System.IO.Path.Combine(temp.Path, "privacy.state");
            string encrypted = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            string decrypted = Encoding.UTF8.GetString(new EncryptedFileStore(key).Read(path));
            True(!encrypted.Contains(Alice));
            True(!decrypted.Contains(Alice));
            True(!decrypted.Contains("@steam"));
        });
        Run("withdrawal remains effective after reconnect or restart", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var preferences = Consent(temp.Path, key);
            preferences.Allow(Alice);
            preferences.Withdraw(Alice);
            True(!preferences.IsAllowed(Alice));
            True(!Consent(temp.Path, key).IsAllowed(Alice));
        });
        Run("renewed approval cannot reactivate old queued contributions", () =>
        {
            using var temp = new TestDirectory();
            var preferences = Consent(temp.Path, NewKey());
            preferences.Allow(Alice);
            var old = new PlayerStats { UserId = Alice, CollectionId = preferences.GetCollectionId(Alice)! };
            preferences.Withdraw(Alice);
            preferences.Allow(Alice);
            True(preferences.IsAllowed(Alice));
            True(!preferences.AllowsContribution(old, 2));
            True(old.CollectionId != preferences.GetCollectionId(Alice));
        });
        Run("notice version changes require renewed approval under opt-in mode", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var original = Consent(temp.Path, key);
            original.Allow(Alice);
            string oldCollection = original.GetCollectionId(Alice)!;
            var updated = new PrivacyPreferences(temp.Path, key, new PrivacyOptions { NoticeVersion = "2", RequirePlayerOptIn = true });
            True(!updated.IsAllowed(Alice));
            updated.Allow(Alice);
            True(updated.IsAllowed(Alice));
            True(oldCollection != updated.GetCollectionId(Alice));
        });
        Run("an explicit withdrawal overrides automatic server collection policy", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var automatic = new PrivacyPreferences(temp.Path, key, new PrivacyOptions { RequirePlayerOptIn = false });
            True(automatic.IsAllowed(Alice));
            automatic.Withdraw(Alice);
            True(!new PrivacyPreferences(temp.Path, key, new PrivacyOptions { RequirePlayerOptIn = false }).IsAllowed(Alice));
        });
        Run("corrupt or wrong-key privacy preferences fail closed", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            Consent(temp.Path, key).Allow(Alice);
            Throws<CryptographicException>(() => Consent(temp.Path, NewKey()));
            string path = System.IO.Path.Combine(temp.Path, "privacy.state");
            byte[] bytes = File.ReadAllBytes(path);
            bytes[30] ^= 1;
            File.WriteAllBytes(path, bytes);
            Throws<CryptographicException>(() => Consent(temp.Path, key));
            True(File.Exists(path));
        });
        Run("failed approval persistence never enables collection", () =>
        {
            using var temp = new TestDirectory();
            var preferences = Consent(temp.Path, NewKey());
            Directory.CreateDirectory(System.IO.Path.Combine(temp.Path, "privacy.state"));
            ThrowsStorage(() => preferences.Allow(Alice));
            True(!preferences.IsAllowed(Alice));
        });
        Run("failed withdrawal persistence blocks immediately and can recover", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var preferences = new PrivacyPreferences(temp.Path, key, new PrivacyOptions { RequirePlayerOptIn = false });
            string obstruction = System.IO.Path.Combine(temp.Path, "privacy.state");
            Directory.CreateDirectory(obstruction);
            ThrowsStorage(() => preferences.Withdraw(Alice));
            True(!preferences.IsAllowed(Alice));
            True(preferences.HasUnsavedChanges);
            Directory.Delete(obstruction);
            preferences.Flush();
            True(!preferences.HasUnsavedChanges);
            True(!new PrivacyPreferences(temp.Path, key, new PrivacyOptions { RequirePlayerOptIn = false }).IsAllowed(Alice));
        });
        Run("privacy rewrite preserves unrelated counters and contribution IDs", () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            var batch = MixedBatch();
            outbox.TryEnqueue(batch);
            string bobId = batch.Players.Single(p => p.UserId == Bob).ContributionId;
            Equal(1, outbox.RemoveDisallowed((player, _) => player.UserId != Alice));
            var remaining = outbox.Peek()!.Batch;
            True(remaining.BatchId != batch.BatchId);
            Equal(Bob, remaining.Players.Single().UserId);
            Equal(bobId, remaining.Players.Single().ContributionId);
            Equal(7L, remaining.Players.Single().XpEarned);
            Equal(3L, remaining.Players.Single().Actions["player.item_used"]);
            Equal(batch.CreatedAt, remaining.CreatedAt);
        });
        Run("privacy rewrite retries cannot double count another player's XP", () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            outbox.TryEnqueue(MixedBatch());
            var accepted = new HashSet<string>();
            long bobXp = 0;
            Action<StatsBatch> accept = batch =>
            {
                foreach (var player in batch.Players)
                    if (accepted.Add(player.ContributionId) && player.UserId == Bob) bobXp += player.XpEarned;
            };
            accept(outbox.Peek()!.Batch); // The backend accepted it, but the receipt was lost.
            outbox.RemoveDisallowed((player, _) => player.UserId != Alice);
            accept(outbox.Peek()!.Batch);
            Equal(7L, bobXp);
        });
        Run("privacy removal deletes a fully disallowed queued batch", () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            outbox.TryEnqueue(MixedBatch());
            Equal(2, outbox.RemoveDisallowed((_, _) => false));
            Equal(0, outbox.Count);
        });
        Run("mixed legacy batches are preserved rather than unsafely rewritten", () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            var batch = MixedBatch();
            batch.SchemaVersion = 1;
            outbox.TryEnqueue(batch);
            byte[] original = outbox.Peek()!.Json;
            Throws<InvalidOperationException>(() => outbox.RemoveDisallowed((player, _) => player.UserId != Alice));
            True(original.SequenceEqual(outbox.Peek()!.Json));
        });
        Run("privacy rewrites do not extend the retention deadline", () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            outbox.TryEnqueue(MixedBatch());
            File.SetLastWriteTimeUtc(Directory.GetFiles(temp.Path, "*.gss").Single(), DateTime.UtcNow.AddHours(-73));
            outbox.RemoveDisallowed((player, _) => player.UserId != Alice);
            Equal(1, outbox.PruneExpired(DateTime.UtcNow));
        });
        Run("clearing a player removes memory stats without touching other players", () =>
        {
            var tracker = Active();
            tracker.Death(Alice, Bob);
            tracker.Forget(Alice);
            True(tracker.GetSession(Alice, 1) == null);
            True(tracker.Snapshot(1).All(p => p.UserId != Alice));
            Equal(1L, tracker.GetSession(Bob, 1)!.Kills);
        });
        Run("runtime collection and queued cleanup follow the saved player preference", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var preferences = Consent(temp.Path, key);
            var outbox = Box(temp.Path, key);
            using var runtime = new StatsRuntime("test-server", outbox, preferences, TimeSpan.FromSeconds(30), null, _ => { });
            runtime.Join(Alice);
            runtime.StartRound(new[] { Alice });
            runtime.Action(Alice, "player.item_used");
            True(runtime.GetSession(Alice) == null);
            Equal(0, runtime.OnlinePlayers);
            True(runtime.AllowCollection(Alice, true));
            runtime.Action(Alice, "player.item_used");
            runtime.Checkpoint();
            Equal(1, outbox.Count);
            True(runtime.WithdrawCollection(Alice));
            True(runtime.GetSession(Alice) == null);
            True(runtime.PrivacyCleanupPending);
            True(runtime.ProcessPrivacyChanges());
            Equal(0, outbox.Count);
            runtime.Join(Alice);
            True(runtime.GetSession(Alice) == null);
        });
    }

    private static async Task TestPrivacyTransport()
    {
        await RunAsync("unapproved players produce no tracking or profile network requests", async () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            int requests = 0;
            var api = Client(_ => { Interlocked.Increment(ref requests); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }, true);
            using var runtime = new StatsRuntime("test-server", Box(temp.Path, key), Consent(temp.Path, key), TimeSpan.FromSeconds(30), api, _ => { });
            runtime.Join(Alice);
            runtime.StartRound(new[] { Alice });
            runtime.Start();
            await Task.Delay(50);
            Equal(0, requests);
            Equal(0, runtime.QueuedBatches);
        });
        await RunAsync("withdrawal during a lost upload response removes future records without duplicating other XP", async () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var preferences = Consent(temp.Path, key);
            preferences.Allow(Alice);
            preferences.Allow(Bob);
            var entered = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            int attempts = 0;
            int completed = 0;
            int withdrawn = 0;
            bool leakedAfterWithdrawal = false;
            long bobXp = 0;
            var accepted = new HashSet<string>();
            var api = Client(async request =>
            {
                var batch = BatchJson.Deserialize(await request.Content!.ReadAsByteArrayAsync());
                int attempt = Interlocked.Increment(ref attempts);
                if (Volatile.Read(ref withdrawn) != 0 && batch.Players.Any(p => p.UserId == Alice)) leakedAfterWithdrawal = true;
                foreach (var player in batch.Players)
                    if (accepted.Add(player.ContributionId) && player.UserId == Bob) bobXp += player.XpEarned;
                if (attempt == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task;
                    throw new HttpRequestException("Lost receipt after durable acceptance.");
                }
                Interlocked.Increment(ref completed);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
            using var runtime = new StatsRuntime("test-server", Box(temp.Path, key), preferences, TimeSpan.FromSeconds(30), api, _ => { });
            try
            {
                runtime.Join(Alice);
                runtime.Join(Bob);
                runtime.StartRound(new[] { Alice, Bob });
                runtime.Checkpoint();
                runtime.Start();
                await Until(() => entered.Task.IsCompleted);
                True(runtime.WithdrawCollection(Alice));
                Interlocked.Exchange(ref withdrawn, 1);
                release.TrySetResult(true);
                await Until(() => Volatile.Read(ref completed) >= 1 && !runtime.PrivacyCleanupPending);
                True(!leakedAfterWithdrawal);
                Equal(20L, bobXp);
                True(runtime.GetSession(Alice) == null);
            }
            finally { release.TrySetResult(true); }
        });
    }

    private static PrivacyPreferences Consent(string directory, byte[] key) => new PrivacyPreferences(directory, key, new PrivacyOptions { RequirePlayerOptIn = true });

    private static StatsBatch MixedBatch() => new StatsBatch
    {
        ServerId = "test-server",
        Players = new List<PlayerStats>
        {
            new PlayerStats { UserId = Alice, CollectionId = "alice-approval", XpEarned = 5, Kills = 1 },
            new PlayerStats { UserId = Bob, CollectionId = "bob-approval", XpEarned = 7, Actions = new Dictionary<string, long> { ["player.item_used"] = 3 } },
        },
    };

    private static void ThrowsStorage(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return; }
        throw new Exception("Expected a storage error.");
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        True(condition());
    }
}
