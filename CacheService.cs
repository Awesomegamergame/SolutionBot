using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace SolutionBot
{
    internal static class CacheService
    {
        // Base directory for cache: <app>/cache
        public static string GetBaseCacheDir() => Path.Combine(AppContext.BaseDirectory, "cache");

        public static string Slugify(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "source";
            var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            var s = new string(chars).Trim('_');
            return string.IsNullOrWhiteSpace(s) ? "source" : s;
        }

        public static string GetSourceCacheDir(string sourceName)
        {
            var slug = Slugify(sourceName);
            return Path.Combine(GetBaseCacheDir(), slug);
        }

        public static string GetCachedImagePath(string sourceName, string normalizedProblem)
        {
            var dir = GetSourceCacheDir(sourceName);
            return Path.Combine(dir, $"{normalizedProblem}.jpg");
        }

        public static async Task BuildAllAsync(bool force = false, string? onlySource = null, int dpi = 110, int jpegQuality = 80, int maxW = 2000, int maxH = 2000)
        {
            var cfg = AnswerConfigProvider.Get();

            var sources = cfg.Sources.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(onlySource))
            {
                var key = onlySource.Trim();
                sources = sources.Where(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (!sources.Any())
                    throw new InvalidOperationException($"Source '{onlySource}' not found in sources.json.");
            }

            Directory.CreateDirectory(GetBaseCacheDir());

            foreach (var kv in sources)
            {
                var sourceName = kv.Key;
                var pdfPath = kv.Value;

                if (!File.Exists(pdfPath))
                {
                    Console.WriteLine($"[skip] '{sourceName}': PDF not found at {pdfPath}");
                    continue;
                }

                Console.WriteLine($"[scan] '{sourceName}' -> {pdfPath}");
                Dictionary<string, int> index;
                try
                {
                    index = await Task.Run(() => BuildProblemIndex(pdfPath));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[error] '{sourceName}': Failed to build index: {ex.Message}");
                    continue;
                }

                if (index.Count == 0)
                {
                    Console.WriteLine($"[warn] '{sourceName}': No problems detected.");
                    continue;
                }

                var outDir = GetSourceCacheDir(sourceName);
                Directory.CreateDirectory(outDir);

                int done = 0, skipped = 0, total = index.Count;
                foreach (var (problem, page) in index.OrderBy(kv2 => kv2.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var dest = Path.Combine(outDir, $"{problem}.jpg");
                    if (File.Exists(dest) && !force)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var tmp = PdfHelpers.RenderPageToJpeg(pdfPath, page, dpi: dpi, jpegQuality: jpegQuality);
                        File.Copy(tmp, dest, overwrite: true);
                        TryDelete(tmp);
                        done++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[error] '{sourceName}' {problem} (page {page}): {ex.Message}");
                    }
                }

                Console.WriteLine($"[done] '{sourceName}': rendered {done}/{total} problems (skipped {skipped}).");
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // Scans the PDF and returns mapping: normalizedProblem ("5-10") -> first 1-based page number where it appears
        private static Dictionary<string, int> BuildProblemIndex(string pdfPath)
        {
            var results = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using var doc = PdfDocument.Open(pdfPath);
            foreach (var page in doc.GetPages())
            {
                var text = page.Text ?? string.Empty;
                if (text.Length == 0) continue;

                foreach (var prob in ExtractProblems(text))
                {
                    if (!results.ContainsKey(prob))
                        results[prob] = page.Number; // 1-based
                }
            }

            return results;
        }

        private static IEnumerable<string> ExtractProblems(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;

            // IMPORTANT: Use en dash U+2013 specifically between chapter and problem.
            // Matches:
            // - Problem 5–10 / Problem5–10
            // - 5–10
            // Normalizes to "5-10" for cache keys and filenames.
            var pattern = @"(?ix)
                \b
                (?:Problem\s*)?
                (?<ch>\d+)
                \s*[\u2013]\s*
                (?<pr>\d+)
                \b";

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(text, pattern))
            {
                if (!m.Success) continue;
                var ch = m.Groups["ch"].Value;
                var pr = m.Groups["pr"].Value;
                if (string.IsNullOrEmpty(ch) || string.IsNullOrEmpty(pr)) continue;

                var normalized = $"{ch}-{pr}";
                if (seen.Add(normalized))
                    yield return normalized;
            }
        }
    }
}