namespace Shoko.Plugin.Fanart;

/// <summary>
/// Build-time constants. The value here is a placeholder in the tree and is
/// rewritten by CI for official builds.
/// </summary>
internal static class Constants
{
    /// <summary>
    /// The project key official builds ship with, substituted by CI from a
    /// secret. For local development, either replace the text below with your
    /// own project key or set one in the plugin settings, which wins over this
    /// either way.
    /// </summary>
    /// <remarks>
    /// Fanart.tv's terms ask that a program send its own project key and let a
    /// user add their personal key alongside it, which is why the project key
    /// belongs to the build and the personal key belongs to the user. An
    /// unofficial build leaves the placeholder in place and the plugin then
    /// needs a project key from the settings before it will fetch anything.
    /// The comparison against the placeholder deliberately lives in
    /// <c>FanartApiClient</c> rather than here, because CI rewrites this file
    /// and would rewrite the thing being compared against along with it.
    /// </remarks>
    public const string ProjectApiKey = "FANART_PROJECT_KEY_GOES_HERE";
}
