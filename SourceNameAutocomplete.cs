using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;

namespace DiscordBot
{
    // Suggests up to 25 source names from sources.json while typing
    internal sealed class SourceNameAutocomplete : IAutocompleteProvider
    {
        public Task<IEnumerable<DiscordAutoCompleteChoice>> Provider(AutocompleteContext ctx)
        {
            AnswerConfig cfg;
            try
            {
                cfg = AnswerConfigProvider.Get();
            }
            catch
            {
                return Task.FromResult(Enumerable.Empty<DiscordAutoCompleteChoice>());
            }

            var query = ctx.FocusedOption?.Value?.ToString()?.Trim() ?? string.Empty;

            IEnumerable<string> names = cfg.Sources.Keys;
            if (!string.IsNullOrEmpty(query))
                names = names.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase));

            var ordered = names
                .OrderBy(n => n.Equals(cfg.DefaultSource, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .Select(n => new DiscordAutoCompleteChoice(n, n));

            return Task.FromResult<IEnumerable<DiscordAutoCompleteChoice>>(ordered);
        }
    }
}