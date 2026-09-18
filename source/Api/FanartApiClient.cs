using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;

namespace Shoko.Plugin.Fanart.Api;

/// <summary>
/// Thin wrapper around the Fanart.tv artwork API
/// (<see href="https://webservice.fanart.tv"/>).
/// </summary>
/// <remarks>
/// Every request carries a project key as <c>api_key</c>, from the settings or
/// else from what CI stamped into an official build, and the user's personal
/// key as <c>client_key</c> when one is set. API version 3.2 is used
/// throughout, because it is the only version that reports image dimensions,
/// which Shoko stores alongside the image.
/// </remarks>
public sealed class FanartApiClient(
    HttpClient httpClient,
    FanartRateLimiter rateLimiter,
    ConfigurationProvider<FanartConfiguration> configurationProvider,
    ILogger<FanartApiClient> logger
)
{
    /// <summary>
    /// The API version every request is made against.
    /// </summary>
    public const string ApiVersion = "v3.2";

    private const int MaximumRateLimitRetries = 3;

    /// <summary>
    /// Whether a project key is available. Nothing can be fetched without one.
    /// </summary>
    public bool HasApiKey => ResolveProjectKey(configurationProvider.Load()) is not null;

    /// <summary>
    /// Resolves the project key to send, preferring the configured one over the
    /// key an official build was stamped with.
    /// </summary>
    /// <param name="configuration">The loaded configuration.</param>
    /// <returns>The key, or <see langword="null"/> when neither is available.</returns>
    private static string? ResolveProjectKey(FanartConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
            return configuration.ApiKey;

        // CI rewrites `Constants.ProjectApiKey` for official builds, so in the
        // tree this comparison is between two equal literals and the compiler
        // sees the second branch as unreachable. It is not, once stamped.
#pragma warning disable CS0162 // Unreachable code detected
        return Constants.ProjectApiKey != "FANART_PROJECT_KEY_GOES_HERE" ? Constants.ProjectApiKey : null;
#pragma warning restore CS0162 // Unreachable code detected
    }

    /// <summary>
    /// Gets the artwork Fanart.tv holds for a TheTVDB show.
    /// </summary>
    /// <param name="tvdbShowID">The TheTVDB show ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The artwork set, or <see langword="null"/> when Fanart.tv has nothing
    /// for the show or no API key is configured.
    /// </returns>
    /// <exception cref="FanartApiException">
    /// Thrown when Fanart.tv answers with anything other than success or
    /// "not found".
    /// </exception>
    public Task<FanartArtworkSet?> GetShowArtwork(int tvdbShowID, CancellationToken cancellationToken = default)
        => GetArtwork($"{ApiVersion}/tv/{tvdbShowID.ToString(CultureInfo.InvariantCulture)}", cancellationToken);

    /// <summary>
    /// Gets the artwork Fanart.tv holds for a TMDB movie. The movie endpoint
    /// accepts either a TMDB or an IMDB ID; the TMDB ID is used, since that is
    /// the one Shoko always has for a TMDB movie.
    /// </summary>
    /// <param name="tmdbMovieID">The TMDB movie ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The artwork set, or <see langword="null"/> when Fanart.tv has nothing
    /// for the movie or no API key is configured.
    /// </returns>
    /// <exception cref="FanartApiException">
    /// Thrown when Fanart.tv answers with anything other than success or
    /// "not found".
    /// </exception>
    public Task<FanartArtworkSet?> GetMovieArtwork(int tmdbMovieID, CancellationToken cancellationToken = default)
        => GetArtwork($"{ApiVersion}/movies/{tmdbMovieID.ToString(CultureInfo.InvariantCulture)}", cancellationToken);

    private async Task<FanartArtworkSet?> GetArtwork(string path, CancellationToken cancellationToken)
    {
        var configuration = configurationProvider.Load();
        if (ResolveProjectKey(configuration) is not { } projectKey)
            return null;

        var requestUri = $"{path}?api_key={Uri.EscapeDataString(projectKey)}";
        if (!string.IsNullOrWhiteSpace(configuration.PersonalApiKey))
            requestUri += $"&client_key={Uri.EscapeDataString(configuration.PersonalApiKey)}";

        for (var attempt = 0; ; attempt++)
        {
            using var response = await Send(requestUri, cancellationToken).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                // Fanart.tv has no artwork for the ID, which is the common case
                // rather than an error: most entries in a library have none.
                case HttpStatusCode.NotFound:
                    return null;

                case HttpStatusCode.TooManyRequests when attempt < MaximumRateLimitRetries:
                    var delay = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
                        ?? TimeSpan.FromSeconds(1);
                    if (delay < TimeSpan.Zero)
                        delay = TimeSpan.FromSeconds(1);
                    logger.LogWarning("Fanart.tv rate limited the request for {Path}; retrying in {Delay}.", path, delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;

                case var status when !response.IsSuccessStatusCode:
                    throw new FanartApiException(status, $"Fanart.tv answered {(int)status} ({status}) for /{path}.");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // UNVERIFIED: how Fanart.tv reports an ID it does not know is
            // assumed to be a 404, but some of its endpoints are reported to
            // answer 200 with an error body instead. That case needs no special
            // handling here: such a body parses into an envelope with no
            // artwork groups, which every caller already treats as "nothing to
            // do".
            try
            {
                return JsonSerializer.Deserialize<FanartArtworkSet>(content, FanartJson.Options);
            }
            catch (JsonException ex)
            {
                throw new FanartApiException(response.StatusCode, $"Fanart.tv answered /{path} with a body that could not be parsed.", ex);
            }
        }
    }

    private async Task<HttpResponseMessage> Send(string requestUri, CancellationToken cancellationToken)
    {
        await rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            rateLimiter.Release();
        }
    }
}

/// <summary>
/// Thrown when Fanart.tv answers a request with something the client cannot
/// make sense of.
/// </summary>
public sealed class FanartApiException : Exception
{
    /// <summary>
    /// The status code Fanart.tv answered with.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Whether the status code says the API key is missing, wrong or not
    /// allowed, which no amount of retrying will fix.
    /// </summary>
    public bool IsAuthenticationFailure => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    /// <summary>
    /// Initializes a new instance of the <see cref="FanartApiException"/> class.
    /// </summary>
    /// <param name="statusCode">The status code Fanart.tv answered with.</param>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public FanartApiException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
        => StatusCode = statusCode;
}
