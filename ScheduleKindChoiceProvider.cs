using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;

namespace SolutionBot
{
    internal sealed class ScheduleKindChoiceProvider : IChoiceProvider
    {
        private static readonly IReadOnlyList<DiscordApplicationCommandOptionChoice> Choices =
        [
            new DiscordApplicationCommandOptionChoice("Test", "test"),
            new DiscordApplicationCommandOptionChoice("Quiz", "quiz")
        ];

        public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter) =>
            ValueTask.FromResult<IEnumerable<DiscordApplicationCommandOptionChoice>>(Choices);
    }
}