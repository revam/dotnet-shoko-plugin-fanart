using System;
using System.Collections.Generic;
using Moq;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata.Containers;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Image;
using Shoko.Abstractions.Metadata.Image.CrossReferences;
using Shoko.Abstractions.Metadata.Image.Exceptions;
using Shoko.Abstractions.Metadata.Image.Options;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Plugin.Fanart.Images;
using Shoko.Plugin.Fanart.Mapping;
using Xunit;

namespace Shoko.Plugin.Fanart.Tests;

/// <summary>
/// What the plugin writes through <see cref="IImageManager"/>, and what it
/// leaves alone.
/// </summary>
public class FanartImageServiceTests
{
    private static readonly IWithImages _entity = Mock.Of<IWithImages>();

    private static FanartArtwork Artwork(string resourceID, ImageEntityType imageType = ImageEntityType.Logo, int ordering = 0)
        => new(resourceID, imageType, "hdtvlogo", "en", 800, 310, ordering);

    private static Guid IdFor(string resourceID)
        => IImageManager.GetIDForImageSourceAndResourceID(FanartImageService.Source, resourceID);

    private static IImage ImageFor(string resourceID)
    {
        var image = new Mock<IImage>();
        image.SetupGet(i => i.ID).Returns(IdFor(resourceID));
        image.SetupGet(i => i.ResourceID).Returns(resourceID);
        image.SetupGet(i => i.Source).Returns(FanartImageService.Source);
        return image.Object;
    }

    private static IImageCrossReference CrossReferenceFor(string resourceID, ImageEntityType imageType = ImageEntityType.Logo, bool isPreferred = false)
    {
        var xref = new Mock<IImageCrossReference>();
        xref.SetupGet(x => x.ImageID).Returns(IdFor(resourceID));
        xref.SetupGet(x => x.ImageType).Returns(imageType);
        xref.SetupGet(x => x.Source).Returns(FanartImageService.Source);
        xref.SetupGet(x => x.IsPreferred).Returns(isPreferred);
        return xref.Object;
    }

    private static (FanartImageService Service, Mock<IImageManager> Manager) CreateService(
        FanartConfiguration? configuration = null,
        IReadOnlyList<IImageCrossReference>? existing = null,
        string? templateUrl = null
    )
    {
        var manager = new Mock<IImageManager>(MockBehavior.Strict);
        manager.Setup(m => m.GetTemplateUrlForSource(FanartImageService.Source)).Returns(templateUrl);
        manager.Setup(m => m.SetTemplateUrlForSource(FanartImageService.Source, It.IsAny<string?>()));
        manager.Setup(m => m.GetImageCrossReferencesForEntity(_entity, It.IsAny<ImageCrossReferenceFilteringOptions>()))
            .Returns(existing ?? []);

        var configurationService = new FakeConfigurationService(configuration ?? new FanartConfiguration());
        var service = new FanartImageService(
            manager.Object,
            new ConfigurationProvider<FanartConfiguration>(configurationService),
            new RecordingLogger<FanartImageService>()
        );

        return (service, manager);
    }

    [Fact]
    public void TheTemplateUrlIsRegisteredWhenTheServerHasNone()
    {
        // The server ships defaults for AniDB, TMDB and AniList only, so
        // without this every AddImage call throws.
        var (service, manager) = CreateService();

        service.EnsureTemplateUrl();

        manager.Verify(m => m.SetTemplateUrlForSource(FanartImageService.Source, FanartAssetUrl.TemplateUrl), Times.Once);
    }

