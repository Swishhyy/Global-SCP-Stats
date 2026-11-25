using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GlobalSCPStats.Core.Storage;

namespace GlobalSCPStats.Core.Transport;

public sealed class ConnectorEnrollment
{
    public const string SetupEnvironmentVariable = "GSS_SETUP_CODE";
    public const string UploadEndpoint = "https://api.globalscpstats.com/v1/stats/batches";
    public const string ProfileEndpoint = "https://api.globalscpstats.com/v1/profiles/lookup";
    private const string RedeemEndpoint = "https://api.globalscpstats.com/v1/enrollment/redeem";
    private const string StateFileName = "enrollment.state";

    private readonly string path;
    private readonly EncryptedFileStore store;

    public ConnectorEnrollment(string directory, byte[] storageKey)
    {
        path = Path.Combine(directory, StateFileName);
        store = new EncryptedFileStore(storageKey);
    }

    public async Task<UploadOptions> GetCredentialsAsync(string? setupCode, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        return await GetCredentialsAsync(setupCode, client, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<UploadOptions> GetCredentialsAsync(
        string? setupCode, HttpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool exists = File.Exists(path);
        State? state = exists ? Decode<State>(store.Read(path)) : null;
        if (state != null && !Regex.IsMatch(state.Token, @"\Agss_[a-zA-Z0-9_-]{43}\z"))
            throw new SerializationException("The saved connector token is invalid.");
        if (state?.ServerId != null)
            return Options(state);

        if (setupCode == null || !Regex.IsMatch(setupCode, @"\Aenroll_[a-zA-Z0-9_-]{43}\z"))
            throw new EnrollmentException("Set GSS_SETUP_CODE to the one-time code from the operator.");

        string codeHash = Hash(setupCode);
        if (state?.SetupCodeHash != codeHash)
        {
            byte[] tokenBytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(tokenBytes);
            state = new State { Token = "gss_" + Base64Url(tokenBytes), SetupCodeHash = codeHash };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (exists) store.Replace(path, Encode(state));
            else store.WriteNew(path, Encode(state));
        }

        // Save the pending token before the request so a lost response is safe to retry.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Post, RedeemEndpoint);
        request.Content = new ByteArrayContent(Encode(new Claim { SetupCode = setupCode, TokenHash = Hash(state.Token) }));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            using HttpResponseMessage response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode == 409)
                throw new EnrollmentException("The setup code is expired, was already used, or its receipt is invalid.");
            if (!response.IsSuccessStatusCode)
                throw new EnrollmentException($"Enrollment returned HTTP {(int)response.StatusCode}. Restart to retry.");
            using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var body = new MemoryStream();
            byte[] buffer = new byte[1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (body.Length + read > 2048) throw new SerializationException("Enrollment response exceeds the size limit.");
                body.Write(buffer, 0, read);
            }
            state.ServerId = Decode<Receipt>(body.ToArray()).ServerId;
            UploadOptions options = Options(state);
            state.SetupCodeHash = null;
            store.Replace(path, Encode(state));
            return options;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EnrollmentException("The enrollment request timed out. Restart with the same setup code to retry.");
        }
        catch (HttpRequestException)
        {
            throw new EnrollmentException("The enrollment API could not be reached. Restart with the same setup code to retry.");
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

    private static byte[] Encode<T>(T value)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        return stream.ToArray();
    }

    private static T Decode<T>(byte[] bytes) where T : class
    {
        using var stream = new MemoryStream(bytes);
        return new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T
            ?? throw new SerializationException("Enrollment data is invalid.");
    }

    [DataContract]
    private sealed class State
    {
        [DataMember(Name = "token", IsRequired = true)]
        public string Token { get; set; } = string.Empty;
        [DataMember(Name = "setup_code_hash", EmitDefaultValue = false)]
        public string? SetupCodeHash { get; set; }
        [DataMember(Name = "server_id", EmitDefaultValue = false)]
        public string? ServerId { get; set; }
    }

    [DataContract]
    private sealed class Claim
    {
        [DataMember(Name = "setup_code")]
        public string SetupCode { get; set; } = string.Empty;
        [DataMember(Name = "token_hash")]
        public string TokenHash { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class Receipt
    {
        [DataMember(Name = "server_id", IsRequired = true)]
        public string? ServerId { get; set; }
    }
}

public sealed class EnrollmentException : Exception
{
    internal EnrollmentException(string message) : base(message) { }
}
