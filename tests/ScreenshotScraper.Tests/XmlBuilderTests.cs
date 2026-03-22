using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Xml;
using Xunit;

namespace ScreenshotScraper.Tests;

public sealed class XmlBuilderTests
{
    [Fact]
    public async Task BuildAsync_ReturnsXmlContainingFieldNames()
    {
        var builder = new XmlBuilder();
        var extractionResult = new ExtractionResult
        {
            Success = true,
            Fields =
            [
                new ExtractedField
                {
                    Name = "DocumentType",
                    RawText = "Invoice",
                    ParsedValue = "Invoice",
                    IsValid = true
                }
            ]
        };

        var result = await builder.BuildAsync(extractionResult);

        Assert.True(result.Success);
        Assert.Contains("ExtractionResult", result.XmlContent);
        Assert.Contains("DocumentType", result.XmlContent);
    }
}
