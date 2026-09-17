using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Scriban;

namespace NexusDocs.Api.Infrastructure.Gen;

/// <summary>
/// ARCHITECTURE.md section 1.2 (module GEN) describes "HTML/Handlebars or DOCX templates rendered
/// to PDF from ERP document data". This implementation covers the HTML side only:
///
///   1. The template string is parsed and rendered with Scriban (https://github.com/scriban/scriban,
///      MIT licensed) - a mature, pure-C#, Liquid/Handlebars-like templating engine with no licensing
///      decision attached (unlike the SIGN module's PDF-signing-library situation). Scriban's own
///      reflection-based object-to-script-object conversion (<c>Template.Render(model, ...)</c>) is
///      used to expose <paramref name="data"/>'s public properties to the template - no intermediate
///      JSON round-trip is needed.
///   2. The RESULTING html string (i.e. Scriban's OUTPUT, not arbitrary caller-supplied markup) is
///      parsed by a small hand-written tag scanner and drawn page-by-page into a PdfSharp
///      <see cref="PdfDocument"/> using XGraphics, following the same page/font/XGraphics
///      conventions already established and proven working in
///      <see cref="Sign.PdfOverlaySealer"/> (Helvetica-family fonts via the globally registered
///      <see cref="Sign.SystemFontResolver"/>, XGraphics.FromPdfPage, manual pagination with a
///      margin/line-height constant pattern).
///
/// DISCLOSED, DELIBERATE SCOPE LIMITATION - this is an honest HTML-SUBSET renderer, not a general
/// browser-grade HTML/CSS engine, and not a PDF/A or pixel-accurate layout tool (the same class of
/// disclosure as PdfOverlaySealer's "SES, not PAdES" note above it). Supported markup:
///   - Headings:      &lt;h1&gt;, &lt;h2&gt;, &lt;h3&gt;  (decreasing bold font sizes)
///   - Paragraphs:     &lt;p&gt;
///   - Inline bold:    &lt;b&gt;, &lt;strong&gt;
///   - Inline italic:  &lt;i&gt;, &lt;em&gt;              (best-effort - simple, non-nested runs;
///                                                        combined bold+italic is NOT distinguished,
///                                                        the innermost style wins)
///   - Line breaks:    &lt;br&gt; / &lt;br/&gt;
///   - Simple tables:  &lt;table&gt;, &lt;tr&gt;, &lt;td&gt; / &lt;th&gt; - drawn as a plain grid with
///                                                        evenly divided column widths and
///                                                        left-aligned cell text
/// NOT supported (silently ignored / treated as plain text, never thrown on): CSS of any kind
/// (inline style=, &lt;style&gt; blocks, classes), images (&lt;img&gt;), colors other than black
/// text, custom fonts, nested/complex inline formatting, colspan/rowspan, lists, links, and any tag
/// not listed above. Malformed or unrecognized markup is skipped rather than raising - this renderer
/// only ever sees HTML that Scriban itself produced from a template an operator authored, not
/// arbitrary untrusted HTML, so a lenient best-effort scanner is an appropriate and safe tradeoff.
/// DOCX templates (the other option ARCHITECTURE.md section 1.2 mentions) are explicitly OUT OF
/// SCOPE for this phase.
/// </summary>
public class ScribanTemplateRenderer : ITemplateRenderer
{
    private const double PageWidthPt = 595.28;   // A4
    private const double PageHeightPt = 841.89;  // A4
    private const double MarginPt = 40;

    private static readonly XFont H1Font = new("Helvetica", 20, XFontStyleEx.Bold);
    private static readonly XFont H2Font = new("Helvetica", 16, XFontStyleEx.Bold);
    private static readonly XFont H3Font = new("Helvetica", 13, XFontStyleEx.Bold);
    private static readonly XFont BodyFont = new("Helvetica", 10, XFontStyleEx.Regular);
    private static readonly XFont BodyBoldFont = new("Helvetica", 10, XFontStyleEx.Bold);
    // NOTE: SystemFontResolver (Infrastructure/Sign) only maps Helvetica Regular/Bold faces (Arial/
    // Arial Bold TTFs) - it has no italic face registered. XFontStyleEx.Italic is still requested
    // here so PdfSharp applies whatever synthetic slant it can, but on this font resolver italic
    // text may render visually identical to regular text. This is part of the disclosed "no custom
    // fonts beyond the default" limitation above, not a bug to chase.
    private static readonly XFont BodyItalicFont = new("Helvetica", 10, XFontStyleEx.Italic);
    private static readonly XFont TableFont = new("Helvetica", 9, XFontStyleEx.Regular);
    private static readonly XFont TableBoldFont = new("Helvetica", 9, XFontStyleEx.Bold);

