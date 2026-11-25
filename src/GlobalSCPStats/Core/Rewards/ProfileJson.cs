using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GlobalSCPStats.Core.Rewards;

public static class ProfileJson
{
    public static byte[] LookupRequest(string userId)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(ProfileLookup)).WriteObject(stream, new ProfileLookup { UserId = userId });
        return stream.ToArray();
    }

    public static ProgressProfile Deserialize(byte[] bytes, string expectedUserId)
    {
        using var stream = new MemoryStream(bytes);
        var profile = (ProgressProfile?)new DataContractJsonSerializer(typeof(ProgressProfile), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream);
        if (profile == null || profile.UserId != expectedUserId || profile.TotalXp < 0 || profile.Level < 1 ||
            profile.Revision < 0)
            throw new SerializationException("Invalid progress profile.");
        profile.RewardIds ??= new List<string>();
        if (profile.RewardIds.Count > 128) throw new SerializationException("Invalid progress profile.");
        if (profile.ServerStats != null)
        {
            var stats = profile.ServerStats;
            if (stats.Kills < 0 || stats.Deaths < 0 || stats.Escapes < 0 || stats.RoundsPlayed < 0 || stats.XpEarned < 0 ||
                double.IsNaN(stats.PlaytimeSeconds) || double.IsInfinity(stats.PlaytimeSeconds) || stats.PlaytimeSeconds < 0 || stats.Actions == null || stats.Actions.Count > 4096)
                throw new SerializationException("Invalid server stats.");
            foreach (var action in stats.Actions) if (action.Value < 0) throw new SerializationException("Invalid server action.");
        }
        foreach (string? reward in profile.RewardIds)
        {
            if (string.IsNullOrWhiteSpace(reward) || reward!.Length > 96)
                throw new SerializationException("Invalid reward ID.");
            foreach (char character in reward)
                if (!(char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.'))
                    throw new SerializationException("Invalid reward ID.");
        }
        return profile;
    }

    [DataContract]
    private sealed class ProfileLookup
    {
        [DataMember(Name = "user_id")]
        public string UserId { get; set; } = string.Empty;
    }
}
