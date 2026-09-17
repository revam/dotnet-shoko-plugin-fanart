using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shoko.Plugin.Fanart.Api;

/// <summary>
/// Token bucket rate limiter for Fanart.tv calls. Thread-safe.
/// </summary>
/// <remarks>
/// Fanart.tv publishes no request-per-second figure. Its documentation says
/// only that requests "may be rate limited" per API key when usage is heavy,
/// and answers with HTTP 429 and a <c>Retry-After</c> header when that happens
/// (<see cref="FanartApiClient"/> honours it). The defaults here, a burst of
/// five refilling at two per second, are therefore a politeness budget rather
/// than a documented limit: a full sweep of a large collection is spread over
/// minutes instead of arriving all at once.
/// </remarks>
public sealed class FanartRateLimiter : IDisposable
{
    private readonly int _maxTokens;
    private readonly double _tokensPerSecond;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly Lock _lock = new();

    private double _currentTokens;
    private DateTimeOffset _lastRefill;

    /// <summary>
    /// Initializes a new instance of the <see cref="FanartRateLimiter"/> class.
    /// </summary>
    /// <param name="maxTokens">Maximum burst capacity, and the maximum number of concurrent requests.</param>
    /// <param name="tokensPerSecond">Tokens refilled per second.</param>
    /// <param name="timeProvider">
    /// The time source to measure refills against. Defaults to
    /// <see cref="TimeProvider.System"/>; a test passes its own so refills can
    /// be simulated without a real wait.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="maxTokens"/> or
    /// <paramref name="tokensPerSecond"/> is not positive.
    /// </exception>
    public FanartRateLimiter(int maxTokens = 5, double tokensPerSecond = 2.0, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tokensPerSecond);

        _maxTokens = maxTokens;
        _tokensPerSecond = tokensPerSecond;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _concurrencySemaphore = new SemaphoreSlim(maxTokens, maxTokens);
        _currentTokens = maxTokens;
        _lastRefill = _timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Acquires a token, waiting until one is available. Callers must pair this
    /// with a <see cref="Release"/> call in a <c>finally</c> block.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once a token has been acquired.</returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled while
    /// waiting.
    /// </exception>
    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _concurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (!TryConsumeToken())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(50), _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            _concurrencySemaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// Releases the concurrency slot acquired by <see cref="WaitAsync"/>. Must
    /// be called exactly once per successful <see cref="WaitAsync"/> call.
    /// </summary>
    public void Release() => _concurrencySemaphore.Release();

    /// <summary>
    /// The number of tokens currently available, after refilling for elapsed
    /// time. Exposed for tests; callers use <see cref="WaitAsync"/>.
    /// </summary>
    internal double AvailableTokens
    {
        get
        {
            lock (_lock)
            {
                Refill();
                return _currentTokens;
            }
        }
    }

    private bool TryConsumeToken()
    {
        lock (_lock)
        {
            Refill();
            if (_currentTokens < 1)
                return false;

            _currentTokens--;
            return true;
        }
    }

    private void Refill()
    {
        var now = _timeProvider.GetUtcNow();
        var elapsedSeconds = (now - _lastRefill).TotalSeconds;
        if (elapsedSeconds <= 0)
            return;

        _currentTokens = Math.Min(_maxTokens, _currentTokens + (elapsedSeconds * _tokensPerSecond));
        _lastRefill = now;
    }

    /// <inheritdoc/>
    public void Dispose() => _concurrencySemaphore.Dispose();
}