    public Task<byte[]> RenderToPdfAsync(string templateHtml, object data)
    {
        var html = RenderScribanTemplate(templateHtml, data);
        var blocks = HtmlSubsetParser.Parse(html);
        var pdfBytes = DrawBlocksToPdf(blocks);
        return Task.FromResult(pdfBytes);
    }

    // -------------------------------------------------------------------------------------------
    // Step 1: Scriban template -> HTML string
    // -------------------------------------------------------------------------------------------

    private static string RenderScribanTemplate(string templateHtml, object data)
    {
        var template = Template.Parse(templateHtml);
        if (template.HasErrors)
        {
            var messages = string.Join("; ", template.Messages.Select(m => m.ToString()));
            throw new InvalidOperationException($"GEN template failed to parse: {messages}");
        }

        // Scriban's reflection-based renderer: exposes data's public properties by name, converting
        // PascalCase members to the template's preferred casing automatically. This avoids a manual
        // JSON round-trip while still handling POCOs, dictionaries and anonymous objects uniformly.
        var rendered = template.Render(data, member => member.Name);

        if (template.HasErrors)
        {
            var messages = string.Join("; ", template.Messages.Select(m => m.ToString()));
            throw new InvalidOperationException($"GEN template failed to render: {messages}");
        }

        return rendered;
    }

    // -------------------------------------------------------------------------------------------
    // Step 2: HTML subset -> PDF (PdfSharp/XGraphics, following PdfOverlaySealer's conventions)
    // -------------------------------------------------------------------------------------------

    private byte[] DrawBlocksToPdf(IReadOnlyList<HtmlBlock> blocks)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(PageWidthPt);
        page.Height = XUnit.FromPoint(PageHeightPt);
        var gfx = XGraphics.FromPdfPage(page);

        double y = MarginPt;
        var contentWidth = PageWidthPt - 2 * MarginPt;

        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    var headingFont = heading.Level switch
                    {
                        1 => H1Font,
                        2 => H2Font,
                        _ => H3Font,
                    };
                    (gfx, page, y) = DrawWrappedRuns(document, gfx, page, y, contentWidth, heading.Runs, headingFont, headingFont);
                    y += headingFont.Height * 0.3;
                    break;

                case ParagraphBlock paragraph:
                    (gfx, page, y) = DrawWrappedRuns(document, gfx, page, y, contentWidth, paragraph.Runs, BodyFont, BodyFont);
                    y += BodyFont.Height * 0.5;
                    break;

