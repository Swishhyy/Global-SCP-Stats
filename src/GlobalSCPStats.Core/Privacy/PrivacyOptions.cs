using System;

namespace GlobalSCPStats.Core.Privacy;

public sealed class PrivacyOptions
{
    public bool RequirePlayerOptIn { get; set; }
    public string NoticeUrl { get; set; } = string.Empty;
    public string NoticeVersion { get; set; } = "1";
    public bool ShowJoinReminder { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(NoticeVersion) || NoticeVersion.Length > 64)
            throw new ArgumentException("A privacy notice version of 1 to 64 characters is required.");
        if (!string.IsNullOrWhiteSpace(NoticeUrl) &&
            (!Uri.TryCreate(NoticeUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new ArgumentException("The privacy notice URL must use HTTPS without embedded credentials.");
    }
}
