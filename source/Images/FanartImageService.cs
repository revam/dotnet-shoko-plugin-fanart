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
    private volatile bool _templateUrlRegistered;

    /// <summary>
    /// Registers the Fanart.tv template URL with the server as the default for
    /// <see cref="FanartSources.FanartTV"/>, once.
    /// </summary>
    /// <remarks>
    /// Shoko stores a template URL per image source and rebuilds a remote URL
    /// as <c>string.Format(template, resourceID)</c>. The registration is kept
    /// in memory only, so it has to happen on every start, before any image of
    /// the source is added or downloaded: without it <c>AddImage</c> throws
    /// <see cref="MissingImageSourceTemplateUrlException"/>. A template the
    /// user set for a mirror of their own takes precedence over it on the
    /// server's side, so the plugin never looks at or writes the user's
    /// setting.
    /// </remarks>
    public void RegisterTemplateUrl()
    {
        if (_templateUrlRegistered)
            return;

        imageManager.RegisterTemplateUrl(FanartSources.FanartTV, FanartAssetUrl.TemplateUrl);
        _templateUrlRegistered = true;
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

        RegisterTemplateUrl();

        var configuration = configurationProvider.Load();
        var existingCrossReferences = imageManager
            .GetImageCrossReferencesForEntity(entity, new ImageCrossReferenceFilteringOptions()
            {
                XrefSource = FanartSources.FanartTV,
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
            var imageID = IImageManager.GetIDForImageSourceAndResourceID(FanartSources.FanartTV, entry.ResourceID);
            wanted.Add((entry.ImageType, imageID));
            if (existingCrossReferences.ContainsKey((entry.ImageType, imageID)))
            {
                kept++;
                continue;
            }

            try
            {
                var image = imageManager.GetImageBySourceAndRemoteResourceID(FanartSources.FanartTV, entry.ResourceID)
                    ?? imageManager.AddImage(new ImageData()
                    {
                        Source = FanartSources.FanartTV,
                        ResourceID = entry.ResourceID,
                        Width = entry.Width,
                        Height = entry.Height,
                        LanguageCode = entry.LanguageCode,
                    });

                imageManager.AddImageCrossReference(entity, image, new ImageCrossReferenceData()
                {
                    ImageType = entry.ImageType,
                    Source = FanartSources.FanartTV,
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
