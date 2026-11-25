using System;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace GlobalSCPStats.Core.Models;

/// <summary>Which plugin framework loaded Global SCP Stats. Sent to the site as a lowercase string.</summary>
public enum PluginFramework
{
    Exiled,
    // LabApi: add a LabAPI entry point that builds ServerInfo with PluginFramework.LabApi and the LabAPI version.
    LabApi,
}

/// <summary>Server details sent with each batch so the site can show a clean name and the versions in use.</summary>
[DataContract]
public sealed class ServerInfo
{
    // EXILED appends an invisible tag such as <color=#00000000><size=1>Exiled 9.15.3</size></color> to the server name.
    private static readonly Regex HiddenText = new(
        @"<size=0*[01](?:px|%)?>.*?</size>|<color=#[0-9a-f]{6}00>.*?</color>|<alpha=#00>.*",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex Markup = new(@"<[^<>]*>|[\p{Cc}\p{Cf}]");

    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "plugin_version", Order = 2)]
    public string PluginVersion { get; set; } = string.Empty;

    [DataMember(Name = "framework", Order = 3)]
    public string Framework { get; set; } = "exiled";

    [DataMember(Name = "framework_version", Order = 4)]
    public string FrameworkVersion { get; set; } = string.Empty;

    public static ServerInfo Create(string? serverName, Version pluginVersion, PluginFramework framework, Version frameworkVersion) => new()
    {
        Name = CleanName(serverName),
        PluginVersion = pluginVersion.ToString(3),
        Framework = framework == PluginFramework.LabApi ? "labapi" : "exiled",
        FrameworkVersion = frameworkVersion.ToString(3),
    };

    /// <summary>Removes hidden text, rich-text tags and control characters, and caps the name at 80 characters.</summary>
    public static string CleanName(string? value)
    {
        string name = Markup.Replace(HiddenText.Replace(value ?? string.Empty, string.Empty), string.Empty).Trim();
        if (name.Length > 80)
            name = name.Substring(0, char.IsHighSurrogate(name[79]) ? 79 : 80).TrimEnd();
        return name.Length == 0 ? "SCP:SL server" : name;
    }
}
