using System;
using System.IO;
using System.Linq;
using Exiled.API.Features;
using GlobalSCPStats.Core;
using GlobalSCPStats.Core.Storage;
using GlobalSCPStats.Core.Transport;
using GlobalSCPStats.Events;
using GlobalSCPStats.Rewards;
using GlobalSCPStats.Core.Privacy;

namespace GlobalSCPStats;

public sealed class Plugin : Plugin<Config>
{
    public override string Name => "Global SCP Stats";
    public override string Prefix => "global_scp_stats";
    public override string Author => "Swishhyy";
    public override Version Version => new Version(0, 2, 2);
    public override Version RequiredExiledVersion => new Version(9, 15, 2);
    public static Plugin? Instance { get; private set; }
    public StatsRuntime? Runtime { get; private set; }
    private StatsEvents? events;
    private ActionEvents? actions;
    private BadgeRewards? badges;

    public override void OnEnabled()
    {
        Instance = this;
        StatsApiClient? api = null;
        try
        {
            if (!Config.CollectionEnabled)
            {
                Log.Info("Stats collection is off. This development build requires collection_enabled for controlled testing.");
                base.OnEnabled();
                return;
            }
            Config.Validate();
            string? encodedKey = Environment.GetEnvironmentVariable(Config.StorageKeyEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(encodedKey))
                throw new ArgumentException("A storage key environment variable is required before collection can start.");
            byte[] storageKey = Convert.FromBase64String(encodedKey!);
            if (!string.IsNullOrWhiteSpace(Config.UploadEndpoint))
            {
                string? token = Environment.GetEnvironmentVariable(Config.ApiTokenEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(token))
                    Log.Warn("API token environment variable is unset. Collecting locally; uploads are disabled.");
                else
                    api = new StatsApiClient(new UploadOptions(Config.UploadEndpoint, Config.ServerId, token!, Config.ProfileEndpoint));
            }
            else Log.Info("No upload endpoint configured. Stats will be saved locally.");

            string directory = string.IsNullOrWhiteSpace(Config.DataDirectory)
                ? Path.Combine(Paths.Configs, "GlobalSCPStats", Server.Port.ToString())
                : Config.DataDirectory;
            var outbox = new DurableOutbox(directory, Config.MaxQueuedBatches, storageKey, TimeSpan.FromHours(Config.MaxQueueAgeHours));
            var privacy = new PrivacyPreferences(directory, storageKey, Config.Privacy);
            Runtime = new StatsRuntime(Config.ServerId, outbox, privacy, TimeSpan.FromSeconds(Config.CheckpointIntervalSeconds), api, message => Log.Warn(message), Config.Experience);
            events = new StatsEvents(Runtime);
            events.Register();
            actions = new ActionEvents(Runtime);
            actions.Register();
            foreach (Player player in Player.List.Where(StatsEvents.IsEligible)) events.TrackPlayer(player);
            if (Round.IsStarted && !Round.IsEnded)
                Runtime.StartRound(Player.List.Where(p => StatsEvents.IsEligible(p) && p.IsAlive).Select(p => p.UserId));
            Runtime.Start();
            if (Config.BadgeRewardsEnabled)
            {
                badges = new BadgeRewards(Runtime, Config.RewardBadges, Config.PreserveExistingBadges);
                badges.Start();
            }
            Log.Info($"Global SCP Stats {Version} enabled. Use gstats in Remote Admin for status.");
        }
        catch (Exception error)
        {
            events?.Unregister();
            actions?.Unregister();
            badges?.Stop();
            events = null;
            try { if (Runtime != null) Runtime.Dispose(); else api?.Dispose(); }
            catch { Log.Error("Could not close stats storage after initialization failed."); }
            Runtime = null;
            // Never print raw exception text: it can contain credentials or request details.
            Log.Error($"Global SCP Stats could not start ({error.GetType().Name}). Check server_id, HTTPS endpoint, token/storage key environment variables, and storage permissions.");
        }
        base.OnEnabled();
    }

    public override void OnDisabled()
    {
        events?.Unregister();
        actions?.Unregister();
        badges?.Stop();
        try { Runtime?.Dispose(); }
        catch { Log.Error("Could not save all stats during shutdown. Check the outbox and storage permissions."); }
        Runtime = null;
        events = null;
        actions = null;
        badges = null;
        Instance = null;
        base.OnDisabled();
    }
}

