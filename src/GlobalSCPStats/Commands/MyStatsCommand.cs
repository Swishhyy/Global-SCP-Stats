using System;
using CommandSystem;
using Exiled.API.Features;
using System.Linq;

namespace GlobalSCPStats.Commands;

[CommandHandler(typeof(ClientCommandHandler))]
public sealed class MyStatsCommand : ICommand
{
    public string Command => "mystats";
    public string[] Aliases => [];
    public string Description => "Show your stats for this connection.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        Player? player = Player.Get(sender);
        var runtime = Plugin.Instance?.Runtime;
        if (runtime != null) Events.StatsEvents.SynchronizePrivacy(runtime, player);
        var stats = runtime?.GetSession(player?.UserId);
        if (stats == null)
        {
            response = "Stats are unavailable for this connection. Use .statsprivacy info to check collection settings.";
            return false;
        }
        if (arguments.Count > 0 && arguments.Array![arguments.Offset].Equals("actions", StringComparison.OrdinalIgnoreCase))
        {
            response = stats.Actions.Count == 0 ? "No actions recorded this connection." :
                string.Join("\n", stats.Actions.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
            return true;
        }
        TimeSpan playtime = TimeSpan.FromSeconds(stats.PlaytimeSeconds);
        response = $"This connection: {(long)playtime.TotalHours}h {playtime.Minutes}m | Rounds: {stats.RoundsPlayed} | Kills: {stats.Kills} | Deaths: {stats.Deaths} | Escapes: {stats.Escapes} | XP: {stats.XpEarned} | Action types: {stats.Actions.Count}";
        var progress = Plugin.Instance?.Runtime?.GetProgress(player?.UserId);
        if (progress != null) response += $"\nGlobal progress: level {progress.Level} | XP: {progress.TotalXp} | Rewards: {progress.RewardIds.Count}";
        return true;
    }
}
