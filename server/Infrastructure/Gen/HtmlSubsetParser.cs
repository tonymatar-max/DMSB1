using System.Text.RegularExpressions;

namespace NexusDocs.Api.Infrastructure.Gen;

/// <summary>Inline text-run style within a paragraph/heading/cell. See ScribanTemplateRenderer's
/// top-of-file doc comment for the exact disclosed HTML subset this supports.</summary>
public enum RunStyle
{
    Regular,
    Bold,
    Italic,
}

/// <summary>One run of inline text with a single style, or a forced line break (<c>IsLineBreak</c>)
/// produced by a &lt;br&gt; tag.</summary>
public sealed record TextRun(string Text, RunStyle Style, bool IsLineBreak = false)
{
    public static readonly TextRun Break = new(string.Empty, RunStyle.Regular, IsLineBreak: true);
}

public abstract record HtmlBlock;

public sealed record HeadingBlock(int Level, List<TextRun> Runs) : HtmlBlock;

public sealed record ParagraphBlock(List<TextRun> Runs) : HtmlBlock;

/// <summary>Rows of cells; each cell is itself a list of text runs (so a cell can contain
/// bold/italic/line-break markup too, best-effort).</summary>
public sealed record TableBlock(List<List<List<TextRun>>> Rows) : HtmlBlock;

/// <summary>
/// A small, deliberately lenient hand-written tag scanner for the HTML subset that
/// <see cref="ScribanTemplateRenderer"/> renders to PDF. This only ever parses HTML that Scriban
/// itself produced from an operator-authored template - not arbitrary untrusted markup - so it
/// favors "skip and keep going" over throwing on anything unexpected, malformed, or unsupported.
/// See ScribanTemplateRenderer's top-of-file doc comment for the exact list of supported tags.
/// </summary>
public static class HtmlSubsetParser
{
    private static readonly Regex TokenPattern = new(@"<[^>]*>|[^<]+", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex TagNamePattern = new(@"^<\s*/?\s*([a-zA-Z0-9]+)", RegexOptions.Compiled);

    public static List<HtmlBlock> Parse(string html)
    {
        var blocks = new List<HtmlBlock>();
        if (string.IsNullOrWhiteSpace(html)) return blocks;

        var tokens = TokenPattern.Matches(html).Select(m => m.Value).ToList();

        // Inline formatting state shared across the whole document; reset whenever a block-level
        // tag opens/closes since bold/italic never crosses paragraph boundaries in this subset.
        var styleStack = new List<RunStyle>();

        List<TextRun>? currentRuns = null;
        int? currentHeadingLevel = null;
        TableBlock? currentTable = null;
        List<List<TextRun>>? currentRow = null;

        void FlushParagraphOrHeading()
        {
            if (currentRuns is { Count: > 0 })
            {
                blocks.Add(currentHeadingLevel is int level
                    ? new HeadingBlock(level, currentRuns)
                    : new ParagraphBlock(currentRuns));
            }
            currentRuns = null;
            currentHeadingLevel = null;
        }

        void AppendText(string text)
        {
            if (text.Length == 0) return;
            var style = styleStack.Count > 0 ? styleStack[^1] : RunStyle.Regular;

            if (currentTable is not null)
            {
                currentRow ??= new List<List<TextRun>>();
                if (currentRow.Count == 0) currentRow.Add(new List<TextRun>());
                currentRow[^1].Add(new TextRun(text, style));
            }
            else
            {
                currentRuns ??= new List<TextRun>();
                currentRuns.Add(new TextRun(text, style));
            }
        }

        foreach (var token in tokens)
        {
            if (!token.StartsWith('<'))
            {
                // Plain text between tags. Collapse runs of whitespace (including newlines from the
                // source template) the way a browser would, but keep at least one space so words
                // don't run together.
                var normalized = Regex.Replace(token, @"\s+", " ");
                AppendText(normalized);
                continue;
            }

            var nameMatch = TagNamePattern.Match(token);
            if (!nameMatch.Success)
            {
                // Something like a comment or an unparseable fragment - skip rather than throw.
                continue;
            }

            var tagName = nameMatch.Groups[1].Value.ToLowerInvariant();
            var isClosing = token.StartsWith("</");
            var isSelfClosing = token.EndsWith("/>") || tagName is "br" or "hr" or "img";

            switch (tagName)
            {
                case "h1" or "h2" or "h3":
                    if (!isClosing)
                    {
                        FlushParagraphOrHeading();
                        currentHeadingLevel = tagName switch { "h1" => 1, "h2" => 2, _ => 3 };
                        currentRuns = new List<TextRun>();
                    }
                    else
                    {
                        FlushParagraphOrHeading();
                    }
                    break;

                case "p" or "div":
                    if (!isClosing)
                    {
                        FlushParagraphOrHeading();
                        currentRuns = new List<TextRun>();
                    }
                    else
                    {
                        FlushParagraphOrHeading();
                    }
                    break;

                case "b" or "strong":
                    if (!isClosing) styleStack.Add(RunStyle.Bold);
                    else if (styleStack.Count > 0) styleStack.RemoveAt(styleStack.Count - 1);
                    break;

                case "i" or "em":
                    if (!isClosing) styleStack.Add(RunStyle.Italic);
                    else if (styleStack.Count > 0) styleStack.RemoveAt(styleStack.Count - 1);
                    break;

                case "br":
                    if (currentTable is not null && currentRow is { Count: > 0 })
                    {
                        currentRow[^1].Add(TextRun.Break);
                    }
                    else
                    {
                        currentRuns ??= new List<TextRun>();
                        currentRuns.Add(TextRun.Break);
                    }
                    break;

                case "table":
                    if (!isClosing)
                    {
                        FlushParagraphOrHeading();
                        currentTable = new TableBlock(new List<List<List<TextRun>>>());
                    }
                    else if (currentTable is not null)
                    {
                        blocks.Add(currentTable);
                        currentTable = null;
                        currentRow = null;
                    }
                    break;

                case "tr":
                    if (!isClosing)
                    {
                        currentRow = new List<List<TextRun>>();
                    }
                    else if (currentTable is not null && currentRow is not null)
                    {
                        currentTable.Rows.Add(currentRow);
                        currentRow = null;
                    }
                    break;

                case "td" or "th":
                    if (!isClosing)
                    {
                        currentRow ??= new List<List<TextRun>>();
                        currentRow.Add(new List<TextRun>());
                    }
                    // closing </td>/</th> needs no action - the cell list is already appended.
                    break;

                default:
                    // Any tag not in the supported subset (span, ul/li, a, img, style, script, ...)
                    // is silently skipped per the disclosed scope - neither its markup nor an error
                    // leaks into the rendered PDF. Its inner text (if any) still arrives as plain
                    // text tokens and gets appended normally.
                    break;
            }

            _ = isSelfClosing; // tags with no separate closing token (br/hr/img) are fully handled above
        }

        FlushParagraphOrHeading();
        if (currentTable is not null)
        {
            // Unclosed <table> at end of document - salvage what we parsed rather than dropping it.
            if (currentRow is { Count: > 0 }) currentTable.Rows.Add(currentRow);
            blocks.Add(currentTable);
        }

        return blocks;
    }
}
