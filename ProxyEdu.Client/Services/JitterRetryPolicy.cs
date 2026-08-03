using Microsoft.AspNetCore.SignalR.Client;

namespace ProxyEdu.Client.Services;

public sealed class JitterRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var delay = Delays[Math.Min((int)retryContext.PreviousRetryCount, Delays.Length - 1)];
        return delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 750));
    }
}
