using System;

namespace GlobalSCPStats.Core.Transport;

public sealed class UploadOptions
{
    public UploadOptions(string endpoint, string serverId, string apiToken, string? profileEndpoint = null)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Upload endpoint must be an HTTPS URL without credentials, a query, or a fragment.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(serverId) || serverId.Length > 128)
            throw new ArgumentException("A server ID of 1 to 128 characters is required.", nameof(serverId));
        if (string.IsNullOrWhiteSpace(apiToken) || apiToken.Length > 4096 || Array.Exists(apiToken.ToCharArray(), c => c <= 32 || c >= 127))
            throw new ArgumentException("The server token must contain printable ASCII characters without whitespace.", nameof(apiToken));

        Endpoint = uri;
        ServerId = serverId;
        ApiToken = apiToken;
        if (!string.IsNullOrWhiteSpace(profileEndpoint))
        {
            if (!Uri.TryCreate(profileEndpoint, UriKind.Absolute, out Uri? profileUri) || profileUri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(profileUri.UserInfo) || !string.IsNullOrEmpty(profileUri.Query) || !string.IsNullOrEmpty(profileUri.Fragment) ||
                profileUri.Host != uri.Host || profileUri.Port != uri.Port)
                throw new ArgumentException("The profile endpoint must be HTTPS on the same host and port as the upload endpoint, without credentials, a query, or a fragment.");
            ProfileEndpoint = profileUri;
        }
    }

    public Uri Endpoint { get; }
    public string ServerId { get; }
    internal string ApiToken { get; }
    public Uri? ProfileEndpoint { get; }
}