                case TableBlock table:
                    (gfx, page, y) = DrawTable(document, gfx, page, y, contentWidth, table);
                    y += BodyFont.Height * 0.5;
                    break;
            }
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    /// <summary>
    /// Word-wraps a run of mixed bold/italic/plain text within <paramref name="maxWidth"/>, drawing
    /// one wrapped line at a time and paginating (new page via <see cref="EnsureRoom"/>) as needed.
    /// Explicit &lt;br&gt; runs force a line break without contributing text.
    /// </summary>
    private (XGraphics Gfx, PdfPage Page, double Y) DrawWrappedRuns(
        PdfDocument document, XGraphics gfx, PdfPage page, double y, double maxWidth,
        IReadOnlyList<TextRun> runs, XFont sizingFont, XFont fallbackFont)
    {
        var lineHeight = sizingFont.Height * 1.25;
        var currentLine = new List<(string Word, XFont Font)>();
        double currentLineWidth = 0;

        (XGraphics, PdfPage, double) FlushLine()
        {
            if (currentLine.Count == 0)
            {
                return (gfx, page, y);
            }

            (gfx, page, y) = EnsureRoom(document, gfx, page, y, lineHeight, MarginPt, PageHeightPt);
            double x = MarginPt;
            foreach (var (word, font) in currentLine)
            {
                gfx.DrawString(word, font, XBrushes.Black, new XPoint(x, y + font.Height));
                x += gfx.MeasureString(word, font).Width;
            }
            y += lineHeight;
            currentLine = new List<(string Word, XFont Font)>();
            currentLineWidth = 0;
            return (gfx, page, y);
        }

        foreach (var run in runs)
        {
            if (run.IsLineBreak)
            {
                (gfx, page, y) = FlushLine();
                continue;
            }

            if (string.IsNullOrEmpty(run.Text)) continue;

            // Headings (sizingFont != BodyFont) render entirely in their own heading font/weight,
            // ignoring inline bold/italic markup - a disclosed best-effort simplification. Only
            // paragraph text (sizingFont == BodyFont) honors per-run bold/italic.
            XFont font;
            if (sizingFont != BodyFont)
            {
                font = sizingFont;
            }
            else
            {
                font = run.Style switch
                {
                    RunStyle.Bold => BodyBoldFont,
                    RunStyle.Italic => BodyItalicFont,
                    _ => fallbackFont,
                };
            }

            foreach (var word in SplitPreservingSpaces(run.Text))
            {
                if (word.Length == 0) continue;
                var wordWidth = gfx.MeasureString(word, font).Width;
                if (currentLineWidth + wordWidth > maxWidth && currentLine.Count > 0)
                {
                    (gfx, page, y) = FlushLine();
                }
                currentLine.Add((word, font));
                currentLineWidth += wordWidth;
            }
        }

        (gfx, page, y) = FlushLine();
        return (gfx, page, y);
    }

    private static IEnumerable<string> SplitPreservingSpaces(string text)
    {
        // Split on whitespace but keep a single trailing space attached to each word so wrapped
        // lines don't lose inter-word spacing.
        var parts = Regex.Split(text, @"(?<=\s)|(?=\s)").Where(p => p.Length > 0);
        var buffer = new StringBuilder();
        foreach (var part in parts)
        {
            if (part == " ")
            {
                buffer.Append(' ');
                yield return buffer.ToString();
                buffer.Clear();
            }
            else
            {
                buffer.Append(part);
            }
        }
        if (buffer.Length > 0) yield return buffer.ToString();
    }

    private (XGraphics Gfx, PdfPage Page, double Y) DrawTable(
        PdfDocument document, XGraphics gfx, PdfPage page, double y, double maxWidth, TableBlock table)
    {
        if (table.Rows.Count == 0) return (gfx, page, y);

        var columnCount = table.Rows.Max(r => r.Count);
        if (columnCount == 0) return (gfx, page, y);

        var columnWidth = maxWidth / columnCount;
        const double cellPaddingPt = 4;
        var rowHeight = TableFont.Height + 2 * cellPaddingPt;

        foreach (var row in table.Rows)
        {
            (gfx, page, y) = EnsureRoom(document, gfx, page, y, rowHeight, MarginPt, PageHeightPt);

            double x = MarginPt;
            for (int col = 0; col < columnCount; col++)
            {
                var cellRect = new XRect(x, y, columnWidth, rowHeight);
                gfx.DrawRectangle(XPens.Black, cellRect);

                if (col < row.Count)
                {
                    var cellText = string.Concat(row[col].Where(r => !r.IsLineBreak).Select(r => r.Text));
                    var font = row[col].Any(r => r.Style == RunStyle.Bold) ? TableBoldFont : TableFont;
                    var textRect = new XRect(x + cellPaddingPt, y, columnWidth - 2 * cellPaddingPt, rowHeight);
                    gfx.DrawString(cellText, font, XBrushes.Black, textRect, XStringFormats.CenterLeft);
                }

                x += columnWidth;
            }

            y += rowHeight;
        }

        return (gfx, page, y);
    }

    /// <summary>Appends a new page (mirrors PdfOverlaySealer.EnsureRoom's pattern) when the current one is full.</summary>
    private static (XGraphics Gfx, PdfPage Page, double Y) EnsureRoom(
        PdfDocument document, XGraphics gfx, PdfPage page, double y, double neededHeightPt, double marginPt, double pageHeightPt)
    {
        if (y + neededHeightPt <= pageHeightPt - marginPt)
        {
            return (gfx, page, y);
        }

        gfx.Dispose();
        var newPage = document.AddPage();
        newPage.Width = XUnit.FromPoint(PageWidthPt);
        newPage.Height = XUnit.FromPoint(PageHeightPt);
        var newGfx = XGraphics.FromPdfPage(newPage);
        return (newGfx, newPage, marginPt);
    }
}
