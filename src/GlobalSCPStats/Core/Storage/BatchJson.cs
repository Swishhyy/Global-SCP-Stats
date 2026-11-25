using System.Collections.Generic;
using System.Runtime.Serialization;
using GlobalSCPStats.Core.Models;

namespace GlobalSCPStats.Core.Storage;

public static class BatchJson
{
    public static byte[] Serialize(StatsBatch batch)
    {
        var writer = new JsonWriter().BeginObject()
            .Property("schema_version", batch.SchemaVersion)
            .Property("batch_id", batch.BatchId)
            .Property("server_id", batch.ServerId)
            .Property("created_at", batch.CreatedAt)
            .Name("players").BeginArray();
        foreach (PlayerStats player in batch.Players) WritePlayer(writer, player);
        writer.EndArray();
        if (batch.Server is ServerInfo server)
            writer.Name("server").BeginObject()
                .Property("name", server.Name)
                .Property("plugin_version", server.PluginVersion)
                .Property("framework", server.Framework)
                .Property("framework_version", server.FrameworkVersion)
                .EndObject();
        return writer.EndObject().ToBytes();
    }

    public static StatsBatch Deserialize(byte[] bytes)
    {
        Dictionary<string, object?> root = Json.ParseObject(bytes);
        var batch = new StatsBatch
        {
            SchemaVersion = Json.Int(root, "schema_version"),
            BatchId = Json.String(root, "batch_id") ?? string.Empty,
            ServerId = Json.String(root, "server_id") ?? string.Empty,
            CreatedAt = Json.String(root, "created_at") ?? string.Empty,
            Players = [],
        };
        foreach (object? item in Json.Array(root, "players") ?? [])
            batch.Players.Add(ReadPlayer(item as Dictionary<string, object?> ?? throw new SerializationException("Invalid player entry.")));
        if (Json.Object(root, "server") is { } server)
            batch.Server = new ServerInfo
            {
                Name = Json.String(server, "name") ?? string.Empty,
                PluginVersion = Json.String(server, "plugin_version") ?? string.Empty,
                Framework = Json.String(server, "framework") ?? "exiled",
                FrameworkVersion = Json.String(server, "framework_version") ?? string.Empty,
            };
        return batch;
    }

    internal static void WritePlayer(JsonWriter writer, PlayerStats player)
    {
        writer.BeginObject()
            .Property("user_id", player.UserId)
            .Property("playtime_seconds", player.PlaytimeSeconds)
            .Property("rounds_played", player.RoundsPlayed)
            .Property("kills", player.Kills)
            .Property("deaths", player.Deaths)
            .Property("escapes", player.Escapes)
            .Name("actions").BeginObject();
        foreach (var action in player.Actions) writer.Property(action.Key, action.Value);
        writer.EndObject()
            .Property("xp_earned", player.XpEarned)
            .Property("damage_dealt", player.DamageDealt)
            .Property("damage_received", player.DamageReceived)
            .Property("health_restored", player.HealthRestored)
            .Property("contribution_id", player.ContributionId)
            .Property("collection_id", player.CollectionId);
        if (player.Name != null) writer.Property("name", player.Name);
        if (player.LongestSessionSeconds > 0) writer.Property("longest_session_seconds", player.LongestSessionSeconds);
        writer.EndObject();
    }

    internal static PlayerStats ReadPlayer(Dictionary<string, object?> obj) => new()
    {
        UserId = Json.String(obj, "user_id") ?? string.Empty,
        PlaytimeSeconds = Json.Double(obj, "playtime_seconds"),
        RoundsPlayed = Json.Long(obj, "rounds_played"),
        Kills = Json.Long(obj, "kills"),
        Deaths = Json.Long(obj, "deaths"),
        Escapes = Json.Long(obj, "escapes"),
        Actions = Json.LongMap(obj, "actions"),
        XpEarned = Json.Long(obj, "xp_earned"),
        DamageDealt = Json.Double(obj, "damage_dealt"),
        DamageReceived = Json.Double(obj, "damage_received"),
        HealthRestored = Json.Double(obj, "health_restored"),
        ContributionId = Json.String(obj, "contribution_id") ?? string.Empty,
        CollectionId = Json.String(obj, "collection_id") ?? string.Empty,
        Name = Json.String(obj, "name"),
        LongestSessionSeconds = Json.Double(obj, "longest_session_seconds"),
    };
}
