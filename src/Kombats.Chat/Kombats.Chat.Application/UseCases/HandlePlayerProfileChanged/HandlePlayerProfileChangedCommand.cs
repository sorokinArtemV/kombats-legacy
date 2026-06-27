using Kombats.Abstractions;

namespace Kombats.Chat.Application.UseCases.HandlePlayerProfileChanged;

internal sealed record HandlePlayerProfileChangedCommand(
    Guid IdentityId,
    string? Name,
    bool IsReady) : ICommand;
