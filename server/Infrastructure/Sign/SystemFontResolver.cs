using PdfSharp.Fonts;

namespace NexusDocs.Api.Infrastructure.Sign;

/// <summary>
/// PdfSharp 6.x (cross-platform rewrite) no longer resolves fonts by family name via GDI+/the OS
/// font table the way PdfSharp 1.x on .NET Framework did - it throws
/// "No appropriate font found for family name '...'" at the first XFont(...) construction unless
/// <see cref="GlobalFontSettings.FontResolver"/> is assigned an <see cref="IFontResolver"/>
/// (https://docs.pdfsharp.net/link/font-resolving.html). PdfOverlaySealer draws with "Helvetica"
/// (regular/bold) and "Courier New" (regular) - this resolver maps those family names onto the
/// nearest metric-compatible TrueType fonts already present on Windows (Arial substitutes for the
/// non-installable Helvetica, exactly as most PDF tooling does), loaded straight from
/// %WINDIR%\Fonts so no font files need to be vendored into the repo.
///
/// Registered once in Program.cs via <c>GlobalFontSettings.FontResolver = new SystemFontResolver()</c>
/// before any XFont is created - IFontResolver has no other lifetime hook, so app startup is the
/// only correct place to assign it.
/// </summary>
public sealed class SystemFontResolver : IFontResolver
{
    private static readonly string FontsDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    // faceName -> ttf file under %WINDIR%\Fonts. Covers every family/weight PdfOverlaySealer uses;
    // extend here if a new XFont(...) call introduces another family.
    private static readonly Dictionary<string, string> FaceFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helvetica#Regular"] = "arial.ttf",
        ["Helvetica#Bold"] = "arialbd.ttf",
        ["Courier New#Regular"] = "cour.ttf",
        ["Courier New#Bold"] = "courbd.ttf",
    };

    public byte[] GetFont(string faceName)
    {
        if (!FaceFiles.TryGetValue(faceName, out var fileName))
        {
            // Shouldn't happen - ResolveTypeface only ever hands back keys present in FaceFiles -
            // but fail loudly rather than silently substituting the wrong glyphs into a legal
            // document.
            throw new InvalidOperationException($"SystemFontResolver has no font file mapped for face '{faceName}'.");
        }

        return File.ReadAllBytes(Path.Combine(FontsDirectory, fileName));
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Only the two families PdfOverlaySealer requests are mapped; anything else falls back to
        // Helvetica-equivalent (Arial) rather than throwing, since a missing overlay font must not
        // abort sealing the whole envelope.
        var family = familyName.Equals("Courier New", StringComparison.OrdinalIgnoreCase) ? "Courier New" : "Helvetica";
        var weight = isBold ? "Bold" : "Regular";
        return new FontResolverInfo($"{family}#{weight}");
    }
}
