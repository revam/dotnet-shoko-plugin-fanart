using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Shoko.Abstractions.Config;

namespace Shoko.Plugin.Fanart;

/// <summary>
/// Configuration for the Fanart.tv artwork plugin.
/// </summary>
/// <remarks>
/// Fanart.tv requires an API key for every request, and the key belongs to
/// whoever installed the plugin: a key bundled with a release would be shared
/// by every install, and Fanart.tv rate-limits per key. Nothing here is marked
/// <c>[Required]</c> on purpose. A required-but-unset property fails
/// configuration validation, which stops the whole plugin from loading, so the
/// missing key is checked for at runtime instead: the plugin stays loaded, logs
/// once, and does nothing until a key is set.
/// </remarks>
[Display(Name = "Fanart.tv")]
public class FanartConfiguration : IConfiguration
{
    #region Credentials

    /// <summary>
    /// The project API key issued at <see href="https://fanart.tv/get-an-api-key/"/>,
    /// sent as <c>api_key</c> on every request. Without it the plugin does
    /// nothing at all: Fanart.tv has no anonymous access.
    /// </summary>
    [DataType(DataType.Password)]
    [Display(Name = "API Key", Description = "Your Fanart.tv project API key. Get one at fanart.tv/get-an-api-key.")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// The personal API key from your Fanart.tv account, sent as
    /// <c>client_key</c> alongside the project key. It is optional, and only
    /// changes how fresh the artwork is: new uploads reach a project key after
    /// seven days, a personal key after two, and a VIP account immediately.
    /// </summary>
    [DataType(DataType.Password)]
    [Display(Name = "Personal API Key", Description = "Optional. Your personal Fanart.tv key, which shortens the delay on newly uploaded artwork from seven days to two.")]
    public string? PersonalApiKey { get; set; }

    #endregion

    #region Sweep

    /// <summary>
    /// How often the sweep job walks the collection and refreshes artwork.
    /// Changing this requires a restart, since the interval is only read when
    /// the recurring job is registered at startup.
    /// </summary>
    /// <remarks>
    /// Seven days is the default because that is how long a newly uploaded
    /// image takes to reach a project API key. Sweeping more often than the
    /// data changes only spends requests.
    /// </remarks>
    [Display(Name = "Sweep Interval")]
    [DefaultValue(typeof(TimeSpan), "7.00:00:00")]
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether the sweep also looks up TMDB movies. Movie artwork uses the same
    /// API and the same key, keyed on the TMDB movie ID, so leaving it on costs
    /// one extra request per linked movie.
    /// </summary>
    [Display(Name = "Include Movies")]
    [DefaultValue(true)]
    public bool IncludeMovies { get; set; } = true;

    #endregion

    #region Artwork

    /// <summary>
    /// How many images to keep per entity and image type, taking the
    /// most-liked first. Fanart.tv can hold dozens of backdrops for a popular
    /// show, and every one of them is a row in the image table and a candidate
    /// download.
    /// </summary>
    [Display(Name = "Maximum Images Per Type")]
    [Range(1, 100)]
    [DefaultValue(5)]
    public int MaximumImagesPerType { get; set; } = 5;

    /// <summary>
    /// Whether the artwork this plugin adds is marked as desired, which is what
    /// makes the server auto-download it. With this off the artwork is still
    /// registered and visible, and is only fetched when something asks for it.
    /// </summary>
    [Display(Name = "Download Artwork")]
    [DefaultValue(true)]
    public bool DownloadArtwork { get; set; } = true;

    /// <summary>
    /// Whether a refresh removes this plugin's own links to artwork Fanart.tv
    /// no longer lists, which is how a deleted or replaced upload stops being
    /// offered. Links a user has marked as preferred are left alone either way,
    /// and the image itself is never deleted, only the link to the entity.
    /// </summary>
    [Display(Name = "Remove Withdrawn Artwork")]
    [DefaultValue(true)]
    public bool RemoveWithdrawnArtwork { get; set; } = true;

    #endregion
}
