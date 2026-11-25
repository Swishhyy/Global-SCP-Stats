using System;
using GlobalSCPStats.Core.Models;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GlobalSCPStats.Core.Storage;

namespace GlobalSCPStats.Core.Transport;

public sealed class ConnectorEnrollment(string directory, byte[] storageKey)
{
    public const string SetupEnvironmentVariable = "GSS_SETUP_CODE";
    public const string UploadEndpoint = "https://api.globalscpstats.com/v1/stats/batches";
    public const string ProfileEndpoint = "https://api.globalscpstats.com/v1/profiles/lookup";
    private const string RedeemEndpoint = "https://api.globalscpstats.com/v1/enrollment/redeem";
    private const string AutomaticEndpoint = "https://api.globalscpstats.com/v1/enrollment/automatic";
    private const string StateFileName = "enrollment.state";

    private readonly string path = Path.Combine(directory, StateFileName);
    private readonly EncryptedFileStore store = new(storageKey);

    public Task<UploadOptions> GetCredentialsAsync(string? setupCode, string serverName, CancellationToken cancellationToken)
        => GetCredentialsAsync(setupCode, serverName, new HttpClientHandler { AllowAutoRedirect = false }, cancellationToken);

    public async Task<UploadOptions> GetCredentialsAsync(
        string? setupCode, string serverName, HttpMessageHandler handler, CancellationToken cancellationToken,
        Action<string>? reportRetry = null)
    {
        using var client = new HttpClient(handler);
        int retrySeconds = 5;
        while (true)
        {
            try { return await GetCredentialsAsync(setupCode, client, cancellationToken, serverName).ConfigureAwait(false); }
            catch (EnrollmentException error) when (error.Retryable)
            {
                reportRetry?.Invoke($"{error.Message} Retrying in {retrySeconds} seconds.");
                await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cancellationToken).ConfigureAwait(false);
                retrySeconds = Math.Min(retrySeconds * 2, 300);
            }
        }
    }

    internal async Task<UploadOptions> GetCredentialsAsync(
        string? setupCode, HttpClient client, CancellationToken cancellationToken, string? serverName = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool exists = File.Exists(path);
        State? state = exists ? DecodeState(store.Read(path)) : null;
        if (state != null && !Regex.IsMatch(state.Token, @"\Agss_[a-zA-Z0-9_-]{43}\z"))
            throw new SerializationException("The saved connector token is invalid.");
        if (state?.ServerId != null)
            return Options(state);

        if (string.IsNullOrWhiteSpace(setupCode)) setupCode = null;
        if (setupCode != null && !Regex.IsMatch(setupCode, @"\Aenroll_[a-zA-Z0-9_-]{43}\z"))
            throw new EnrollmentException("GSS_SETUP_CODE is invalid. Remove it for automatic enrollment.");

        string? codeHash = setupCode == null ? null : Hash(setupCode);
        if (state == null || (setupCode != null && state.SetupCodeHash != codeHash))
        {
            byte[] tokenBytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(tokenBytes);
            state = new State { Token = "gss_" + Base64Url(tokenBytes), SetupCodeHash = codeHash };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (exists) store.Replace(path, EncodeState(state));
            else store.WriteNew(path, EncodeState(state));
        }

        // Save the pending token before the request so a lost response is safe to retry.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Post, setupCode == null ? AutomaticEndpoint : RedeemEndpoint);
        if (setupCode == null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Token);
            request.Content = new ByteArrayContent(new JsonWriter().BeginObject().Property("name", ServerInfo.CleanName(serverName)).EndObject().ToBytes());
        }
        else request.Content = new ByteArrayContent(new JsonWriter().BeginObject()
            .Property("setup_code", setupCode).Property("token_hash", Hash(state.Token)).EndObject().ToBytes());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            using HttpResponseMessage response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode == 409)
                throw new EnrollmentException(setupCode == null
                    ? "This connector registration was revoked. Contact the stats operator."
                    : "The setup code is expired or used. Remove it to use automatic enrollment.");
            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                throw new EnrollmentException($"Enrollment returned HTTP {status}.", status == 408 || status == 429 || status >= 500);
            }
            using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var body = new MemoryStream();
            byte[] buffer = new byte[1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (body.Length + read > 2048) throw new SerializationException("Enrollment response exceeds the size limit.");
                body.Write(buffer, 0, read);
            }
            state.ServerId = Json.String(Json.ParseObject(body.ToArray()), "server_id", required: true);
            UploadOptions options = Options(state);
            state.SetupCodeHash = null;
            store.Replace(path, EncodeState(state));
            return options;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EnrollmentException("The enrollment request timed out.", retryable: true);
        }
        catch (HttpRequestException)
        {
            throw new EnrollmentException("The enrollment API could not be reached.", retryable: true);
        }
    }

    private static UploadOptions Options(State state)
    {
        if (state.ServerId == null || !Regex.IsMatch(state.ServerId, @"\Aserver-[a-f0-9-]{36}\z"))
            throw new SerializationException("The assigned server ID is invalid.");
        return new UploadOptions(UploadEndpoint, state.ServerId, state.Token, ProfileEndpoint);
    }

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return Base64Url(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] EncodeState(State state)
    {
        var writer = new JsonWriter().BeginObject();
        if (state.ServerId != null) writer.Property("server_id", state.ServerId);
        if (state.SetupCodeHash != null) writer.Property("setup_code_hash", state.SetupCodeHash);
        return writer.Property("token", state.Token).EndObject().ToBytes();
    }

    private static State DecodeState(byte[] bytes)
    {
        Dictionary<string, object?> obj = Json.ParseObject(bytes);
        return new State
        {
            Token = Json.String(obj, "token", required: true)!,
            SetupCodeHash = Json.String(obj, "setup_code_hash"),
            ServerId = Json.String(obj, "server_id"),
        };
    }

    private sealed class State
    {
        public string Token { get; set; } = string.Empty;
        public string? SetupCodeHash { get; set; }
        public string? ServerId { get; set; }
    }
}

public sealed class EnrollmentException : Exception
{
    internal bool Retryable { get; }

    internal EnrollmentException(string message, bool retryable = false) : base(message)
    {
        Retryable = retryable;
    }
}
