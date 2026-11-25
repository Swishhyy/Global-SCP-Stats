using System;
using System.ComponentModel;
using Exiled.API.Interfaces;
using GlobalSCPStats.Core.Experience;
using System.Collections.Generic;
using GlobalSCPStats.Rewards;
using GlobalSCPStats.Core.Privacy;

namespace GlobalSCPStats;

public sealed class Config : IConfig
{
    public bool IsEnabled { get; set; } = true;
    public bool Debug { get; set; }

    [Description("Enable this server's stats collection. Game Do Not Track and local opt-outs always take priority.")]
    public bool CollectionEnabled { get; set; }

    [Description("Collection follows the game's DNT flag by default. DNT always overrides local approval and server settings.")]
    public PrivacyOptions Privacy { get; set; } = new PrivacyOptions();

    [Description("XP is independent of action statistics. Set enabled: false here to keep tracking without granting XP.")]
    public ExperienceOptions Experience { get; set; } = new ExperienceOptions();

    [Description("Evaluate this server's configured badge rules using its lifetime stats. Rewards are never defined by the backend.")]
    public bool BadgeRewardsEnabled { get; set; } = true;

    [Description("Keep existing staff, supporter, and other badges instead of replacing them with reward badges.")]
    public bool PreserveExistingBadges { get; set; } = true;

    public List<RewardBadge> RewardBadges { get; set; } = new List<RewardBadge> { new RewardBadge() };

    [Description("Leave empty for one-time setup using GSS_SETUP_CODE. Set an ID for an existing or manually configured server.")]
    public string ServerId { get; set; } = string.Empty;

    [Description("Manual mode only: full HTTPS ingestion URL. Leave empty for local-only collection with a manual server ID.")]
    public string UploadEndpoint { get; set; } = string.Empty;

    [Description("Manual mode only: optional HTTPS profile URL on the same host and port as uploads.")]
    public string ProfileEndpoint { get; set; } = string.Empty;

    [Description("Manual mode only: name of the environment variable holding this server's API token.")]
    public string ApiTokenEnvironmentVariable { get; set; } = "GSS_API_TOKEN";

    [Description("Environment variable containing a base64-encoded, random 64-byte storage key. Keep it separate from the outbox and API token.")]
    public string StorageKeyEnvironmentVariable { get; set; } = "GSS_STORAGE_KEY";

    [Description("How often in-memory stat deltas are saved to the encrypted outbox.")]
    public int CheckpointIntervalSeconds { get; set; } = 30;

    [Description("Maximum queued batches. When full, new stats stay in memory until space is available.")]
    public int MaxQueuedBatches { get; set; } = 10000;

    [Description("Delete queued batches older than this many hours, even if the API is unavailable. Local outbox retention only.")]
    public int MaxQueueAgeHours { get; set; } = 72;

    [Description("Optional absolute storage directory; otherwise uses EXILED/configs/GlobalSCPStats/<port>.")]
    public string DataDirectory { get; set; } = string.Empty;

    public void Validate()
    {
        Privacy.Validate();
        Experience.Validate();
        foreach (RewardBadge badge in RewardBadges) badge.Validate();
        if (ServerId?.Length > 128)
            throw new ArgumentException("server_id must contain at most 128 characters.");
        if (CheckpointIntervalSeconds < 5 || CheckpointIntervalSeconds > 3600)
            throw new ArgumentException("checkpoint_interval_seconds must be between 5 and 3600.");
        if (MaxQueuedBatches < 1 || MaxQueuedBatches > 100000)
            throw new ArgumentException("max_queued_batches must be between 1 and 100000.");
        if (MaxQueueAgeHours < 1 || MaxQueueAgeHours > 720)
            throw new ArgumentException("max_queue_age_hours must be between 1 and 720.");
        if (!string.IsNullOrWhiteSpace(DataDirectory) && !System.IO.Path.IsPathRooted(DataDirectory))
            throw new ArgumentException("data_directory must be an absolute path.");
    }
}

