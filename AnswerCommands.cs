using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace DiscordBot
{
    public sealed class AnswerCommands : ApplicationCommandModule
    {
        [SlashCommand("answer", "Find and send a JPG image of the page containing the given problem number (e.g.,5-10).")]
        public async Task AnswerAsync(InteractionContext ctx,
            [Option("problem", "Problem number, e.g.,5-10")] string problem)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

            var pdfPath = Environment.GetEnvironmentVariable("ANSWER_PDF_PATH");
            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                pdfPath = Path.Combine(AppContext.BaseDirectory, "answers.pdf");
            }

            if (!File.Exists(pdfPath))
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Answers PDF not found at: {pdfPath}"));
                return;
            }

            // Normalize the input (e.g., trim, unify separators)
            var normalized = NormalizeProblem(problem);
            if (normalized is null)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent("Invalid problem format. Use something like5-10 or5.10."));
                return;
            }

            int? pageNumber;
            try
            {
                pageNumber = await Task.Run(() => PdfHelpers.FindFirstMatchingPage(pdfPath, normalized));
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Error while searching the PDF: {ex.Message}"));
                return;
            }

            if (pageNumber is null)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Couldn't find '{normalized}' in the PDF."));
                return;
            }

            // Read optional tuning from environment
            int dpi = GetEnvInt("ANSWER_DPI", 110, 50, 300);
            int jpegQ = GetEnvInt("ANSWER_JPEG_QUALITY", 80, 30, 95);
            int maxW = GetEnvInt("ANSWER_MAX_W", 2000, 600, 4000);
            int maxH = GetEnvInt("ANSWER_MAX_H", 2000, 600, 4000);

            string? outFile = null;
            try
            {
                outFile = await Task.Run(() => PdfHelpers.RenderPageToJpeg(pdfPath, pageNumber.Value, dpi, jpegQ, maxW, maxH));
                await using var fs = File.OpenRead(outFile);

                var webhook = new DiscordWebhookBuilder()
                    .WithContent($"Answer page for {normalized} (page {pageNumber.Value}).")
                    .AddFile($"answer-{normalized}.jpg", fs);

                await ctx.EditResponseAsync(webhook);
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Failed to render/send the page image: {ex.Message}"));
            }
            finally
            {
                // Encourage GC to reclaim large arrays/buffers after image processing
                try
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                }
                catch { /* ignore */ }
            }
        }

        private static int GetEnvInt(string name, int def, int min, int max)
        {
            var s = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(s) && int.TryParse(s, out var v))
            {
                if (v < min) v = min;
                if (v > max) v = max;
                return v;
            }
            return def;
        }

        private static string? NormalizeProblem(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var trimmed = input.Trim();
            var m = Regex.Match(trimmed, "^\\s*(\\d+)\\s*[\\p{Pd}\\.]\\s*(\\d+)\\s*$");
            if (!m.Success) return null;
            return $"{m.Groups[1].Value}–{m.Groups[2].Value}";
        }
    }
}