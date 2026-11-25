using System;

namespace GlobalSCPStats.Core.Transport;

public enum UploadOutcome { Accepted, Retry, Blocked }

public sealed class UploadResult(UploadOutcome outcome, string message, TimeSpan? retryAfter = null)
{
    public UploadOutcome Outcome { get; } = outcome;
    public string Message { get; } = message;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
