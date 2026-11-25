using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MEC;
using UnityEngine.Networking;

namespace GlobalSCPStats;

internal sealed class UnityHttpTransport : IDisposable
{
    private readonly Queue<Request> waiting = new();
    private readonly CoroutineHandle pump;
    private UnityWebRequest? web;
    private Request? current;
    private BoundedDownload? download;
    private bool disposed;

    public UnityHttpTransport()
    {
        pump = Timing.RunCoroutine(Pump());
    }

    public HttpMessageHandler CreateHandler() => new Handler(this);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token)
    {
        Uri? uri = message.RequestUri;
        if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
            throw new HttpRequestException("The stats API requires an HTTPS URL without embedded credentials.");
        if (message.Method != HttpMethod.Post || message.Content == null)
            throw new HttpRequestException("The stats transport requires a POST body.");

        var pending = new Request(
            uri.AbsoluteUri,
            await message.Content.ReadAsByteArrayAsync().ConfigureAwait(false),
            message.Headers.Authorization?.ToString(),
            message.Headers.TryGetValues("Idempotency-Key", out var values) ? values.FirstOrDefault() : null);
        // Cancellation must finish the task even while the game thread is shutting down.
        using var registration = token.Register(() => pending.Completion.TrySetCanceled());
        lock (waiting)
        {
            if (disposed) throw new ObjectDisposedException(nameof(UnityHttpTransport));
            waiting.Enqueue(pending);
        }
        return await pending.Completion.Task.ConfigureAwait(false);
    }

    private IEnumerator<float> Pump()
    {
        while (true)
        {
            if (current == null)
            {
                lock (waiting)
                    if (waiting.Count > 0) current = waiting.Dequeue();
                if (current != null && !current.Completion.Task.IsCompleted)
                {
                    try { Start(current); }
                    catch (Exception error)
                    {
                        current.Completion.TrySetException(new HttpRequestException("Unity could not start the stats request.", error));
                        Release();
                    }
                }
            }

            if (current != null)
            {
                if (current.Completion.Task.IsCompleted) Release();
                else if (web!.isDone)
                {
                    try
                    {
                        var response = ReadResponse(web, download!);
                        if (!current.Completion.TrySetResult(response)) response.Dispose();
                    }
                    catch (Exception error) { current.Completion.TrySetException(error); }
                    finally { Release(); }
                }
            }
            yield return Timing.WaitForOneFrame;
        }
    }

    private void Start(Request request)
    {
        web = new UnityWebRequest(request.Url, UnityWebRequest.kHttpVerbPOST)
        {
            redirectLimit = 0,
            timeout = 10,
            uploadHandler = new UploadHandlerRaw(request.Body)
        };
        download = new BoundedDownload();
        web.downloadHandler = download;
        web.SetRequestHeader("Content-Type", "application/json");
        if (request.Authorization != null) web.SetRequestHeader("Authorization", request.Authorization);
        if (request.IdempotencyKey != null) web.SetRequestHeader("Idempotency-Key", request.IdempotencyKey);
        web.SendWebRequest();
    }

    private static HttpResponseMessage ReadResponse(UnityWebRequest request, BoundedDownload body)
    {
        long status = request.responseCode;
        bool validHttpError = status >= 300 && status <= 599;
        if (status < 100 || status > 599 || (!validHttpError && request.result != UnityWebRequest.Result.Success))
            throw new HttpRequestException("Unity could not read the stats API response.");

        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new ByteArrayContent(body.GetBytes()),
        };
        string? retryAfter = request.GetResponseHeader("Retry-After");
        if (retryAfter != null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
    }

    private void Release()
    {
        if (web != null)
        {
            if (!web.isDone) web.Abort();
            web.Dispose();
        }
        web = null;
        download = null;
        current = null;
    }

    public void Dispose()
    {
        lock (waiting)
        {
            if (disposed) return;
            disposed = true;
            while (waiting.Count > 0) waiting.Dequeue().Completion.TrySetCanceled();
        }
        Timing.KillCoroutines(pump);
        current?.Completion.TrySetCanceled();
        Release();
    }

    private sealed class Handler(UnityHttpTransport owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => owner.SendAsync(request, cancellationToken);
    }

    private sealed class Request(string url, byte[] body, string? authorization, string? idempotencyKey)
    {
        public string Url { get; } = url;
        public byte[] Body { get; } = body;
        public string? Authorization { get; } = authorization;
        public string? IdempotencyKey { get; } = idempotencyKey;
        public TaskCompletionSource<HttpResponseMessage> Completion { get; } =
            new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BoundedDownload() : DownloadHandlerScript(new byte[8192])
    {
        private readonly MemoryStream buffer = new();

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (dataLength == 0) return true;
            if (data == null || buffer.Length + dataLength > 65536) return false;
            buffer.Write(data, 0, dataLength);
            return true;
        }

        public byte[] GetBytes() => buffer.ToArray();
    }
}
