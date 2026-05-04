using FluentValidation;
using Kombats.Players.Api.Endpoints.AllocateStatPoints;

namespace Kombats.Players.Api.Validators;

/// <summary>
/// Validator for AllocateStatPointsRequest. Validates request shape only (no business logic).
/// </summary>
internal sealed class AllocateStatPointsRequestValidator : AbstractValidator<AllocateStatPointsRequest>
{
    public AllocateStatPointsRequestValidator()
    {
        RuleFor(x => x.ExpectedRevision)
            .GreaterThanOrEqualTo(1)
            .WithMessage("ExpectedRevision must be greater than or equal to 1.");

        RuleFor(x => x.Str)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Strength points must be greater than or equal to zero.");

        RuleFor(x => x.Agi)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Agility points must be greater than or equal to zero.");

        RuleFor(x => x.Intuition)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Intuition points must be greater than or equal to zero.");

        RuleFor(x => x.Vit)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Vitality points must be greater than or equal to zero.");

        RuleFor(x => x.Str + x.Agi + x.Intuition + x.Vit)
            .GreaterThan(0)
            .WithMessage("At least one stat point must be allocated.");
    }
}

