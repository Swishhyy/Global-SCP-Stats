using System;
using CommandSystem;
using Exiled.API.Features;
using GlobalSCPStats.Events;

namespace GlobalSCPStats.Commands;

[CommandHandler(typeof(ClientCommandHandler))]
public sealed class StatsPrivacyCommand : ICommand
{
    public string Command => "statsprivacy";
    public string[] Aliases => new[] { "gprivacy" };
    public string Description => "View the stats privacy notice, allow collection, or stop collection and remove pending local data.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        Player? player = Player.Get(sender);
        Plugin? plugin = Plugin.Instance;
        var runtime = plugin?.Runtime;
        if (!StatsEvents.IsEligible(player) || runtime == null || plugin == null)
        {
            response = "Stats collection is unavailable on this server or for this connection.";
            return false;
        }
        if (arguments.Count > 1)
        {
            response = "Usage: .statsprivacy [info|status|allow|off]";
            return false;
        }
        StatsEvents.SynchronizePrivacy(runtime, player);
        string action = arguments.Count == 0 ? "info" : arguments.Array![arguments.Offset].ToLowerInvariant();
        string notice = string.IsNullOrWhiteSpace(plugin.Config.Privacy.NoticeUrl)
            ? "The server has not configured its privacy notice URL."
            : "Privacy notice: " + plugin.Config.Privacy.NoticeUrl;

        switch (action)
        {
            case "info":
            case "status":
                response = $"Collection on this server: {(runtime.CollectionAllowed(player!.UserId) ? "ON" : "OFF")}.\n" +
                    $"In-game Do Not Track: {(player.DoNotTrack ? "ON — collection blocked" : "OFF")}.\n" +
                    "Collected data: platform account ID, gameplay counters, playtime, and optional XP.\n" + notice + "\n" +
                    "Collection follows the in-game DNT flag by default. Use .statsprivacy off for an additional local opt-out; .statsprivacy allow removes that local opt-out when DNT is off. " +
                    "Hosted history and other servers require their own privacy controls.";
                return true;
            case "allow":
                if (player!.DoNotTrack)
                {
                    response = "Collection is blocked by your in-game Do Not Track setting. This command cannot override it. Update that setting in the game; reconnect if required for the game to apply it.";
                    return false;
                }
                if (plugin.Config.Privacy.RequirePlayerOptIn && string.IsNullOrWhiteSpace(plugin.Config.Privacy.NoticeUrl))
                {
                    response = "The server must configure a privacy notice before accepting collection approval. Your preference has not changed.";
                    return false;
                }
                if (!runtime.AllowCollection(player!.UserId, player.IsAlive))
                {
                    response = "Your collection approval could not be saved. Contact a server admin; no new approval was applied.";
                    return false;
                }
                response = "Collection is enabled on this server. Use .statsprivacy off whenever you want to stop it.\n" + notice;
                return true;
            case "off":
                if (!runtime.WithdrawCollection(player!.UserId))
                {
                    response = "Tracking has stopped, but your preference could not be saved. Uploads are paused while storage recovers. Contact a server admin before reconnecting or restarting the server.";
                    return false;
                }
                response = "Collection has stopped on this server. Your in-memory stats were cleared and queued local contributions are scheduled for removal. " +
                    "An upload already in progress may still arrive. For previously uploaded history or other servers, use the account privacy process in the notice.\n" + notice;
                return true;
            default:
                response = "Usage: .statsprivacy [info|status|allow|off]";
                return false;
        }
    }
}
