using System;
using System.IO;
using System.Linq;
using System.Threading;
using UglyToad.PdfPig;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Converters;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace DiscordBot
{
    internal static class PdfHelpers
    {
        // Throttle concurrent renders to reduce peak memory (default1)
        private static readonly SemaphoreSlim RenderSemaphore = new(1,1);

        // Find the first page number (1-based) that contains the given problem marker.
        // Tries multiple variants: "5-10", "5.10", and "Problem5-10".
        public static int? FindFirstMatchingPage(string pdfPath, string normalizedProblem)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF not found.", pdfPath);

            // Build search variants
            var variants = new[]
            {
                normalizedProblem,                        // e.g., "5-10"
                normalizedProblem.Replace('-', '.'),      // e.g., "5.10"
                $"Problem {normalizedProblem}",           // e.g., "Problem5-10"
                $"Problem {normalizedProblem.Replace('-', '.')}" // e.g., "Problem5.10"
            };

            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            foreach (var page in doc.GetPages())
            {
                var text = page.Text;
                if (string.IsNullOrEmpty(text)) continue;

                // Simple contains match, case-insensitive
                if (variants.Any(v => text.IndexOf(v, StringComparison.OrdinalIgnoreCase) >=0))
                {
                    return page.Number; // PdfPig uses1-based page numbers
                }
            }

            return null;
        }

        // Renders a single PDF page to a temporary JPEG and returns the path.
        // - Default lower dpi to reduce memory footprint
        // - Clamp to a maximum pixel size to avoid huge allocations
        public static string RenderPageToJpeg(string pdfPath, int oneBasedPageNumber, int dpi =110, int jpegQuality =80,
            int maxPixelsW =2000, int maxPixelsH =2000)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
                throw new ArgumentNullException(nameof(pdfPath));
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF not found.", pdfPath);
            if (oneBasedPageNumber <1)
                throw new ArgumentOutOfRangeException(nameof(oneBasedPageNumber));

            var pageIndex = oneBasedPageNumber -1;

            RenderSemaphore.Wait();
            try
            {
                // Measure page size in points
                double widthPoints, heightPoints;
                using (var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
                {
                    var page = pdf.GetPage(oneBasedPageNumber);
                    widthPoints = page.Width;
                    heightPoints = page.Height;
                }

                // Convert points to pixels at desired DPI
                var targetW = (int)Math.Round(widthPoints * dpi /72.0);
                var targetH = (int)Math.Round(heightPoints * dpi /72.0);

                // Clamp while preserving aspect ratio if needed
                (int pixelWidth, int pixelHeight) = ClampSize(targetW, targetH, maxPixelsW, maxPixelsH);

                using var lib = DocLib.Instance;
                using var docReader = lib.GetDocReader(pdfPath, new PageDimensions(pixelWidth, pixelHeight));

                if (pageIndex <0 || pageIndex >= docReader.GetPageCount())
                    throw new ArgumentOutOfRangeException(nameof(oneBasedPageNumber), "Page number exceeds document length.");

                using var pageReader = docReader.GetPageReader(pageIndex);

                var raw = pageReader.GetImage();
                var renderWidth = pageReader.GetPageWidth();
                var renderHeight = pageReader.GetPageHeight();

                // Convert raw BGRA bytes directly to ImageSharp image
                using var image = Image.LoadPixelData<Bgra32>(raw, renderWidth, renderHeight);

                // Ensure white background to avoid black/transparent artifacts
                image.Mutate(ctx =>
                {
                    ctx.BackgroundColor(Color.White);
                });

                // Write to a temporary file and return its path
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

        private static (int w, int h) ClampSize(int w, int h, int maxW, int maxH)
        {
            if (w <= maxW && h <= maxH) return (w, h);
            double rw = (double)maxW / w;
            double rh = (double)maxH / h;
            double r = Math.Min(rw, rh);
            return ((int)Math.Max(1, Math.Floor(w * r)), (int)Math.Max(1, Math.Floor(h * r)));
        }
    }
}