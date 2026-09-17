using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata.Containers;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Image;
using Shoko.Abstractions.Metadata.Image.CrossReferences;
using Shoko.Abstractions.Metadata.Image.Exceptions;
using Shoko.Abstractions.Metadata.Image.Options;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Plugin.Fanart.Mapping;

namespace Shoko.Plugin.Fanart.Images;

/// <summary>
/// Writes selected Fanart.tv artwork into Shoko through
/// <see cref="IImageManager"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two rows go into the database per image. <c>AddImage</c> registers the image
/// itself, keyed by source and resource ID, and says nothing about what it is a
/// picture of. <c>AddImageCrossReference</c> is the row that attaches it to an
/// entity with an image type. Neither implies the other, and an image with no
/// cross-reference is an orphan the server will eventually purge.
/// </para>
/// <para>
/// The artwork is attached to the TMDB show or movie itself, not to the shoko
/// series in front of it. A shoko series reads the images of everything it
/// links to, so artwork attached to the TMDB show shows up on the series
/// anyway, and attaching it to the provider entity means it survives a series
/// being removed and re-added, and is not duplicated when two shoko series link
/// to the same TMDB show.
/// </para>
/// </remarks>
public sealed class FanartImageService(
    IImageManager imageManager,
    ConfigurationProvider<FanartConfiguration> configurationProvider,
    ILogger<FanartImageService> logger
)
{
    /// <summary>
    /// The data source every image and cross-reference this plugin writes is
    /// attributed to.
    /// </summary>
    /// <remarks>
    /// The image source has to be <see cref="DataSource.FanartTV"/>, since that
    /// is what the template URL is registered against. The cross-reference
    /// source is the same on purpose: <see cref="DataSource.Plugin"/> is shared
    /// by every plugin, and this plugin has to be able to recognise its own
    /// rows to withdraw artwork that Fanart.tv no longer lists.
    /// </remarks>
    public const DataSource Source = DataSource.FanartTV;

    private bool _templateUrlChecked;

    /// <summary>
    /// Registers the Fanart.tv template URL with the server, unless one is
    /// already set.
    /// </summary>
    /// <remarks>
    /// Shoko stores a template URL per image source and rebuilds a remote URL
    /// as <c>string.Format(template, resourceID)</c>. It ships defaults for
    /// AniDB, TMDB and AniList only, so without this call
    /// <c>AddImage</c> throws
    /// <see cref="MissingImageSourceTemplateUrlException"/> for every Fanart.tv
    /// image. An existing value is left alone: it is stored in the server's own
    /// configuration, so a user pointing Fanart.tv at a mirror of their own
    /// should not have it overwritten on every startup.
    /// </remarks>
    public void EnsureTemplateUrl()
    {
        if (_templateUrlChecked)
            return;

        _templateUrlChecked = true;
        if (imageManager.GetTemplateUrlForSource(Source) is { Length: > 0 } existing)
        {
            if (!string.Equals(existing, FanartAssetUrl.TemplateUrl, StringComparison.Ordinal))
                logger.LogInformation("Leaving the existing Fanart.tv image template URL in place. (Template={TemplateUrl})", existing);
            return;
        }

        imageManager.SetTemplateUrlForSource(Source, FanartAssetUrl.TemplateUrl);
        logger.LogInformation("Registered the Fanart.tv image template URL. (Template={TemplateUrl})", FanartAssetUrl.TemplateUrl);
    }

    /// <summary>
    /// Attaches the selected artwork to an entity, and withdraws this plugin's
    /// own links to artwork that is no longer listed.
    /// </summary>
    /// <param name="entity">The TMDB show or movie to attach the artwork to.</param>
    /// <param name="artwork">
    /// The artwork to attach, as chosen by <see cref="FanartArtworkSelector"/>.
    /// </param>
    /// <returns>What changed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="entity"/> or <paramref name="artwork"/> is
    /// <see langword="null"/>.
    /// </exception>
    public FanartArtworkChanges Apply(IWithImages entity, IReadOnlyList<FanartArtwork> artwork)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(artwork);

        EnsureTemplateUrl();

        var configuration = configurationProvider.Load();
        var existingCrossReferences = imageManager
            .GetImageCrossReferencesForEntity(entity, new ImageCrossReferenceFilteringOptions()
            {
                XrefSource = Source,
                // Only what this entity itself owns. The default walks the
                // entity's links, and a row belonging to a linked entity is not
                // ours to withdraw.
                LinkedEntityImages = false,
            })
            .ToDictionary(xref => (xref.ImageType, xref.ImageID));

        var added = 0;
        var kept = 0;
        var failed = 0;
        var wanted = new HashSet<(ImageEntityType, Guid)>();
        foreach (var entry in artwork)
        {
            var imageID = IImageManager.GetIDForImageSourceAndResourceID(Source, entry.ResourceID);
            wanted.Add((entry.ImageType, imageID));
            if (existingCrossReferences.ContainsKey((entry.ImageType, imageID)))
            {
                kept++;
                continue;
            }

            try
            {
                var image = imageManager.GetImageBySourceAndRemoteResourceID(Source, entry.ResourceID)
                    ?? imageManager.AddImage(new ImageData()
                    {
                        Source = Source,
                        ResourceID = entry.ResourceID,
                        Width = entry.Width,
                        Height = entry.Height,
                        LanguageCode = entry.LanguageCode,
                    });

                imageManager.AddImageCrossReference(entity, image, new ImageCrossReferenceData()
                {
                    ImageType = entry.ImageType,
                    Source = Source,
                    IsEnabled = true,
                    IsDesired = configuration.DownloadArtwork,
                    IsPreferred = false,
                    Ordering = entry.Ordering,
                });
                added++;
            }
            catch (ImageCrossReferenceExistsException)
            {
                // Someone else got there between the read above and this write.
                kept++;
            }
            catch (UnsupportedImageTypeException ex)
            {
                failed++;
                logger.LogDebug(ex, "Skipped a Fanart.tv {Kind} image in a format Shoko does not accept. (ResourceID={ResourceID})", entry.Kind, entry.ResourceID);
            }
            catch (MissingImageSourceTemplateUrlException ex)
            {
                failed++;
                logger.LogWarning(ex, "Unable to add Fanart.tv artwork without a registered template URL. (ResourceID={ResourceID})", entry.ResourceID);
            }
        }

        var withdrawn = 0;
        if (configuration.RemoveWithdrawnArtwork)
        {
            foreach (var (key, xref) in existingCrossReferences)
            {
                if (wanted.Contains(key))
                    continue;

                // A user promoting one of these to preferred is a decision, and
                // artwork dropping off Fanart.tv is not a good enough reason to
                // undo it.
                if (xref.IsPreferred)
                {
                    logger.LogDebug("Keeping withdrawn Fanart.tv artwork because it is the preferred {ImageType}. (Image={ImageID})", xref.ImageType, xref.ImageID);
                    continue;
                }

                if (imageManager.RemoveImageCrossReference(xref))
                    withdrawn++;
            }
        }

        return new FanartArtworkChanges(added, kept, withdrawn, failed);
    }
}

/// <summary>
/// What one call to <see cref="FanartImageService.Apply"/> changed.
/// </summary>
/// <param name="Added">Artwork newly attached to the entity.</param>
/// <param name="Kept">Artwork that was already attached.</param>
/// <param name="Withdrawn">Links removed because Fanart.tv no longer lists the artwork.</param>
/// <param name="Failed">Artwork that could not be attached.</param>
public sealed record FanartArtworkChanges(int Added, int Kept, int Withdrawn, int Failed)
{
    /// <summary>
    /// Whether anything at all changed.
    /// </summary>
    public bool HasChanges => Added > 0 || Withdrawn > 0;
}
