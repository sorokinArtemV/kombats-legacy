using Kombats.Abstractions;

namespace Kombats.Players.Application.UseCases.AllocateStatPoints;

internal sealed record AllocateStatPointsCommand(
    Guid IdentityId,
    int ExpectedRevision,
    int Str,
    int Agi,
    int Intuition,
    int Vit) : ICommand<AllocateStatPointsResult>;
