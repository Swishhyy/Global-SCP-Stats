using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
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
        using var stream = new MemoryStream();
        Serializer().WriteObject(stream, value);
        store.Replace(path, stream.ToArray());
    }

    private static PreferenceState Deserialize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var value = (PreferenceState?)Serializer().ReadObject(stream);
        if (value?.Preferences == null || (value.Version != 1 && value.Version != 2)) throw new SerializationException("Invalid privacy preferences.");
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

    private static DataContractJsonSerializer Serializer() => new(typeof(PreferenceState),
        new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

    private static void ValidateUser(string id)
    {
        if (!StatsTracker.IsSupportedUser(id)) throw new ArgumentException("A supported player account is required.", nameof(id));
    }

    [DataContract]
    private sealed class PreferenceState
    {
        [DataMember] public int Version { get; set; } = 2;
        [DataMember] public Dictionary<string, Preference> Preferences { get; set; } = [];
    }

    [DataContract]
    private sealed class Preference
    {
        [DataMember] public bool Allowed { get; set; }
        [DataMember] public bool HasLocalPreference { get; set; }
        [DataMember] public bool GameDoNotTrack { get; set; }
        [DataMember] public string CollectionId { get; set; } = string.Empty;
        [DataMember] public string NoticeVersion { get; set; } = string.Empty;
        [DataMember] public string ChangedAt { get; set; } = string.Empty;
    }
}
