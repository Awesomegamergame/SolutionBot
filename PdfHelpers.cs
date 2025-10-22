using System;
using System.IO;
using System.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using UglyToad.PdfPig;

namespace DiscordBot
{
    internal static class PdfHelpers
    {
        // Find the first page number (1-based) that contains the given problem marker.
        // Tries multiple variants: "5-10", "5.10", and "Problem 5-10".
        public static int? FindFirstMatchingPage(string pdfPath, string normalizedProblem)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF not found.", pdfPath);

            // Build search variants
            var variants = new[]
            {
                normalizedProblem,                        // e.g., "5-10"
                normalizedProblem.Replace('-', '.'),      // e.g., "5.10"
                $"Problem {normalizedProblem}",           // e.g., "Problem 5-10"
                $"Problem {normalizedProblem.Replace('-', '.')}" // e.g., "Problem 5.10"
            };

            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            foreach (var page in doc.GetPages())
            {
                var text = page.Text;
                if (string.IsNullOrEmpty(text)) continue;

                // Simple contains match, case-insensitive
                if (variants.Any(v => text.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return page.Number; // PdfPig uses 1-based page numbers
                }
            }

            return null;
        }

        // Extracts a single page into a temporary PDF file and returns the path.
        public static string ExtractSinglePage(string pdfPath, int oneBasedPageNumber)
        {
            if (oneBasedPageNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(oneBasedPageNumber));

            using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            var pageIndex = oneBasedPageNumber - 1;
            if (pageIndex >= input.PageCount)
                throw new ArgumentOutOfRangeException(nameof(oneBasedPageNumber), "Page number exceeds document length.");

            using var output = new PdfSharpCore.Pdf.PdfDocument
            {
                Version = input.Version
            };
            output.Info.Title = $"Extracted page {oneBasedPageNumber}";
            output.Info.Creator = "DiscordBot";

            output.AddPage(input.Pages[pageIndex]);

            var temp = Path.Combine(Path.GetTempPath(), $"answer-page-{oneBasedPageNumber}-{Guid.NewGuid():N}.pdf");
            output.Save(temp);
            return temp;
        }
    }
}