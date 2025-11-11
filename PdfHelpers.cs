using System;
using System.IO;
using System.Linq;
using System.Threading;
using UglyToad.PdfPig;
using Docnet.Core;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Text.RegularExpressions;

namespace SolutionBot
{
    internal static class PdfHelpers
    {
        // Keep a small throttle so multiple large renders don't spike memory at once.
        private static readonly SemaphoreSlim RenderSemaphore = new(1, 1);

        // Find the first page number (1-based) that contains the given problem marker.
        // Tries variants: "5-10", "5.10", "5–10", "Problem 5–10", etc.
        // NEW: Skip pages where the problem only appears in the abbreviated form "Prob. 5–10" (i.e. not the actual problem heading).
        public static int? FindFirstMatchingPage(string pdfPath, string normalizedProblem)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF not found.", pdfPath);

            var enDashVariant = normalizedProblem.Replace('-', '–');
            var variants = new[]
            {
                normalizedProblem,
                normalizedProblem.Replace('-', '.'),
                enDashVariant,
                $"Problem {normalizedProblem}",
                $"Problem {enDashVariant}",
                $"Problem {normalizedProblem.Replace('-', '.')}"

            };

            // Extract chapter/problem numbers if in the form "<ch>-<pr>"
            string? chapter = null, prob = null;
            var parts = normalizedProblem.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                chapter = parts[0];
                prob = parts[1];
            }

            using var doc = PdfDocument.Open(pdfPath);
            foreach (var page in doc.GetPages())
            {
                var text = page.Text;
                if (string.IsNullOrEmpty(text)) continue;

                if (variants.Any(v => text.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0))
                    return page.Number; // 1-based
            }

            return null;
        }

        // Simplified rendering: render the page at a fixed DPI (default 300) with high JPEG quality.
        // Adds optional white background flattening (default: true) so transparent regions become white.
        // Returns path to a temporary JPEG file.
        public static string RenderPageToJpeg(string pdfPath, int oneBasedPageNumber, int dpi = 300, int jpegQuality = 95, bool forceWhiteBackground = true)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
                throw new ArgumentNullException(nameof(pdfPath));
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF not found.", pdfPath);
            if (oneBasedPageNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(oneBasedPageNumber));

            var pageIndex = oneBasedPageNumber - 1;

            RenderSemaphore.Wait();
            try
            {
                // Get page size in points (1 point = 1/72 inch). Convert directly using desired DPI.
                double widthPoints, heightPoints;
                using (var pdf = PdfDocument.Open(pdfPath))
                {
                    var page = pdf.GetPage(oneBasedPageNumber);
                    widthPoints = page.Width;
                    heightPoints = page.Height;
                }

                var pixelWidth = (int)Math.Round(widthPoints * dpi / 72.0);
                var pixelHeight = (int)Math.Round(heightPoints * dpi / 72.0);

                using var lib = DocLib.Instance;
                using var docReader = lib.GetDocReader(pdfPath, new PageDimensions(pixelWidth, pixelHeight));

                if (pageIndex < 0 || pageIndex >= docReader.GetPageCount())
                    throw new ArgumentOutOfRangeException(nameof(oneBasedPageNumber), "Page number exceeds document length.");

                using var pageReader = docReader.GetPageReader(pageIndex);
                var raw = pageReader.GetImage();
                var renderWidth = pageReader.GetPageWidth();
                var renderHeight = pageReader.GetPageHeight();

                using var image = Image.LoadPixelData<Bgra32>(raw, renderWidth, renderHeight);

                // Flatten transparency against white if requested.
                if (forceWhiteBackground)
                {
                    if (image.DangerousTryGetSinglePixelMemory(out var memory))
                    {
                        var span = memory.Span;
                        for (int i = 0; i < span.Length; i++)
                        {
                            var p = span[i];
                            byte a = p.A;
                            if (a == 255)
                                continue;
                            if (a == 0)
                            {
                                // Fully transparent -> pure white
                                span[i] = new Bgra32(255, 255, 255, 255);
                            }
                            else
                            {
                                // Alpha blend over white: out = src * a + white * (1 - a)
                                // For white background the formula simplifies:
                                // c' = (c*a + 255*(255-a)) / 255
                                byte r = (byte)((p.R * a + 255 * (255 - a)) / 255);
                                byte g = (byte)((p.G * a + 255 * (255 - a)) / 255);
                                byte b = (byte)((p.B * a + 255 * (255 - a)) / 255);
                                span[i] = new Bgra32(b, g, r, 255);
                            }
                        }
                    }
                    else
                    {
                        // Fallback (rare): create a new white canvas and draw original.
                        var flattened = new Image<Bgra32>(image.Width, image.Height, new Bgra32(255, 255, 255, 255));
                        flattened.Mutate(ctx => ctx.DrawImage(image, 1f));
                        image.Dispose();
                        // Replace reference (cannot reassign using because of scope, so just save flattened).
                        var tempFallback = Path.Combine(Path.GetTempPath(), $"answer-page-{oneBasedPageNumber}-{Guid.NewGuid():N}.jpg");
                        using var fsFallback = File.Create(tempFallback);
                        flattened.SaveAsJpeg(fsFallback, new JpegEncoder { Quality = jpegQuality });
                        return tempFallback;
                    }
                }

                var temp = Path.Combine(Path.GetTempPath(), $"answer-page-{oneBasedPageNumber}-{Guid.NewGuid():N}.jpg");
                using (var fs = File.Create(temp))
                {
                    image.SaveAsJpeg(fs, new JpegEncoder { Quality = jpegQuality });
                }

                return temp;
            }
            finally
            {
                try { RenderSemaphore.Release(); } catch { /* ignore */ }
            }
        }
    }
}