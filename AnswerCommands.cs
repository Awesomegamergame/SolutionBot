using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;

namespace DiscordBot
{
    public sealed class AnswerCommands : ApplicationCommandModule
    {
        [SlashCommand("answer", "Find and send a JPG image of the page containing the given problem number (e.g., 5-10).")]
        public async Task AnswerAsync(
            InteractionContext ctx,
            [Option("problem", "Problem number, e.g., 5-10")] string problem,
            [Option("source", "Optional source name from sources.json; defaults to the config default")]
            [Autocomplete(typeof(SourceNameAutocomplete))] string? source = null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

            // Load configuration
            AnswerConfig cfg;
            try
            {
                cfg = AnswerConfigProvider.Get();
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Failed to load sources.json: {ex.Message}"));
                return;
            }

            // Resolve source and PDF path
            var sourceName = string.IsNullOrWhiteSpace(source) ? cfg.DefaultSource : source.Trim();
            if (!cfg.Sources.TryGetValue(sourceName, out var pdfPath) || string.IsNullOrWhiteSpace(pdfPath))
            {
                var available = string.Join(", ", cfg.Sources.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Unknown source '{sourceName}'. Available: {available}"));
                return;
            }

            if (!File.Exists(pdfPath))
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"PDF not found for '{sourceName}' at: {pdfPath}"));
                return;
            }

            var normalized = NormalizeProblem(problem);
            if (normalized is null)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent("Invalid problem format. Use something like 5-10 or 5.10."));
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
                    .WithContent($"Couldn't find '{normalized}' in '{sourceName}'."));
                return;
            }

            try
            {
                var outFile = await Task.Run(() => PdfHelpers.RenderPageToJpeg(pdfPath, pageNumber.Value));
                await using var fs = File.OpenRead(outFile);

                var srcSlug = string.Concat(sourceName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')).Trim('_');

                var webhook = new DiscordWebhookBuilder()
                    .WithContent($"Answer page for {normalized} (page {pageNumber.Value}) from '{sourceName}'.")
                    .AddFile($"answer-{normalized}-{srcSlug}.jpg", fs);

                await ctx.EditResponseAsync(webhook);
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Failed to render/send the page image: {ex.Message}"));
            }
            finally
            {
                try
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                }
                catch { }
            }
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