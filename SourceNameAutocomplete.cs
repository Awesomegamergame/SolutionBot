using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;

namespace SolutionBot
{
    // Suggests up to 25 source names from sources.json while typing
    internal sealed class SourceNameAutoComplete : IAutoCompleteProvider
    {
        public ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
        {
            AnswerConfig cfg;
            try
            {
                cfg = AnswerConfigProvider.Get();
            }
            catch
            {
                return ValueTask.FromResult<IEnumerable<DiscordAutoCompleteChoice>>(Array.Empty<DiscordAutoCompleteChoice>());
            }

            var query = context.UserInput?.Trim() ?? string.Empty;

            IEnumerable<string> names = cfg.Sources.Keys;
            if (!string.IsNullOrEmpty(query))
                names = names.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase));

            var ordered = names
                .OrderBy(n => n.Equals(cfg.DefaultSource, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .Select(n => new DiscordAutoCompleteChoice(n, n));

            return ValueTask.FromResult<IEnumerable<DiscordAutoCompleteChoice>>(ordered);
        }
    }
}