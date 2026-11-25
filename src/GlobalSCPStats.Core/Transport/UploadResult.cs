using System;

namespace GlobalSCPStats.Core.Transport;

public enum UploadOutcome { Accepted, Retry, Blocked }

public sealed class UploadResult
{
    public UploadResult(UploadOutcome outcome, string message, TimeSpan? retryAfter = null)
    {
        Outcome = outcome;
        Message = message;
        RetryAfter = retryAfter;
    }

    public UploadOutcome Outcome { get; }
    public string Message { get; }
    public TimeSpan? RetryAfter { get; }
}
