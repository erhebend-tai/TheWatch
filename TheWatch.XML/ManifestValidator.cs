// =============================================================================
// ManifestValidator — Validates TheWatchArchitecture.xml against the XSD schema
// =============================================================================
// Uses System.Xml.Schema to validate the architecture manifest against the
// embedded XSD schema. Returns a list of validation errors/warnings.
//
// Usage:
//   // Validate embedded resource:
//   var errors = ManifestValidator.ValidateEmbeddedResource();
//   if (errors.Count == 0) Console.WriteLine("Valid!");
//
//   // Validate a file:
//   var errors = ManifestValidator.ValidateFile("path/to/TheWatchArchitecture.xml");
//
//   // Validate with custom XSD:
//   var errors = ManifestValidator.ValidateFile(xmlPath, xsdPath);
//
// Note: The current TheWatchArchitecture.xml does not declare a namespace, while
//       the XSD targets "http://thewatch.io/architecture/2026-03". Validation
//       will report namespace-related warnings. This is expected for the as-is
//       manifest and can be resolved by adding xmlns to the XML root element.
//
// Write-ahead log:
//   - v1.0.0: Initial — ValidateEmbeddedResource, ValidateFile, ValidateXml (2026-03-24)
// =============================================================================

using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace TheWatch.XML;

/// <summary>
/// Represents a single validation finding (error or warning) from XSD validation.
/// </summary>
/// <param name="Severity">Whether this is an Error or Warning.</param>
/// <param name="Message">Human-readable description of the validation issue.</param>
/// <param name="LineNumber">Line number in the XML where the issue was found (0 if unavailable).</param>
/// <param name="LinePosition">Column position in the XML where the issue was found (0 if unavailable).</param>
public record ValidationFinding(
    XmlSeverityType Severity,
    string Message,
    int LineNumber,
    int LinePosition);

/// <summary>
/// Validates TheWatchArchitecture.xml against the XSD schema.
/// </summary>
public static class ManifestValidator
{
    /// <summary>
    /// Validates the embedded TheWatchArchitecture.xml against the embedded XSD.
    /// </summary>
    /// <returns>List of validation findings. Empty list means the document is valid.</returns>
    public static IReadOnlyList<ValidationFinding> ValidateEmbeddedResource()
    {
        var assembly = typeof(ManifestValidator).Assembly;

        using var xmlStream = assembly.GetManifestResourceStream("TheWatch.XML.TheWatchArchitecture.xml")
            ?? throw new InvalidOperationException("Embedded XML resource not found.");
        using var xsdStream = assembly.GetManifestResourceStream("TheWatch.XML.TheWatchArchitecture.xsd")
            ?? throw new InvalidOperationException("Embedded XSD resource not found.");

        var xmlDoc = XDocument.Load(xmlStream);
        var xsdReader = XmlReader.Create(xsdStream);

        return ValidateInternal(xmlDoc, xsdReader);
    }

    /// <summary>
    /// Validates an XML file against the embedded XSD schema.
    /// </summary>
    /// <param name="xmlFilePath">Path to the XML file to validate.</param>
    /// <returns>List of validation findings.</returns>
    public static IReadOnlyList<ValidationFinding> ValidateFile(string xmlFilePath)
    {
        if (!File.Exists(xmlFilePath))
            throw new FileNotFoundException($"XML file not found: {xmlFilePath}", xmlFilePath);

        var assembly = typeof(ManifestValidator).Assembly;
        using var xsdStream = assembly.GetManifestResourceStream("TheWatch.XML.TheWatchArchitecture.xsd")
            ?? throw new InvalidOperationException("Embedded XSD resource not found.");

        var xmlDoc = XDocument.Load(xmlFilePath);
        var xsdReader = XmlReader.Create(xsdStream);

        return ValidateInternal(xmlDoc, xsdReader);
    }

    /// <summary>
    /// Validates an XML file against a specific XSD file.
    /// </summary>
    /// <param name="xmlFilePath">Path to the XML file.</param>
    /// <param name="xsdFilePath">Path to the XSD schema file.</param>
    /// <returns>List of validation findings.</returns>
    public static IReadOnlyList<ValidationFinding> ValidateFile(string xmlFilePath, string xsdFilePath)
    {
        if (!File.Exists(xmlFilePath))
            throw new FileNotFoundException($"XML file not found: {xmlFilePath}", xmlFilePath);
        if (!File.Exists(xsdFilePath))
            throw new FileNotFoundException($"XSD file not found: {xsdFilePath}", xsdFilePath);

        var xmlDoc = XDocument.Load(xmlFilePath);
        using var xsdStream = File.OpenRead(xsdFilePath);
        var xsdReader = XmlReader.Create(xsdStream);

        return ValidateInternal(xmlDoc, xsdReader);
    }

    /// <summary>
    /// Validates an XDocument against an XSD schema provided via XmlReader.
    /// </summary>
    public static IReadOnlyList<ValidationFinding> ValidateXml(XDocument xmlDoc, XmlReader xsdReader)
    {
        return ValidateInternal(xmlDoc, xsdReader);
    }

    // ── Internal ────────────────────────────────────────────────────────

    private static IReadOnlyList<ValidationFinding> ValidateInternal(XDocument xmlDoc, XmlReader xsdReader)
    {
        var findings = new List<ValidationFinding>();

        try
        {
            var schemaSet = new XmlSchemaSet();
            schemaSet.Add(null, xsdReader);
            schemaSet.Compile();

            xmlDoc.Validate(schemaSet, (_, e) =>
            {
                findings.Add(new ValidationFinding(
                    e.Severity,
                    e.Message,
                    e.Exception?.LineNumber ?? 0,
                    e.Exception?.LinePosition ?? 0));
            });
        }
        catch (XmlSchemaException ex)
        {
            findings.Add(new ValidationFinding(
                XmlSeverityType.Error,
                $"Schema compilation error: {ex.Message}",
                ex.LineNumber,
                ex.LinePosition));
        }
        catch (XmlException ex)
        {
            findings.Add(new ValidationFinding(
                XmlSeverityType.Error,
                $"XML parsing error: {ex.Message}",
                ex.LineNumber,
                ex.LinePosition));
        }

        return findings;
    }
}
