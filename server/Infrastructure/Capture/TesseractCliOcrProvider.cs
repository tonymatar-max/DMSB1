using System.Diagnostics;

namespace NexusDocs.Api.Infrastructure.Capture;

/// <summary>
/// Secondary <see cref="IOcrProvider"/> implementation: shells out to the "tesseract" CLI
/// (Tesseract OCR, https://github.com/tesseract-ocr/tesseract) for content PdfPig could not
/// extract text from (i.e. genuinely scanned/image-only pages).
///
/// HONEST STATUS OF THIS CODE (read before assuming it "just works" end to end):
///   - <see cref="IsTesseractAvailableAsync"/> and <see cref="ExtractTextFromImageAsync"/> are
///     real, correctly-written code against Tesseract's actual documented CLI contract
///     (`tesseract --version` to detect availability; `tesseract &lt;input&gt; &lt;output-base&gt;`
///     writing `&lt;output-base&gt;.txt`). They have NOT been executed against a real tesseract
///     binary in this environment — none is installed here. The same disclosure convention is
///     used elsewhere in this codebase for code that can't be exercised locally (see the SAP B1
///     adapter's doc comment on not having run against a live B1 system).
///   - RASTERIZATION GAP: Tesseract OCRs images (PNG/TIFF/JPEG/etc.), not PDFs directly for
///     multi-page reliability, and PdfPig (this project's PDF library) does not rasterize pages to
///     images. No PDF-to-image rasterizer (e.g. a "pdftoppm" or ImageMagick "magick" CLI) is
///     confirmed present in this environment, and wiring one in without confirming it's actually
///     available would be exactly the kind of code that "looks like it works but doesn't" — so
///     this phase does NOT implement PDF rasterization. <see cref="ExtractTextAsync"/> therefore
///     throws <see cref="NotSupportedException"/> when handed PDF bytes. The real, working case
///     this provider supports is being handed pre-rasterized image bytes directly (PNG/JPEG) via
///     <see cref="ExtractTextFromImageAsync"/> — that path is real and would work today if a
///     tesseract binary were installed. Wiring a PDF rasterization step (via pdftoppm/magick, or a
///     .NET rasterization library) in front of this provider is a documented follow-up once that
///     dependency choice is made — see ARCHITECTURE.md section 11 item 5.
/// </summary>
public class TesseractCliOcrProvider : IOcrProvider
{
    private static readonly TimeSpan AvailabilityCheckTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OcrTimeout = TimeSpan.FromMinutes(2);

    public string ProviderName => "Tesseract CLI";

    /// <summary>
    /// Checks whether the "tesseract" executable is reachable on PATH by attempting to run
    /// "tesseract --version" with a short timeout. Wrapped in try/catch so a missing binary
    /// produces a clean "not available" (false) instead of an unhandled Win32Exception
    /// (the exception .NET throws when Process.Start can't find the executable at all).
    /// </summary>
    public static async Task<bool> IsTesseractAvailableAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "tesseract",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            using var cts = new CancellationTokenSource(AvailabilityCheckTimeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            // Covers Win32Exception (binary not found) and any other failure to even start the
            // process — all of these mean "tesseract is not available here".
            return false;
        }
    }

    /// <summary>
    /// This provider does not implement PDF rasterization (see class-level doc comment) — it only
    /// supports being handed image bytes directly. Calling this with PDF content always throws.
    /// Callers with a PDF must rasterize pages to images themselves (not implemented in this
    /// phase) and call <see cref="ExtractTextFromImageAsync"/> per page, then concatenate.
    /// </summary>
    public Task<OcrResult> ExtractTextAsync(Stream pdfContent)
    {
        throw new NotSupportedException(
            "TesseractCliOcrProvider does not rasterize PDF pages to images in this phase (no " +
            "PDF-to-image rasterizer is wired in — see the doc comment on this class and " +
            "ARCHITECTURE.md section 11 item 5). Pass pre-rasterized image bytes (PNG/JPEG) to " +
            "ExtractTextFromImageAsync instead.");
    }

    /// <summary>
    /// The real, working OCR path: runs "tesseract &lt;input&gt; &lt;output-base&gt;" against a
    /// pre-rasterized image (PNG/JPEG/TIFF) and reads back the resulting "&lt;output-base&gt;.txt"
    /// file, per Tesseract's documented CLI contract. NOT executed against a real tesseract binary
    /// in this environment — see the class-level doc comment.
    /// </summary>
    /// <param name="imageContent">Raw bytes of a single-page image (PNG/JPEG/TIFF).</param>
    /// <param name="imageExtension">
    /// File extension (including the dot, e.g. ".png") matching the image format, so Tesseract's
    /// image-format auto-detection has a usable hint.
    /// </param>
    public async Task<OcrResult> ExtractTextFromImageAsync(byte[] imageContent, string imageExtension = ".png")
    {
        if (!await IsTesseractAvailableAsync())
        {
            throw new TesseractNotAvailableException(
                "The \"tesseract\" CLI is not available on PATH in this environment. Install " +
                "Tesseract OCR (https://github.com/tesseract-ocr/tesseract) and ensure the " +
                "\"tesseract\" executable is on PATH to enable OCR of scanned documents.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "nexusdocs-ocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var inputPath = Path.Combine(tempDir, "input" + imageExtension);
            var outputBasePath = Path.Combine(tempDir, "output");
            await File.WriteAllBytesAsync(inputPath, imageContent);

            var startInfo = new ProcessStartInfo
            {
                FileName = "tesseract",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outputBasePath);

            using var process = Process.Start(startInfo)
                ?? throw new TesseractNotAvailableException("Failed to start the \"tesseract\" process.");

            using var cts = new CancellationTokenSource(OcrTimeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw new TesseractNotAvailableException(
                    $"tesseract did not complete within {OcrTimeout.TotalSeconds:0}s and was terminated.");
            }

            var outputTextPath = outputBasePath + ".txt";
            if (process.ExitCode != 0 || !File.Exists(outputTextPath))
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                throw new TesseractNotAvailableException(
                    $"tesseract exited with code {process.ExitCode}: {stderr}");
            }

            var text = await File.ReadAllTextAsync(outputTextPath);
            return new OcrResult(Text: text, LooksLikeScannedImage: text.Trim().Length < PdfPigTextProvider.LowTextCharacterThreshold);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
    }
}

/// <summary>
/// Thrown when OCR is requested but the "tesseract" CLI is not available on PATH, or fails to run
/// correctly. Callers should surface this as a clear, actionable error rather than letting the
/// pipeline silently do nothing.
/// </summary>
public class TesseractNotAvailableException(string message) : Exception(message);
