using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using GlobalSCPStats.Core.Storage;

namespace GlobalSCPStats.Core.Rewards;

public static class ProfileJson
{
    public static byte[] LookupRequest(string userId)
        => new JsonWriter().BeginObject().Property("user_id", userId).EndObject().ToBytes();

    public static ProgressProfile Deserialize(byte[] bytes, string expectedUserId)
    {
        ProgressProfile profile = Read(Json.ParseObject(bytes));
        if (profile.UserId != expectedUserId || profile.TotalXp < 0 || profile.Level < 1 ||
            profile.Revision < 0)
            throw new SerializationException("Invalid progress profile.");
        profile.RewardIds ??= [];
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

    private static ProgressProfile Read(Dictionary<string, object?> root)
    {
        var profile = new ProgressProfile
        {
            UserId = Json.String(root, "user_id") ?? string.Empty,
            TotalXp = Json.Long(root, "total_xp"),
            Level = Json.Int(root, "level"),
            Revision = Json.Long(root, "revision"),
            RewardIds = [],
        };
        foreach (object? reward in Json.Array(root, "reward_ids") ?? [])
            profile.RewardIds.Add(reward as string ?? throw new SerializationException("Invalid reward ID."));
        Dictionary<string, object?>? stats = Json.Object(root, "server_stats");
        if (stats != null)
        {
            if (Json.Object(stats, "actions") == null) throw new SerializationException("Invalid server stats.");
            profile.ServerStats = BatchJson.ReadPlayer(stats);
        }
        return profile;
    }
}
