using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using GlobalSCPStats.Core;
using GlobalSCPStats.Core.Privacy;
using GlobalSCPStats.Core.Storage;

namespace GlobalSCPStats.Tests;

internal static partial class Program
{
    private static void RunDoNotTrackTests()
    {
        Run("default game policy permits DNT-off players without a separate allow command", () =>
        {
            using var temp = new TestDirectory();
            var preferences = new PrivacyPreferences(temp.Path, NewKey(), new PrivacyOptions());
            preferences.ObserveDoNotTrack(Alice, false);
            True(preferences.IsAllowed(Alice));
            string collection = preferences.GetCollectionId(Alice)!;
            True(!preferences.Allow(Alice));
            Equal(collection, preferences.GetCollectionId(Alice)!);
        });
        Run("DNT overrides saved approval and cannot be bypassed by allowing collection", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var preferences = Consent(temp.Path, key);
            preferences.Allow(Alice);
            preferences.ObserveDoNotTrack(Alice, true);
            True(!preferences.IsAllowed(Alice));
            True(!preferences.Allow(Alice));
            True(Consent(temp.Path, key).IsDoNotTrackEnabled(Alice));
            True(!Consent(temp.Path, key).IsAllowed(Alice));
        });
        Run("turning DNT off resumes the default policy and retires old queued data", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var preferences = new PrivacyPreferences(temp.Path, key, new PrivacyOptions());
            string original = preferences.GetCollectionId(Alice)!;
            preferences.ObserveDoNotTrack(Alice, true);
            preferences.ObserveDoNotTrack(Alice, false);
            True(preferences.IsAllowed(Alice));
            True(preferences.GetCollectionId(Alice) != original);
            True(!preferences.AllowsContribution(new Core.Models.PlayerStats { UserId = Alice, CollectionId = original }, 2));
            True(new PrivacyPreferences(temp.Path, key, new PrivacyOptions()).IsAllowed(Alice));
        });
        Run("DNT changes do not undo an explicit local opt-out", () =>
        {
            using var temp = new TestDirectory();
            var preferences = new PrivacyPreferences(temp.Path, NewKey(), new PrivacyOptions());
            preferences.Withdraw(Alice);
            preferences.ObserveDoNotTrack(Alice, true);
            preferences.ObserveDoNotTrack(Alice, false);
            True(!preferences.IsAllowed(Alice));
            True(preferences.Allow(Alice));
            True(preferences.IsAllowed(Alice));
        });
        Run("failed DNT-on persistence blocks immediately and later flushes", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var preferences = new PrivacyPreferences(temp.Path, key, new PrivacyOptions());
            string obstruction = System.IO.Path.Combine(temp.Path, "privacy.state");
            Directory.CreateDirectory(obstruction);
            ThrowsStorage(() => preferences.ObserveDoNotTrack(Alice, true));
            True(!preferences.IsAllowed(Alice));
            True(preferences.HasUnsavedChanges);
            Directory.Delete(obstruction);
            preferences.Flush();
            True(new PrivacyPreferences(temp.Path, key, new PrivacyOptions()).IsDoNotTrackEnabled(Alice));
        });
        Run("failed DNT-off persistence does not resume collection prematurely", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var preferences = new PrivacyPreferences(temp.Path, key, new PrivacyOptions());
            preferences.ObserveDoNotTrack(Alice, true);
            string path = System.IO.Path.Combine(temp.Path, "privacy.state");
            File.Delete(path);
            Directory.CreateDirectory(path);
            ThrowsStorage(() => preferences.ObserveDoNotTrack(Alice, false));
            True(!preferences.IsAllowed(Alice));
            Directory.Delete(path);
            preferences.ObserveDoNotTrack(Alice, false);
            True(preferences.IsAllowed(Alice));
        });
        Run("DNT immediately clears tracking and removes pending local contributions", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var outbox = Box(temp.Path, key);
            var preferences = new PrivacyPreferences(temp.Path, key, new PrivacyOptions());
            using var runtime = new StatsRuntime("test-server", outbox, preferences, TimeSpan.FromSeconds(30), null, _ => { });
            runtime.ObserveDoNotTrack(Alice, false);
            runtime.Join(Alice);
            runtime.StartRound(new[] { Alice });
            runtime.Checkpoint();
            runtime.ObserveDoNotTrack(Alice, true);
            True(runtime.GetSession(Alice) == null);
            True(!runtime.AllowCollection(Alice, true));
            runtime.Join(Alice);
            runtime.Action(Alice, "player.item_used");
            True(runtime.GetSession(Alice) == null);
            True(runtime.ProcessPrivacyChanges());
            Equal(0, outbox.Count);
            runtime.ObserveDoNotTrack(Alice, false);
            runtime.Join(Alice);
            True(runtime.GetSession(Alice) != null);
        });
        Run("DNT does not erase another player's anonymous kill counters", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var outbox = Box(temp.Path, key);
            using var runtime = new StatsRuntime("test-server", outbox,
                new PrivacyPreferences(temp.Path, key, new PrivacyOptions()), TimeSpan.FromSeconds(30), null, _ => { });
            runtime.ObserveDoNotTrack(Alice, true);
            runtime.Join(Alice);
            runtime.ObserveDoNotTrack(Bob, false);
            runtime.Join(Bob);
            runtime.StartRound(new[] { Alice, Bob });
            runtime.Death(Alice, Bob);
            runtime.Checkpoint();
            Equal(Bob, outbox.Peek()!.Batch.Players.Single().UserId);
            Equal(1L, outbox.Peek()!.Batch.Players.Single().Kills);
        });
        Run("legacy saved local withdrawals remain blocked after preference migration", () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            var preferences = new PrivacyPreferences(temp.Path, key, new PrivacyOptions());
            preferences.Withdraw(Alice);
            string path = System.IO.Path.Combine(temp.Path, "privacy.state");
            var store = new EncryptedFileStore(key);
            string json = Encoding.UTF8.GetString(store.Read(path));
            True(json.Contains("\"Version\":2"));
            json = json.Replace("\"Version\":2", "\"Version\":1")
                .Replace("\"HasLocalPreference\":true,", string.Empty)
                .Replace("\"GameDoNotTrack\":false,", string.Empty);
            True(!json.Contains("HasLocalPreference"));
            store.Replace(path, Encoding.UTF8.GetBytes(json));
            var migrated = new PrivacyPreferences(temp.Path, key, new PrivacyOptions());
            migrated.ObserveDoNotTrack(Alice, false);
            True(!migrated.IsAllowed(Alice));
        });
    }

    private static async Task TestDoNotTrackTransport()
    {
        await RunAsync("DNT clears cached reward profiles and prevents new profile requests", async () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            int requests = 0;
            var api = Client(_ =>
            {
                System.Threading.Interlocked.Increment(ref requests);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Profile(Alice)) });
            }, true);
            using var runtime = new StatsRuntime("test-server", Box(temp.Path, key),
                new PrivacyPreferences(temp.Path, key, new PrivacyOptions()), TimeSpan.FromSeconds(30), api, _ => { });
            runtime.ObserveDoNotTrack(Alice, false);
            runtime.Join(Alice);
            runtime.Start();
            await Until(() => runtime.GetProgress(Alice) != null);
            int before = System.Threading.Volatile.Read(ref requests);
            runtime.ObserveDoNotTrack(Alice, true);
            True(runtime.GetProgress(Alice) == null);
            runtime.Join(Alice);
            await Task.Delay(50);
            Equal(before, System.Threading.Volatile.Read(ref requests));
        });
    }
}
