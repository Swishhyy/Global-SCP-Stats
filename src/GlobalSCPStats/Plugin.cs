using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Exiled.API.Features;
using GlobalSCPStats.Core;
using GlobalSCPStats.Core.Models;
using GlobalSCPStats.Core.Storage;
using GlobalSCPStats.Core.Transport;
using GlobalSCPStats.Events;
using GlobalSCPStats.Rewards;
using GlobalSCPStats.Core.Privacy;
using MEC;

namespace GlobalSCPStats;

public sealed class Plugin : Plugin<Config>
{
    public override string Name => "Global SCP Stats";
    public override string Prefix => "global_scp_stats";
    public override string Author => "Swishhyy";
    public override Version Version => typeof(Plugin).Assembly.GetName().Version!;
    public override Version RequiredExiledVersion => new(9, 15, 2);
    public static Plugin? Instance { get; private set; }
    public StatsRuntime? Runtime { get; private set; }
    internal string StartupStatus => startupStatus;
    internal string? ActiveServerId { get; private set; }
    private StatsEvents? events;
    private ActionEvents? actions;
    private BadgeRewards? badges;
    private CoroutineHandle? enrollmentLoop;
    private CancellationTokenSource? enrollmentCancellation;
    private UnityHttpTransport? http;
    private volatile string startupStatus = "The connector is not running.";
    private string startupStep = "validating settings";

    public override void OnEnabled()
    {
        Instance = this;
        try { ConfigureConnector(); }
        catch (Exception error) { ReportStartupError(error); }
        base.OnEnabled();
    }

    private void ConfigureConnector()
    {
        Config.Validate();
        string? encodedKey = Environment.GetEnvironmentVariable(Config.StorageKeyEnvironmentVariable);
        string directory = string.IsNullOrWhiteSpace(Config.DataDirectory)
            ? Path.Combine(Paths.Configs, "GlobalSCPStats", Server.Port.ToString())
            : Config.DataDirectory;
        startupStep = "opening encrypted storage";
        byte[] storageKey = StorageKey.LoadOrCreate(directory, encodedKey);

        if (string.IsNullOrWhiteSpace(Config.ServerId))
        {
            startupStatus = "Loading saved credentials or connecting to the stats API.";
            startupStep = "registering with the stats API";
            var enrollment = new ConnectorEnrollment(directory, storageKey);
            http = new UnityHttpTransport();
            var handler = http.CreateHandler();
            var cancellation = new CancellationTokenSource();
            enrollmentCancellation = cancellation;
            string? code = Environment.GetEnvironmentVariable(ConnectorEnrollment.SetupEnvironmentVariable);
            string name = Server.Name;
            CancellationToken token = cancellation.Token;
            Task<UploadOptions> task = Task.Run(() => enrollment.GetCredentialsAsync(
                code, name, handler, token, message => startupStatus = message));
            enrollmentLoop = Timing.RunCoroutine(FinishEnrollment(task, directory, storageKey));
            return;
        }

        UploadOptions? upload = null;
        if (!string.IsNullOrWhiteSpace(Config.UploadEndpoint))
        {
            string? token = Environment.GetEnvironmentVariable(Config.ApiTokenEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(token))
                Log.Warn("API token environment variable is unset. Collecting locally; uploads are disabled.");
            else
                upload = new UploadOptions(Config.UploadEndpoint, Config.ServerId, token!, Config.ProfileEndpoint);
        }
        else Log.Info("No upload endpoint configured. Stats will be saved locally.");
        StartRuntime(directory, storageKey, Config.ServerId, upload);
    }

    private IEnumerator<float> FinishEnrollment(Task<UploadOptions> task, string directory, byte[] key)
    {
        while (!task.IsCompleted) yield return Timing.WaitForSeconds(0.1f);
        try
        {
            UploadOptions upload = task.GetAwaiter().GetResult();
            StartRuntime(directory, key, upload.ServerId, upload);
        }
        catch (Exception error) { ReportStartupError(error); }
    }

    private void StartRuntime(string directory, byte[] storageKey, string serverId, UploadOptions? upload)
    {
        startupStep = "starting the stats service";
        StatsApiClient? api = upload == null ? null : new StatsApiClient(upload, (http ??= new UnityHttpTransport()).CreateHandler());
        try
        {
            var outbox = new DurableOutbox(directory, Config.MaxQueuedBatches, storageKey, TimeSpan.FromHours(Config.MaxQueueAgeHours));
            var privacy = new PrivacyPreferences(directory, storageKey, Config.Privacy);
            // EXILED build. A LabAPI build would pass PluginFramework.LabApi and the LabAPI loader version here.
            var server = ServerInfo.Create(Server.Name, Version, PluginFramework.Exiled, typeof(Server).Assembly.GetName().Version);
            Runtime = new StatsRuntime(serverId, outbox, privacy, TimeSpan.FromSeconds(Config.CheckpointIntervalSeconds), api, message => Log.Warn(message), Config.Experience, server);
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
            ActiveServerId = serverId;
            Log.Info($"Global SCP Stats {Version} is tracking as {serverId}. Use gstats in Remote Admin for status.");
        }
        catch
        {
            events?.Unregister();
            actions?.Unregister();
            badges?.Stop();
            events = null;
            try { if (Runtime != null) Runtime.Dispose(); else api?.Dispose(); }
            catch { Log.Error("Could not close stats storage after initialization failed."); }
            Runtime = null;
            throw;
        }
    }

    private void ReportStartupError(Exception error)
    {
        // Only enrollment exceptions carry messages that are safe for the server log.
        Exception cause = error.GetBaseException();
        startupStatus = error is EnrollmentException
            ? error.Message
            : $"Could not start while {startupStep} ({cause.GetType().Name}).";
        var method = new StackTrace(cause, false).GetFrame(0)?.GetMethod();
        Log.Error($"Global SCP Stats: {StartupStatus} [{method?.DeclaringType?.FullName}.{method?.Name}]");
        http?.Dispose();
        http = null;
    }

    public override void OnDisabled()
    {
        enrollmentCancellation?.Cancel();
        if (enrollmentLoop.HasValue) Timing.KillCoroutines(enrollmentLoop.Value);
        enrollmentCancellation?.Dispose();
        enrollmentCancellation = null;
        enrollmentLoop = null;
        events?.Unregister();
        actions?.Unregister();
        badges?.Stop();
        try { Runtime?.Dispose(); }
        catch { Log.Error("Could not save all stats during shutdown. Check the outbox and storage permissions."); }
        http?.Dispose();
        http = null;
        Runtime = null;
        events = null;
        actions = null;
        badges = null;
        ActiveServerId = null;
        Instance = null;
        base.OnDisabled();
    }
}


