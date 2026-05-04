using Kombats.Abstractions;
using Kombats.Chat.Application.Ports;

namespace Kombats.Chat.Application.UseCases.HandlePlayerProfileChanged;

/// <summary>
/// Updates the player info cache from the <c>PlayerCombatProfileChanged</c> integration event.
/// Maps the event's <c>IsReady</c> bool to the canonical <c>OnboardingState</c> string
/// ("Ready"/"NotReady") used by the eligibility model in Batch 2.
/// If <c>Name</c> is null or blank, the event is ignored: this is the pre-naming
/// EnsureCharacter event, which carries no usable info. Removing existing cache would be
/// destructive under MassTransit retry/redelivery reordering, where a retried early event
/// could wipe a valid later state (Character.Name is never reset to null in the domain).
/// </summary>
internal sealed class HandlePlayerProfileChangedHandler(IPlayerInfoCache cache)
    : ICommandHandler<HandlePlayerProfileChangedCommand>
{
    public async Task<Result> HandleAsync(
        HandlePlayerProfileChangedCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Success();
        }

        string onboardingState = command.IsReady ? "Ready" : "NotReady";
        var info = new CachedPlayerInfo(command.Name, onboardingState);

        await cache.SetAsync(command.IdentityId, info, cancellationToken);

        return Result.Success();
    }
}
