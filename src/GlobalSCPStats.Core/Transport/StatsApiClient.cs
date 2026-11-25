using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using GlobalSCPStats.Core.Storage;
using System.IO;
using GlobalSCPStats.Core.Rewards;

namespace GlobalSCPStats.Core.Transport;

public sealed class StatsApiClient : IDisposable
{
    private readonly UploadOptions options;
    private readonly HttpClient client;

    public StatsApiClient(UploadOptions options) : this(options, new HttpClientHandler { AllowAutoRedirect = false }) { }

    public StatsApiClient(UploadOptions options, HttpMessageHandler handler)
    {
        this.options = options;
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public bool ProfilesEnabled => options.ProfileEndpoint != null;

    public async Task<ProgressProfile?> FetchProfileAsync(string userId, CancellationToken cancellationToken)
    {
        if (options.ProfileEndpoint == null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        CancellationToken requestToken = timeout.Token;
        // Keep player IDs out of URLs and ordinary reverse-proxy access logs.
        using var request = new HttpRequestMessage(HttpMethod.Post, options.ProfileEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);
        request.Content = new ByteArrayContent(ProfileJson.LookupRequest(userId));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            using Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var output = new MemoryStream();
            byte[] buffer = new byte[4096];
            int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, requestToken).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > 65536) throw new System.Runtime.Serialization.SerializationException("Progress profile exceeds the size limit.");
                output.Write(buffer, 0, read);
            }
            return ProfileJson.Deserialize(output.ToArray(), userId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
    }

    public async Task<UploadResult> SendAsync(QueuedBatch batch, CancellationToken cancellationToken)
    {
        // A server ID change must never submit an old server's queue under a new token.
        if (!string.Equals(batch.Batch.ServerId, options.ServerId, StringComparison.Ordinal))
            return new UploadResult(UploadOutcome.Blocked, "Queued server ID differs from the configured server ID. Restore the original configuration.");

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);
        request.Headers.Add("Idempotency-Key", batch.Batch.BatchId);
        request.Content = new ByteArrayContent(batch.Json);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status >= 200 && status < 300)
                return new UploadResult(UploadOutcome.Accepted, "Batch accepted.");
            if (status == 408 || status == 429 || status >= 500)
            {
                TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
                if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
                    retryAfter = date - DateTimeOffset.UtcNow;
                return new UploadResult(UploadOutcome.Retry, $"API returned HTTP {status}; batch retained.", retryAfter);
            }
            return new UploadResult(UploadOutcome.Blocked, $"API returned HTTP {status}; uploads paused until reload. Batch retained.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UploadResult(UploadOutcome.Retry, "API request timed out; batch retained.");
        }
        catch (HttpRequestException)
        {
            // Exception text and response bodies may contain secrets supplied by a remote service.
            return new UploadResult(UploadOutcome.Retry, "API connection failed; batch retained.");
        }
    }

    public void Dispose() => client.Dispose();
}
