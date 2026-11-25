using System;
using System.ComponentModel;
using Exiled.API.Interfaces;
using GlobalSCPStats.Core.Experience;
using System.Collections.Generic;
using GlobalSCPStats.Rewards;
using GlobalSCPStats.Core.Privacy;
using YamlDotNet.Serialization;

namespace GlobalSCPStats;

public sealed class Config : IConfig
{
    private const DefaultValuesHandling OptionalSetting = DefaultValuesHandling.OmitDefaults;
    private const string DefaultApiTokenVariable = "GSS_API_TOKEN";
    private const string DefaultStorageKeyVariable = "GSS_STORAGE_KEY";
    private const int DefaultCheckpointInterval = 30;
    private const int DefaultMaxQueuedBatches = 10000;
    private const int DefaultMaxQueueAge = 72;

    private string serverId = string.Empty;
    private string uploadEndpoint = string.Empty;
    private string profileEndpoint = string.Empty;
    private string dataDirectory = string.Empty;

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

    [DefaultValue(""), YamlMember(DefaultValuesHandling = OptionalSetting)]
    [Description("Manual mode only. Omit this for one-time setup using GSS_SETUP_CODE.")]
    public string ServerId
    {
        get => serverId;
        set => serverId = value ?? string.Empty;
    }

    [DefaultValue(""), YamlMember(DefaultValuesHandling = OptionalSetting)]
    [Description("Manual mode only: full HTTPS ingestion URL. Leave empty for local-only collection with a manual server ID.")]
    public string UploadEndpoint
    {
        get => uploadEndpoint;
        set => uploadEndpoint = value ?? string.Empty;
    }

    [DefaultValue(""), YamlMember(DefaultValuesHandling = OptionalSetting)]
    [Description("Manual mode only: optional HTTPS profile URL on the same host and port as uploads.")]
    public string ProfileEndpoint
    {
        get => profileEndpoint;
        set => profileEndpoint = value ?? string.Empty;
    }

    [DefaultValue(DefaultApiTokenVariable), YamlMember(DefaultValuesHandling = OptionalSetting)]
    [Description("Manual mode only: name of the environment variable holding this server's API token.")]
    public string ApiTokenEnvironmentVariable { get; set; } = DefaultApiTokenVariable;

    [DefaultValue(DefaultStorageKeyVariable), YamlMember(DefaultValuesHandling = OptionalSetting)]
    [Description("Environment variable containing a base64-encoded, random 64-byte storage key. Keep it separate from the outbox and API token.")]
    public string StorageKeyEnvironmentVariable { get; set; } = DefaultStorageKeyVariable;

    [DefaultValue(DefaultCheckpointInterval), YamlMember(DefaultValuesHandling = OptionalSetting)]
    [Description("How often in-memory stat deltas are saved to the encrypted outbox.")]
    public int CheckpointIntervalSeconds { get; set; } = DefaultCheckpointInterval;

    [DefaultValue(DefaultMaxQueuedBatches), YamlMember(DefaultValuesHandling = OptionalSetting)]
    [Description("Maximum queued batches. When full, new stats stay in memory until space is available.")]
    public int MaxQueuedBatches { get; set; } = DefaultMaxQueuedBatches;

    [DefaultValue(DefaultMaxQueueAge), YamlMember(DefaultValuesHandling = OptionalSetting)]
    [Description("Delete queued batches older than this many hours, even if the API is unavailable. Local outbox retention only.")]
    public int MaxQueueAgeHours { get; set; } = DefaultMaxQueueAge;

    [DefaultValue(""), YamlMember(DefaultValuesHandling = OptionalSetting)]
    [Description("Optional absolute storage directory; otherwise uses EXILED/configs/GlobalSCPStats/<port>.")]
    public string DataDirectory
    {
        get => dataDirectory;
        set => dataDirectory = value ?? string.Empty;
    }

    public void Validate()
    {
        Privacy.Validate();
        Experience.Validate();
        foreach (RewardBadge badge in RewardBadges) badge.Validate();
        if (ServerId.Length > 128)
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

