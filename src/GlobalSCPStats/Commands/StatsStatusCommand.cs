using System;
using CommandSystem;
using Exiled.Permissions.Extensions;

namespace GlobalSCPStats.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class StatsStatusCommand : ICommand
{
    public string Command => "gstats";
    public string[] Aliases => new[] { "globalstats" };
    public string Description => "Show connector status. Requires globalstats.admin.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission("globalstats.admin"))
        {
            response = "Missing permission: globalstats.admin";
            return false;
        }
        var runtime = Plugin.Instance?.Runtime;
        if (runtime == null)
        {
            response = "Global SCP Stats is not running. Check the server log and configuration.";
            return false;
        }
        response = $"Global SCP Stats {Plugin.Instance!.Version} | Players tracked: {runtime.OnlinePlayers} | Queued batches: {runtime.QueuedBatches} | Uploads: {runtime.UploadStatus} | Privacy: {runtime.PrivacyStatus}";
        return true;
    }
}