    [Fact]
    public void AnExistingTemplateUrlIsLeftAlone()
    {
        var (service, manager) = CreateService(templateUrl: "https://mirror.example.test/{0}");

        service.EnsureTemplateUrl();

        manager.Verify(m => m.SetTemplateUrlForSource(It.IsAny<DataSource>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void TheTemplateUrlIsOnlyCheckedOnce()
    {
        var (service, manager) = CreateService();

        service.EnsureTemplateUrl();
        service.EnsureTemplateUrl();

        manager.Verify(m => m.GetTemplateUrlForSource(FanartImageService.Source), Times.Once);
    }

    [Fact]
    public void NewArtworkIsRegisteredAndThenAttached()
    {
        var (service, manager) = CreateService();
        var image = ImageFor("tv/1/hdtvlogo/a.png");
        manager.Setup(m => m.GetImageBySourceAndRemoteResourceID(FanartImageService.Source, "tv/1/hdtvlogo/a.png", It.IsAny<bool>())).Returns((IImage?)null);
        manager.Setup(m => m.AddImage(It.IsAny<ImageData>())).Returns(image);
        manager.Setup(m => m.AddImageCrossReference(_entity, image, It.IsAny<ImageCrossReferenceData>())).Returns(Mock.Of<IImageCrossReference>());

        var changes = service.Apply(_entity, [Artwork("tv/1/hdtvlogo/a.png")]);

        Assert.Equal(1, changes.Added);
        manager.Verify(m => m.AddImage(It.Is<ImageData>(data =>
            data.Source == DataSource.FanartTV &&
            data.ResourceID == "tv/1/hdtvlogo/a.png" &&
            data.Width == 800 &&
            data.Height == 310 &&
            data.LanguageCode == "en"
        )), Times.Once);
        manager.Verify(m => m.AddImageCrossReference(_entity, image, It.Is<ImageCrossReferenceData>(data =>
            data.ImageType == ImageEntityType.Logo &&
            // Attributed to Fanart.tv rather than to the user, and not
            // preferred: choosing a preferred image is the user's call.
            data.Source == DataSource.FanartTV &&
            data.IsEnabled &&
            !data.IsPreferred &&
            data.Ordering == 0
        )), Times.Once);
    }

    [Fact]
    public void AnImageTheServerAlreadyKnowsIsNotRegisteredTwice()
    {
        var (service, manager) = CreateService();
        var image = ImageFor("tv/1/hdtvlogo/a.png");
        manager.Setup(m => m.GetImageBySourceAndRemoteResourceID(FanartImageService.Source, "tv/1/hdtvlogo/a.png", It.IsAny<bool>())).Returns(image);
        manager.Setup(m => m.AddImageCrossReference(_entity, image, It.IsAny<ImageCrossReferenceData>())).Returns(Mock.Of<IImageCrossReference>());

        var changes = service.Apply(_entity, [Artwork("tv/1/hdtvlogo/a.png")]);

        Assert.Equal(1, changes.Added);
        manager.Verify(m => m.AddImage(It.IsAny<ImageData>()), Times.Never);
    }

    [Fact]
    public void DownloadArtwork_DecidesWhetherTheArtworkIsMarkedForDownload()
    {
        var (service, manager) = CreateService(new FanartConfiguration() { DownloadArtwork = false });
        var image = ImageFor("tv/1/hdtvlogo/a.png");
        manager.Setup(m => m.GetImageBySourceAndRemoteResourceID(FanartImageService.Source, It.IsAny<string>(), It.IsAny<bool>())).Returns(image);
        manager.Setup(m => m.AddImageCrossReference(_entity, image, It.IsAny<ImageCrossReferenceData>())).Returns(Mock.Of<IImageCrossReference>());

        service.Apply(_entity, [Artwork("tv/1/hdtvlogo/a.png")]);

        manager.Verify(m => m.AddImageCrossReference(_entity, image, It.Is<ImageCrossReferenceData>(data => !data.IsDesired)), Times.Once);
    }

    [Fact]
    public void ArtworkThatIsAlreadyAttachedIsLeftUntouched()
    {
        var (service, manager) = CreateService(existing: [CrossReferenceFor("tv/1/hdtvlogo/a.png")]);

        var changes = service.Apply(_entity, [Artwork("tv/1/hdtvlogo/a.png")]);

        Assert.Equal(0, changes.Added);
        Assert.Equal(1, changes.Kept);
        Assert.Equal(0, changes.Withdrawn);
        Assert.False(changes.HasChanges);
        manager.Verify(m => m.AddImage(It.IsAny<ImageData>()), Times.Never);
        manager.Verify(m => m.RemoveImageCrossReference(It.IsAny<IImageCrossReference>()), Times.Never);
    }

    [Fact]
    public void TheSameImageUnderTwoTypesIsTwoSeparateLinks()
    {
        // The dedup key is the image and the image type, not the image alone.
        var (service, manager) = CreateService(existing: [CrossReferenceFor("tv/1/a.png", ImageEntityType.Logo)]);
        var image = ImageFor("tv/1/a.png");
        manager.Setup(m => m.GetImageBySourceAndRemoteResourceID(FanartImageService.Source, It.IsAny<string>(), It.IsAny<bool>())).Returns(image);
        manager.Setup(m => m.AddImageCrossReference(_entity, image, It.IsAny<ImageCrossReferenceData>())).Returns(Mock.Of<IImageCrossReference>());

        var changes = service.Apply(_entity, [Artwork("tv/1/a.png", ImageEntityType.Logo), Artwork("tv/1/a.png", ImageEntityType.Banner)]);

        Assert.Equal(1, changes.Kept);
        Assert.Equal(1, changes.Added);
    }

    [Fact]
    public void ArtworkFanartNoLongerListsIsWithdrawn()
    {
        var stale = CrossReferenceFor("tv/1/hdtvlogo/gone.png");
        var (service, manager) = CreateService(existing: [stale]);
        manager.Setup(m => m.RemoveImageCrossReference(stale)).Returns(true);

        var changes = service.Apply(_entity, []);

        Assert.Equal(1, changes.Withdrawn);
        manager.Verify(m => m.RemoveImageCrossReference(stale), Times.Once);
    }

    [Fact]
    public void WithdrawnArtworkTheUserPreferredIsKept()
    {
        var preferred = CrossReferenceFor("tv/1/hdtvlogo/gone.png", isPreferred: true);
        var (service, _) = CreateService(existing: [preferred]);

        var changes = service.Apply(_entity, []);

        // Nothing to verify beyond the strict mock: a RemoveImageCrossReference
        // call would have thrown, since it is not set up.
        Assert.Equal(0, changes.Withdrawn);
    }

    [Fact]
    public void WithdrawalCanBeSwitchedOff()
    {
        var stale = CrossReferenceFor("tv/1/hdtvlogo/gone.png");
        var (service, _) = CreateService(new FanartConfiguration() { RemoveWithdrawnArtwork = false }, existing: [stale]);

        var changes = service.Apply(_entity, []);

        Assert.Equal(0, changes.Withdrawn);
    }

    [Fact]
    public void ALinkThatLostARaceIsCountedAsAlreadyThere()
    {
        var (service, manager) = CreateService();
        var image = ImageFor("tv/1/hdtvlogo/a.png");
        manager.Setup(m => m.GetImageBySourceAndRemoteResourceID(FanartImageService.Source, It.IsAny<string>(), It.IsAny<bool>())).Returns(image);
        manager.Setup(m => m.AddImageCrossReference(_entity, image, It.IsAny<ImageCrossReferenceData>()))
            .Throws(new ImageCrossReferenceExistsException()
            {
                CrossReference = Mock.Of<IImageCrossReference>(),
                Image = image,
                Entity = _entity,
            });

        var changes = service.Apply(_entity, [Artwork("tv/1/hdtvlogo/a.png")]);

        Assert.Equal(0, changes.Added);
        Assert.Equal(1, changes.Kept);
        Assert.Equal(0, changes.Failed);
    }

    [Fact]
    public void AnImageTypeShokoRefusesIsCountedAndSkipped()
    {
        var (service, manager) = CreateService();
        manager.Setup(m => m.GetImageBySourceAndRemoteResourceID(FanartImageService.Source, It.IsAny<string>(), It.IsAny<bool>())).Returns((IImage?)null);
        manager.Setup(m => m.AddImage(It.IsAny<ImageData>())).Throws(new UnsupportedImageTypeException()
        {
            ImageSource = FanartImageService.Source,
            ImageResourceID = "tv/1/hdtvlogo/a.svg",
            FileExtension = ".svg",
            DetectedMimeType = "image/svg+xml",
        });

        var changes = service.Apply(_entity, [Artwork("tv/1/hdtvlogo/a.svg")]);

        Assert.Equal(0, changes.Added);
        Assert.Equal(1, changes.Failed);
    }

    [Fact]
    public void OnlyTheEntitysOwnLinksAreConsidered()
    {
        // The default walks the entity's links, and a row belonging to a linked
        // entity is not this plugin's to withdraw.
        var (service, manager) = CreateService();

        service.Apply(_entity, []);

        manager.Verify(m => m.GetImageCrossReferencesForEntity(_entity, It.Is<ImageCrossReferenceFilteringOptions>(options =>
            options.LinkedEntityImages == false &&
            options.XrefSource == DataSource.FanartTV
        )), Times.Once);
    }
}
