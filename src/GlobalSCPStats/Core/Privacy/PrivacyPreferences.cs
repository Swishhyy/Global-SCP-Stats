using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using GlobalSCPStats.Core.Models;
using GlobalSCPStats.Core.Storage;
using GlobalSCPStats.Core.Tracking;

namespace GlobalSCPStats.Core.Privacy;

public sealed class PrivacyPreferences
{
    private const string AutomaticCollection = "server-policy-v1";
    private readonly object gate = new();
    private readonly string path;
    private readonly EncryptedFileStore store;
    private readonly byte[] subjectKey;
    private readonly bool requireOptIn;
    private readonly string noticeVersion;
    private PreferenceState state;
    private bool dirty;
    private long revision;

    public PrivacyPreferences(string directory, byte[] storageKey, PrivacyOptions options)
    {
        options.Validate();
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "privacy.state");
        store = new EncryptedFileStore(storageKey);
        using var derive = new HMACSHA256(storageKey);
        subjectKey = derive.ComputeHash(Encoding.UTF8.GetBytes("GlobalSCPStats/privacy/subject/v1"));
        requireOptIn = options.RequirePlayerOptIn;
        noticeVersion = options.NoticeVersion;
        state = File.Exists(path) ? Deserialize(store.Read(path)) : new PreferenceState();
    }

    public long Revision { get { lock (gate) return revision; } }
    public bool HasUnsavedChanges { get { lock (gate) return dirty; } }
    public bool IsAllowed(string? id) => GetCollectionId(id) != null;

    public string? GetCollectionId(string? id)
    {
        if (!StatsTracker.IsSupportedUser(id)) return null;
        lock (gate)
        {
            if (!state.Preferences.TryGetValue(Subject(id!), out Preference? preference))
                return requireOptIn ? null : AutomaticCollection;
            if (preference.GameDoNotTrack || (preference.HasLocalPreference && !preference.Allowed)) return null;
            if (requireOptIn && (!preference.HasLocalPreference || preference.NoticeVersion != noticeVersion)) return null;
            return preference.CollectionId;
        }
    }

    public bool AllowsContribution(PlayerStats contribution, int schemaVersion)
    {
        string? current = GetCollectionId(contribution.UserId);
        if (current == null) return false;
        if (schemaVersion == 1 && string.IsNullOrEmpty(contribution.CollectionId)) return current == AutomaticCollection;
        return current == contribution.CollectionId;
    }

    public bool Allow(string id)
    {
        ValidateUser(id);
        lock (gate)
        {
            string subject = Subject(id);
            state.Preferences.TryGetValue(subject, out Preference? existing);
            if (existing?.GameDoNotTrack == true) return false;
            if (!requireOptIn && (existing == null || !existing.HasLocalPreference || existing.Allowed)) return false;
            if (existing != null && existing.HasLocalPreference && existing.Allowed && existing.NoticeVersion == noticeVersion)
                return false;
            var next = new PreferenceState { Preferences = new Dictionary<string, Preference>(state.Preferences) };
            next.Preferences[subject] = new Preference
            {
                Allowed = true, HasLocalPreference = true, NoticeVersion = noticeVersion, CollectionId = Guid.NewGuid().ToString("D"), ChangedAt = DateTimeOffset.UtcNow.ToString("O"),
            };
            // Do not begin collection unless the approval was saved successfully.
            Save(next);
            state = next;
            dirty = false;
            revision++;
            return true;
        }
    }

    public void Withdraw(string id)
    {
        ValidateUser(id);
        lock (gate)
        {
            string subject = Subject(id);
            state.Preferences.TryGetValue(subject, out Preference? existing);
            if (existing != null && existing.HasLocalPreference && !existing.Allowed && !dirty) return;
            state.Preferences[subject] = new Preference
            {
                Allowed = false, HasLocalPreference = true, GameDoNotTrack = existing?.GameDoNotTrack ?? false,
                NoticeVersion = noticeVersion, CollectionId = string.Empty, ChangedAt = DateTimeOffset.UtcNow.ToString("O"),
            };
            // Block immediately in memory even if the following disk write fails.
            dirty = true;
            revision++;
            Save(state);
            dirty = false;
        }
    }

    public bool ObserveDoNotTrack(string id, bool enabled)
    {
        ValidateUser(id);
        lock (gate)
        {
            string subject = Subject(id);
            state.Preferences.TryGetValue(subject, out Preference? existing);
            if ((existing?.GameDoNotTrack ?? false) == enabled) return false;
            var next = new PreferenceState { Preferences = new Dictionary<string, Preference>(state.Preferences) };
            next.Preferences[subject] = new Preference
            {
                Allowed = existing?.Allowed ?? false,
                HasLocalPreference = existing?.HasLocalPreference ?? false,
                GameDoNotTrack = enabled,
                NoticeVersion = existing?.NoticeVersion ?? string.Empty,
                CollectionId = enabled ? string.Empty : Guid.NewGuid().ToString("D"),
                ChangedAt = DateTimeOffset.UtcNow.ToString("O"),
            };
            if (enabled)
            {
                // A DNT request blocks immediately, even when storage is unavailable.
                state = next;
                dirty = true;
                revision++;
                Save(state);
            }
            else
            {
                // Never resume collection until the updated game preference is durable.
                Save(next);
                state = next;
                revision++;
            }
            dirty = false;
            return true;
        }
    }

    public bool IsDoNotTrackEnabled(string? id)
    {
        if (!StatsTracker.IsSupportedUser(id)) return false;
        lock (gate) return state.Preferences.TryGetValue(Subject(id!), out Preference? value) && value.GameDoNotTrack;
    }

    public void Flush()
    {
        lock (gate)
        {
            if (!dirty) return;
            Save(state);
            dirty = false;
        }
    }

    private string Subject(string id)
    {
        using var hmac = new HMACSHA256(subjectKey);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(id)));
    }

    private void Save(PreferenceState value)
    {
        var writer = new JsonWriter().BeginObject()
            .Property("Version", value.Version)
            .Name("Preferences").BeginObject();
        foreach (var entry in value.Preferences)
        {
            Preference p = entry.Value;
            writer.Name(entry.Key).BeginObject()
                .Property("Allowed", p.Allowed)
                .Property("ChangedAt", p.ChangedAt)
                .Property("CollectionId", p.CollectionId)
                .Property("GameDoNotTrack", p.GameDoNotTrack)
                .Property("HasLocalPreference", p.HasLocalPreference)
                .Property("NoticeVersion", p.NoticeVersion)
                .EndObject();
        }
        store.Replace(path, writer.EndObject().EndObject().ToBytes());
    }

    private static PreferenceState Deserialize(byte[] bytes)
    {
        Dictionary<string, object?> root = Json.ParseObject(bytes);
        Dictionary<string, object?>? saved = Json.Object(root, "Preferences");
        if (saved == null) throw new SerializationException("Invalid privacy preferences.");
        var value = new PreferenceState { Version = Json.Int(root, "Version"), Preferences = [] };
        foreach (var entry in saved)
        {
            if (entry.Value is not Dictionary<string, object?> item) throw new SerializationException("Invalid saved collection preference.");
            value.Preferences[entry.Key] = new Preference
            {
                Allowed = Json.Bool(item, "Allowed"),
                HasLocalPreference = Json.Bool(item, "HasLocalPreference"),
                GameDoNotTrack = Json.Bool(item, "GameDoNotTrack"),
                CollectionId = Json.String(item, "CollectionId") ?? string.Empty,
                NoticeVersion = Json.String(item, "NoticeVersion") ?? string.Empty,
                ChangedAt = Json.String(item, "ChangedAt") ?? string.Empty,
            };
        }
        if ( (value.Version != 1 && value.Version != 2)) throw new SerializationException("Invalid privacy preferences.");
        foreach (Preference? preference in value.Preferences.Values)
        {
            if (preference == null) throw new SerializationException("Invalid saved collection preference.");
            if (value.Version == 1) preference.HasLocalPreference = true;
            if (!preference.GameDoNotTrack && (preference.Allowed || !preference.HasLocalPreference) && !Guid.TryParse(preference.CollectionId, out _))
                throw new SerializationException("Invalid saved collection approval.");
        }
        value.Version = 2;
        return value;
    }

    private static void ValidateUser(string id)
    {
        if (!StatsTracker.IsSupportedUser(id)) throw new ArgumentException("A supported player account is required.", nameof(id));
    }

    private sealed class PreferenceState
    {
        public int Version { get; set; } = 2;
        public Dictionary<string, Preference> Preferences { get; set; } = [];
    }

    private sealed class Preference
    {
        public bool Allowed { get; set; }
        public bool HasLocalPreference { get; set; }
        public bool GameDoNotTrack { get; set; }
        public string CollectionId { get; set; } = string.Empty;
        public string NoticeVersion { get; set; } = string.Empty;
        public string ChangedAt { get; set; } = string.Empty;
    }
}
