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
        [SlashCommand("answer", "Find and send a JPG image of the page containing the given problem number (e.g., 5-10).")]
        public async Task AnswerAsync(InteractionContext ctx,
            [Option("problem", "Problem number, e.g., 5-10")] string problem)
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
                    .WithContent($"Couldn't find '{normalized}' in the PDF."));
                return;
            }

            // Render the single page to a temp JPEG and send it
            string? tempFile = null;
            try
            {
                tempFile = await Task.Run(() => PdfHelpers.RenderPageToJpeg(pdfPath, pageNumber.Value, jpegQuality: 85));
                await using var fs = File.OpenRead(tempFile);

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
                if (tempFile is not null && File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { /* ignore */ }
                }
            }
        }

        private static string? NormalizeProblem(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var trimmed = input.Trim();

            // Accept forms like "5-10", "5.10", "  5 - 10  "
            var m = Regex.Match(trimmed, @"^\s*(\d+)\s*[-\.]\s*(\d+)\s*$");
            if (!m.Success) return null;
            return $"{m.Groups[1].Value}–{m.Groups[2].Value}";
        }
    }
}