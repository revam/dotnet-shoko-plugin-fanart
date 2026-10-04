using System.ComponentModel.DataAnnotations;
using Shoko.Abstractions.Config;

namespace Shoko.Plugin.Fanart;

/// <summary>
/// Configuration for the fanart.tv artwork plugin.
/// </summary>
/// <remarks>
/// <para>
/// Only the credentials live here. Which sources and kinds get artwork is set
/// per contributor pair, and how many images of each type are downloaded is
/// set in the server's image settings for the fanart.tv source, both on the
/// server's side.
/// </para>
/// <para>
/// fanart.tv requires an API key for every request, and the key belongs to
/// whoever installed the plugin: a key bundled with a release would be shared
/// by every install, and fanart.tv rate-limits per key. Nothing here is marked
/// <c>[Required]</c> on purpose. A required-but-unset property fails
/// configuration validation, which stops the whole plugin from loading, so the
/// missing key is checked for at runtime instead: the plugin stays loaded, logs
/// once, and does nothing until a key is set.
/// </para>
/// </remarks>
[Display(Name = "fanart.tv")]
public class FanartConfiguration : IConfiguration
{
    #region Credentials

    /// <summary>
    /// The project API key issued at <see href="https://fanart.tv/get-an-api-key/"/>,
    /// sent as <c>api_key</c> on every request. Without it the plugin does
    /// nothing at all: fanart.tv has no anonymous access.
    /// </summary>
    [DataType(DataType.Password)]
    [Display(Name = "API Key", Description = "Your fanart.tv project API key. Get one at fanart.tv/get-an-api-key.")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// The personal API key from your fanart.tv account, sent as
    /// <c>client_key</c> alongside the project key. It is optional, and only
    /// changes how fresh the artwork is: new uploads reach a project key after
    /// seven days, a personal key after two, and a VIP account immediately.
    /// </summary>
    [DataType(DataType.Password)]
    [Display(Name = "Personal API Key", Description = "Optional. Your personal fanart.tv key, which shortens the delay on newly uploaded artwork from seven days to two.")]
    public string? PersonalApiKey { get; set; }

    #endregion
}
