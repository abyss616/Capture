using System.Xml.Linq;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Models;

namespace ScreenshotScraper.Xml;

/// <summary>
/// Builds a minimal XML payload from extracted fields.
/// </summary>
public sealed class XmlBuilder : IXmlBuilder
{
    public Task<XmlBuildResult> BuildAsync(ExtractionResult extractionResult, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = new XDocument(
            new XElement(
                "ExtractionResult",
                new XAttribute("success", extractionResult.Success),
                extractionResult.Fields.Select(field =>
                    new XElement(
                        "Field",
                        new XAttribute("name", field.Name),
                        new XAttribute("isValid", field.IsValid),
                        new XElement("RawText", field.RawText ?? string.Empty),
                        new XElement("ParsedValue", field.ParsedValue ?? string.Empty),
                        new XElement("Error", field.Error ?? string.Empty))),
                extractionResult.Errors.Select(error => new XElement("WorkflowError", error))));

        return Task.FromResult(new XmlBuildResult
        {
            XmlContent = document.ToString(),
            Success = true,
            Errors = []
        });
    }
}
