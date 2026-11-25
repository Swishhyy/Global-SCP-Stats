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
using GlobalSCPStats.Core.Experience;
using GlobalSCPStats.Core.Models;
using GlobalSCPStats.Core.Rewards;
using GlobalSCPStats.Core.Storage;
using GlobalSCPStats.Core.Tracking;
using GlobalSCPStats.Core.Transport;

namespace GlobalSCPStats.Tests;

internal static partial class Program
{
    private const string Alice = "76561198000000001@steam";
    private const string Bob = "76561198000000002@steam";
    private static int passed;
    private static int failed;

    private static async Task<int> Main()
    {
        Run("fractional playtime survives checkpoints and disconnects", () =>
        {
            var tracker = new StatsTracker();
            tracker.Join(Alice, 10);
            Near(0.25, tracker.Snapshot(10.25).Single().PlaytimeSeconds);
            tracker.CommitSnapshot();
            tracker.Leave(Alice, 10.75);
            Near(0.5, tracker.Snapshot(50).Single().PlaytimeSeconds);
        });
        Run("duplicate joins and clock regressions do not inflate playtime", () =>
        {
            var tracker = new StatsTracker();
            tracker.Join(Alice, 10);
            tracker.Join(Alice, 12);
            Near(2, tracker.GetSession(Alice, 12)!.PlaytimeSeconds);
            Near(2, tracker.GetSession(Alice, 11)!.PlaytimeSeconds);
            Near(3, tracker.GetSession(Alice, 13)!.PlaytimeSeconds);
        });
        Run("uncommitted snapshots retain stats after failed persistence", () =>
        {
            var tracker = new StatsTracker();
            tracker.Join(Alice, 0);
            Near(2, tracker.Snapshot(2).Single().PlaytimeSeconds);
            Near(3, tracker.Snapshot(3).Single().PlaytimeSeconds);
            tracker.CommitSnapshot();
            Near(1, tracker.Snapshot(4).Single().PlaytimeSeconds);
        });
        Run("rounds count participation once across respawns and reconnects", () =>
        {
            var tracker = new StatsTracker();
            tracker.Join(Alice, 0);
            tracker.StartRound(new[] { Alice });
            tracker.Participate(Alice);
            tracker.Leave(Alice, 1);
            tracker.Join(Alice, 2);
            tracker.Participate(Alice);
            Equal(1L, tracker.Snapshot(3).Single().RoundsPlayed);
            tracker.EndRound();
            tracker.StartRound(new[] { Alice });
            Equal(2L, tracker.Snapshot(4).Single().RoundsPlayed);
        });
        Run("warmup actions do not grant gameplay stats or XP", () =>
        {
            var tracker = new StatsTracker();
            tracker.Join(Alice, 0);
            tracker.Death(Alice, Bob);
            tracker.Escape(Alice);
            tracker.Action(Alice, "player.shot", 0);
            var stats = tracker.GetSession(Alice, 1)!;
            Equal(0L, stats.Deaths);
            Equal(0L, stats.XpEarned);
            Equal(0, stats.Actions.Count);
        });
        Run("world deaths and suicides do not award a kill", () =>
        {
            var tracker = Active();
            tracker.Death(Alice, null);
            tracker.Death(Alice, Alice);
            tracker.Death(Alice, Bob);
            Equal(3L, tracker.GetSession(Alice, 1)!.Deaths);
            Equal(0L, tracker.GetSession(Alice, 1)!.Kills);
            Equal(1L, tracker.GetSession(Bob, 1)!.Kills);
        });
        Run("escape callbacks count once per round", () =>
        {
            var tracker = Active();
            tracker.Escape(Alice);
            tracker.Escape(Alice);
            Equal(1L, tracker.GetSession(Alice, 1)!.Escapes);
            tracker.EndRound();
            tracker.StartRound(new[] { Alice });
            tracker.Escape(Alice);
            Equal(2L, tracker.GetSession(Alice, 2)!.Escapes);
        });
        Run("unsupported, host, and malformed IDs are not tracked", () =>
        {
            var tracker = new StatsTracker();
            foreach (string id in new[] { "", "host", "npc@server", "abc@steam", "1@unknown", "1@steam@discord" }) tracker.Join(id, 0);
            Equal(0, tracker.OnlinePlayers);
            tracker.Join("123456789@discord", 0);
            Equal(1, tracker.OnlinePlayers);
        });
        Run("XP disabled still tracks every action and damage", () =>
        {
            var tracker = Active(new ExperienceOptions { Enabled = false });
            tracker.Action(Alice, "player.door_interacting", 1);
            tracker.Damage(Bob, Alice, 42, 1);
            tracker.Heal(Alice, 10, 1);
            var stats = tracker.GetSession(Alice, 2)!;
            Equal(0L, stats.XpEarned);
            Equal(1L, stats.Actions["player.door_interacting"]);
            Near(42, stats.DamageDealt);
            Near(10, stats.HealthRestored);
        });
        Run("all actions earn configurable default XP", () =>
        {
            var tracker = Active(new ExperienceOptions { DefaultReward = 3 });
            long before = tracker.GetSession(Alice, 0)!.XpEarned;
            tracker.Action(Alice, "player.door_interacting", 1);
            tracker.Action(Alice, "player.item_added", 1);
            Equal(before + 6, tracker.GetSession(Alice, 2)!.XpEarned);
        });
        Run("cooldowns suppress XP without dropping action counters", () =>
        {
            var tracker = Active();
            long before = tracker.GetSession(Alice, 0)!.XpEarned;
            tracker.Action(Alice, "player.door_interacting", 1);
            tracker.Action(Alice, "player.door_interacting", 2);
            tracker.Action(Alice, "player.door_interacting", 11);
            var stats = tracker.GetSession(Alice, 12)!;
            Equal(before + 2, stats.XpEarned);
            Equal(3L, stats.Actions["player.door_interacting"]);
        });
        Run("rolling XP limit survives reconnect and expires after a minute", () =>
        {
            var options = new ExperienceOptions { DefaultReward = 3, MaxXpPerMinute = 6, DefaultCooldownSeconds = 0 };
            options.Rewards["round_played"] = 0;
            var tracker = Active(options);
            tracker.Action(Alice, "test.action", 1);
            tracker.Action(Alice, "test.action", 2);
            tracker.Leave(Alice, 3);
            tracker.Join(Alice, 4);
            tracker.Action(Alice, "test.action", 5);
            Equal(0L, tracker.GetSession(Alice, 5)!.XpEarned);
            tracker.Action(Alice, "test.action", 61);
            Equal(3L, tracker.GetSession(Alice, 61)!.XpEarned);
        });
        Run("per-action zero reward retains statistics", () =>
        {
            var options = new ExperienceOptions();
            options.Rewards["player.item_added"] = 0;
            var tracker = Active(options);
            long before = tracker.GetSession(Alice, 0)!.XpEarned;
            tracker.Action(Alice, "player.item_added", 1);
            Equal(before, tracker.GetSession(Alice, 2)!.XpEarned);
            Equal(1L, tracker.GetSession(Alice, 2)!.Actions["player.item_added"]);
        });
        Run("snapshot copies cannot mutate live action stats", () =>
        {
            var tracker = Active();
            var snapshot = tracker.Snapshot(1);
            snapshot.Single(s => s.UserId == Alice).Actions["round_played"] = 999;
            Equal(1L, tracker.GetSession(Alice, 1)!.Actions["round_played"]);
        });
        Run("invalid and self-inflicted damage never grants offensive damage", () =>
        {
            var tracker = Active();
            tracker.Damage(Alice, Bob, double.NaN, 1);
            tracker.Damage(Alice, Bob, -5, 1);
            tracker.Damage(Alice, Alice, 10, 1);
            Near(10, tracker.GetSession(Alice, 2)!.DamageReceived);
            Near(0, tracker.GetSession(Alice, 2)!.DamageDealt);
            Near(0, tracker.GetSession(Bob, 2)!.DamageDealt);
        });
        Run("encrypted outbox persists exact payload across restarts", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var outbox = Box(temp.Path, key);
            var batch = Batch();
            True(outbox.TryEnqueue(batch));
            byte[] original = outbox.Peek()!.Json;
            var restarted = Box(temp.Path, key);
            Equal(batch.BatchId, restarted.Peek()!.Batch.BatchId);
            True(original.SequenceEqual(restarted.Peek()!.Json));
            string fileText = Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(temp.Path, "*.gss").Single()));
            True(fileText.IndexOf(Alice, StringComparison.Ordinal) < 0);
            Equal(1, Directory.GetFiles(temp.Path).Length);
            restarted.Acknowledge(restarted.Peek()!);
            Equal(0, restarted.Count);
        });
        Run("encrypted files use different random IVs for identical payloads", () =>
        {
            using var temp = new TestDirectory();
            var store = new EncryptedFileStore(NewKey());
            byte[] message = Encoding.UTF8.GetBytes(Alice);
            store.WriteNew(System.IO.Path.Combine(temp.Path, "one.gss"), message);
            store.WriteNew(System.IO.Path.Combine(temp.Path, "two.gss"), message);
            True(!File.ReadAllBytes(System.IO.Path.Combine(temp.Path, "one.gss")).SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(temp.Path, "two.gss"))));
        });
        Run("tampered outbox is rejected and preserved", () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            outbox.TryEnqueue(Batch());
            string file = Directory.GetFiles(temp.Path, "*.gss").Single();
            byte[] bytes = File.ReadAllBytes(file);
            bytes[25] ^= 1;
            File.WriteAllBytes(file, bytes);
            Throws<CryptographicException>(() => outbox.Peek());
            Equal(1, outbox.Count);
        });
        Run("wrong encryption key cannot read or erase pending stats", () =>
        {
            using var temp = new TestDirectory();
            Box(temp.Path, NewKey()).TryEnqueue(Batch());
            Throws<CryptographicException>(() => Box(temp.Path, NewKey()));
            Equal(1, Directory.GetFiles(temp.Path, "*.gss").Length);
        });
        Run("retention expires old batches while preserving recent ones", () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            outbox.TryEnqueue(Batch());
            string old = Directory.GetFiles(temp.Path, "*.gss").Single();
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-73));
            outbox.TryEnqueue(Batch());
            Equal(1, outbox.PruneExpired(DateTime.UtcNow));
            Equal(1, outbox.Count);
        });
        Run("retention removes orphaned encrypted temporary files", () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            string orphan = System.IO.Path.Combine(temp.Path, "orphan.gss.tmp");
            File.WriteAllText(orphan, "incomplete encrypted write");
            File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddHours(-73));
            Equal(1, outbox.PruneExpired(DateTime.UtcNow));
            True(!File.Exists(orphan));
        });
        Run("full outbox retains new deltas until capacity is restored", () =>
        {
            using var temp = new TestDirectory();
            var outbox = new DurableOutbox(temp.Path, 1, NewKey(), TimeSpan.FromHours(72));
            using var runtime = new StatsRuntime("test-server", outbox, Preferences(temp.Path), TimeSpan.FromSeconds(30), null, _ => { });
            runtime.Join(Alice);
            runtime.StartRound(new[] { Alice });
            True(runtime.Checkpoint());
            runtime.Action(Alice, "player.shot");
            True(!runtime.Checkpoint());
            outbox.Acknowledge(outbox.Peek()!);
            True(runtime.Checkpoint());
            Equal(1L, outbox.Peek()!.Batch.Players.Single().Actions["player.shot"]);
        });
        Run("normal shutdown checkpoints outstanding stats", () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            using (var runtime = new StatsRuntime("test-server", outbox, Preferences(temp.Path), TimeSpan.FromSeconds(30), null, _ => { }))
            {
                runtime.Join(Alice);
                runtime.StartRound(new[] { Alice });
                runtime.Escape(Alice);
            }
            Equal(1L, outbox.Peek()!.Batch.Players.Single().Escapes);
        });
        Run("runtime serializes concurrent events without losing counts", () =>
        {
            using var temp = new TestDirectory();
            using var runtime = new StatsRuntime("test-server", Box(temp.Path, NewKey()), Preferences(temp.Path), TimeSpan.FromSeconds(30), null, _ => { });
            runtime.Join(Alice);
            runtime.Join(Bob);
            runtime.StartRound(new[] { Alice, Bob });
            Parallel.For(0, 500, _ => runtime.Death(Alice, Bob));
            Equal(500L, runtime.GetSession(Bob)!.Kills);
            Equal(500L, runtime.GetSession(Alice)!.Deaths);
        });
        Run("wire JSON preserves dictionary actions and numeric XP", () =>
        {
            var batch = Batch();
            batch.Players[0].Actions["player.item_used"] = 2;
            batch.Players[0].XpEarned = 10;
            byte[] json = BatchJson.Serialize(batch);
            True(Encoding.UTF8.GetString(json).Contains("\"actions\":{\"player.item_used\":2}"));
            Equal(10L, BatchJson.Deserialize(json).Players.Single().XpEarned);
        });
        Run("HTTP, embedded credentials, and cross-host profile endpoints are refused", () =>
        {
            foreach (string url in new[] { "http://example.test/api", "https://secret@example.test/api", "https://example.test/api?key=secret", "https://example.test/api#fragment" })
                Throws<ArgumentException>(() => new UploadOptions(url, "test-server", "test-token"));
            Throws<ArgumentException>(() => new UploadOptions("https://example.test/api", "test-server", "test-token", "https://evil.test/profiles"));
            Throws<ArgumentException>(() => new UploadOptions("https://example.test/api", "test-server", "token\r\ninjection"));
        });
        Run("milestones support XP, levels, stats, and action counters", () =>
        {
            var stats = new PlayerStats { XpEarned = 1000, Kills = 25, Actions = new Dictionary<string, long> { ["player.item_used"] = 10 } };
            True(new MilestoneRule { RewardId = "level_10", Metric = "level", Threshold = 10 }.IsEarned(stats, 10));
            True(new MilestoneRule { RewardId = "xp_1000", Metric = "xp", Threshold = 1000 }.IsEarned(stats, 10));
            True(!new MilestoneRule { RewardId = "kills_100", Metric = "kills", Threshold = 100 }.IsEarned(stats, 10));
            True(new MilestoneRule { RewardId = "items_10", Metric = "action:player.item_used", Threshold = 10 }.IsEarned(stats, 10));
        });
        Run("profiles reject another player's rewards and malformed reward IDs", () =>
        {
            Throws<System.Runtime.Serialization.SerializationException>(() => ProfileJson.Deserialize(Profile(Bob), Alice));
            Throws<System.Runtime.Serialization.SerializationException>(() => ProfileJson.Deserialize(Profile(Alice, "<admin>"), Alice));
            Equal("level_10", ProfileJson.Deserialize(Profile(Alice), Alice).RewardIds.Single());
        });

        Run("server stats decode independently of backend reward IDs", () =>
        {
            var json = Encoding.UTF8.GetBytes("{\"user_id\":\"" + Alice + "\",\"total_xp\":500,\"level\":2,\"revision\":1,\"server_stats\":{\"xp_earned\":500,\"kills\":4,\"actions\":{\"player.item_used\":3}}}");
            var profile = ProfileJson.Deserialize(json, Alice);
            Equal(500L, profile.ServerStats!.XpEarned);
            Equal(3L, profile.ServerStats.Actions["player.item_used"]);
            Equal(0, profile.RewardIds.Count);
            True(new MilestoneRule { RewardId = "local_kills", Metric = "kills", Threshold = 4 }.IsEarned(profile.ServerStats, 2));
        });

        RunPrivacyTests();
        RunDoNotTrackTests();
        await TestTransport();
        await TestPrivacyTransport();
        await TestDoNotTrackTransport();
        Console.WriteLine($"\n{passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task TestTransport()
    {
        await RunAsync("authenticated request preserves body and idempotency key", async () =>
        {
            using var temp = new TestDirectory();
            var box = Box(temp.Path, NewKey());
            var batch = Batch();
            box.TryEnqueue(batch);
            using var client = Client(async request =>
            {
                Equal("Bearer", request.Headers.Authorization!.Scheme);
                Equal("test-token", request.Headers.Authorization.Parameter!);
                Equal(batch.BatchId, request.Headers.GetValues("Idempotency-Key").Single());
                True((await request.Content!.ReadAsByteArrayAsync()).SequenceEqual(box.Peek()!.Json));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
            Equal(UploadOutcome.Accepted, (await client.SendAsync(box.Peek()!, CancellationToken.None)).Outcome);
            Equal(1, box.Count); // Only the runtime may acknowledge after durable acceptance.
        });
        foreach (int status in new[] { 408, 429, 500, 503 })
        {
            await RunAsync($"HTTP {status} retains a retryable batch", async () =>
            {
                using var temp = new TestDirectory();
                var box = Box(temp.Path, NewKey());
                box.TryEnqueue(Batch());
                using var client = Client(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
                Equal(UploadOutcome.Retry, (await client.SendAsync(box.Peek()!, CancellationToken.None)).Outcome);
                Equal(1, box.Count);
            });
        }
        foreach (int status in new[] { 301, 302, 400, 401, 403, 409, 422 })
        {
            await RunAsync($"HTTP {status} pauses uploads and preserves evidence", async () =>
            {
                using var temp = new TestDirectory();
                var box = Box(temp.Path, NewKey());
                box.TryEnqueue(Batch());
                using var client = Client(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
                var result = await client.SendAsync(box.Peek()!, CancellationToken.None);
                Equal(UploadOutcome.Blocked, result.Outcome);
                True(!result.Message.Contains("test-token"));
                Equal(1, box.Count);
            });
        }
        await RunAsync("lost response retries exactly the same batch without leaking exception text", async () =>
        {
            using var temp = new TestDirectory();
            var box = Box(temp.Path, NewKey());
            box.TryEnqueue(Batch());
            var attempts = new List<string>();
            using var client = Client(request =>
            {
                attempts.Add(request.Headers.GetValues("Idempotency-Key").Single());
                if (attempts.Count == 1) throw new HttpRequestException("test-token should never be logged");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            });
            var first = await client.SendAsync(box.Peek()!, CancellationToken.None);
            Equal(UploadOutcome.Retry, first.Outcome);
            True(!first.Message.Contains("test-token"));
            Equal(UploadOutcome.Accepted, (await client.SendAsync(box.Peek()!, CancellationToken.None)).Outcome);
            Equal(attempts[0], attempts[1]);
        });
        await RunAsync("server identity changes cannot leak old queued records", async () =>
        {
            using var temp = new TestDirectory();
            var box = Box(temp.Path, NewKey());
            var batch = Batch();
            batch.ServerId = "different-server";
            box.TryEnqueue(batch);
            bool called = false;
            using var client = Client(_ => { called = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); });
            Equal(UploadOutcome.Blocked, (await client.SendAsync(box.Peek()!, CancellationToken.None)).Outcome);
            True(!called);
        });
        await RunAsync("profile fetch checks identity and receives backend-approved rewards", async () =>
        {
            using var client = Client(request =>
            {
                Equal(HttpMethod.Post, request.Method);
                Equal("/v1/profiles/lookup", request.RequestUri!.AbsolutePath);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Profile(Alice)) });
            }, true);
            var profile = await client.FetchProfileAsync(Alice, CancellationToken.None);
            Equal(10, profile!.Level);
            Equal("level_10", profile.RewardIds.Single());
        });
        await RunAsync("oversized profiles cannot inject rewards", async () =>
        {
            using var client = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string(' ', 70000)) }), true);
            try { await client.FetchProfileAsync(Alice, CancellationToken.None); throw new Exception("Expected profile rejection."); }
            catch (System.Runtime.Serialization.SerializationException) { }
        });
        await RunAsync("runtime uploads durable batches and caches only backend-approved progress", async () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            var options = new UploadOptions("https://example.test/v1/stats/batches", "test-server", "test-token", "https://example.test/v1/profiles/lookup");
            var api = new StatsApiClient(options, new FakeHandler(request => Task.FromResult(
                request.RequestUri!.AbsolutePath.EndsWith("lookup", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Profile(Alice)) }
                    : new HttpResponseMessage(HttpStatusCode.NoContent))));
            using var runtime = new StatsRuntime("test-server", outbox, Preferences(temp.Path), TimeSpan.FromSeconds(30), api, _ => { });
            runtime.Join(Alice);
            runtime.StartRound(new[] { Alice });
            runtime.Checkpoint();
            runtime.Start();
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while ((runtime.QueuedBatches != 0 || runtime.GetProgress(Alice) == null) && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Equal(0, runtime.QueuedBatches);
            Equal("level_10", runtime.GetProgress(Alice)!.RewardIds.Single());
            runtime.Leave(Alice);
            True(runtime.GetProgress(Alice) == null);
        });
        await RunAsync("expired batches are removed before the first upload after restart", async () =>
        {
            using var temp = new TestDirectory();
            var outbox = Box(temp.Path, NewKey());
            outbox.TryEnqueue(Batch());
            File.SetLastWriteTimeUtc(Directory.GetFiles(temp.Path, "*.gss").Single(), DateTime.UtcNow.AddHours(-73));
            int uploads = 0;
            var api = Client(_ => { Interlocked.Increment(ref uploads); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); });
            using var runtime = new StatsRuntime("test-server", outbox, Preferences(temp.Path), TimeSpan.FromSeconds(30), api, _ => { });
            runtime.Start();
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (runtime.QueuedBatches != 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
            Equal(0, runtime.QueuedBatches);
            Equal(0, uploads);
        });
    }

    private static StatsTracker Active(ExperienceOptions? options = null)
    {
        var tracker = new StatsTracker(options);
        tracker.Join(Alice, 0);
        tracker.Join(Bob, 0);
        tracker.StartRound(new[] { Alice, Bob });
        return tracker;
    }

    private static GlobalSCPStats.Core.Privacy.PrivacyPreferences Preferences(string directory) => new GlobalSCPStats.Core.Privacy.PrivacyPreferences(directory, NewKey(), new GlobalSCPStats.Core.Privacy.PrivacyOptions { RequirePlayerOptIn = false });
    private static StatsBatch Batch() => new StatsBatch { ServerId = "test-server", Players = new List<PlayerStats> { new PlayerStats { UserId = Alice, Kills = 1, CollectionId = "server-policy-v1" } } };
    private static DurableOutbox Box(string path, byte[] key) => new DurableOutbox(path, 100, key, TimeSpan.FromHours(72));
    private static byte[] NewKey() { byte[] key = new byte[64]; using var rng = RandomNumberGenerator.Create(); rng.GetBytes(key); return key; }
    private static byte[] Profile(string id, string reward = "level_10") => Encoding.UTF8.GetBytes($"{{\"user_id\":\"{id}\",\"total_xp\":1000,\"level\":10,\"revision\":5,\"reward_ids\":[\"{reward}\"]}}");
    private static StatsApiClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send, bool profiles = false) => new StatsApiClient(
        new UploadOptions("https://example.test/v1/stats/batches", "test-server", "test-token", profiles ? "https://example.test/v1/profiles/lookup" : null), new FakeHandler(send));
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Near(double expected, double actual) => True(Math.Abs(expected - actual) < 0.00001);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Run(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); } }
    private static async Task RunAsync(string name, Func<Task> test) { try { await test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); } }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> send;
        public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => this.send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class TestDirectory : IDisposable
    {
        private readonly string root = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public string Path { get; }
        public TestDirectory() { Path = System.IO.Path.Combine(root, "gss-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public void Dispose()
        {
            string resolved = System.IO.Path.GetFullPath(Path);
            if (!resolved.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Path.GetFileName(resolved).StartsWith("gss-tests-", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}

