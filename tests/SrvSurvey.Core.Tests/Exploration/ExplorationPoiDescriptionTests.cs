using System.Text.Json;
using SrvSurvey.Core.Exploration;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class ExplorationPoiDescriptionTests
{
    /// <summary>Verifies markdown photos preserve order and captions without exposing image code.</summary>
    [Fact]
    public void MarkdownPhotosPreserveOrderAndCaptionsWithoutExposingImageCode()
    {
        var description = ExplorationPoiDescription.Parse(
            """
            Before *the photo*.
            ![A beautiful moon](https://example.test/moon(1).png "Title")
            Between the photos. [Original report](https://example.test/report)
            ![](<https://example.test/second.jpg>)
            After the photos.
            """
        );
        Assert.Equal(5, description.Blocks.Count);
        Assert.Equal("Before the photo.", description.Blocks[0].Text);
        Assert.Equal("A beautiful moon", description.Blocks[1].Caption);
        Assert.Equal("https://example.test/moon(1).png", description.Blocks[1].ImageUri!.AbsoluteUri);
        Assert.Contains("Original report", description.Blocks[2].Text);
        Assert.Equal("https://example.test/second.jpg", description.Blocks[3].ImageUri!.AbsoluteUri);
        Assert.Equal("After the photos.", description.Blocks[4].Text);
        Assert.DoesNotContain("example.test", description.Text);
        Assert.DoesNotContain("![", description.Text);
    }

    /// <summary>Verifies html photos decode attributes and paragraphs and never treat hidden scripts as images.</summary>
    [Fact]
    public void HtmlPhotosDecodeAttributesAndParagraphsAndNeverTreatHiddenScriptsAsImages()
    {
        var description = ExplorationPoiDescription.Parse(
            """
            <p>Before &amp; after.</p><img alt='A &amp; B' src="/poiimages/moon.jpg?x=1&amp;y=2" />
            <p>Credit <b>Commander</b>.</p><IMG SRC=//example.test/second.png>
            <script><img src="https://example.test/hidden.jpg" />code</script>
            <style>hidden style</style>
            """
        );
        Assert.Equal(4, description.Blocks.Count);
        Assert.Equal("Before & after.", description.Blocks[0].Text);
        Assert.Equal("A & B", description.Blocks[1].Caption);
        Assert.Equal("https://edastro.com/poiimages/moon.jpg?x=1&y=2", description.Blocks[1].ImageUri!.AbsoluteUri);
        Assert.Equal("Credit Commander.", description.Blocks[2].Text);
        Assert.Equal("https://example.test/second.png", description.Blocks[3].ImageUri!.AbsoluteUri);
        Assert.DoesNotContain("hidden", description.Text);
        Assert.DoesNotContain("code", description.Text);
    }

    /// <summary>Verifies unsupported images are omitted from the text preview.</summary>
    [Theory]
    [InlineData("![Photo](file:///etc/passwd)")]
    [InlineData("![Photo](javascript:alert)")]
    [InlineData("![Photo](not-a-url)")]
    [InlineData("![Photo][missing-reference]")]
    [InlineData("<img alt='Photo' />")]
    public void UnsupportedImagesAreOmittedFromTheTextPreview(string markup)
    {
        var description = ExplorationPoiDescription.Parse("Introduction. " + markup + " End.");
        Assert.All(description.Blocks, block => Assert.Null(block.ImageUri));
        Assert.DoesNotContain(markup, description.Text);
        Assert.Contains("Introduction.", description.Text);
        Assert.Contains("End.", description.Text);
    }

    /// <summary>Verifies catalogue retains html images and readable descriptions alongside identity.</summary>
    [Fact]
    public void CatalogueRetainsHtmlImagesAndReadableDescriptionsAlongsideIdentity()
    {
        using var json = JsonDocument.Parse(
            """
            [{"id":42,"galMapSearch":"Sol","name":"Moon","descriptionMardown":"Fallback",
              "descriptionHtml":"<p>Scenery</p><img src='https://example.test/moon.jpg' alt='Moon' /><p>Credit</p>"}]
            """
        );
        ExplorationPoi poi = Assert.Single(ExplorationPoiCatalog.Parse(json.RootElement).Items);
        Assert.Equal("GEC:42", poi.Identity);
        Assert.Equal("Scenery\n\nCredit", poi.Description);
        Assert.Equal("Moon", Assert.Single(poi.DescriptionBlocks, block => block.ImageUri is not null).Caption);
        Assert.Empty(ExplorationPoiDescription.Parse("").Blocks);
        Assert.Throws<ArgumentNullException>(() => ExplorationPoiDescription.Parse(null!));
    }
}
