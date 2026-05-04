using FluentValidation;
using Kombats.Bff.Api.Models.Requests;

namespace Kombats.Bff.Api.Validation;

public sealed class AllocateStatsRequestValidator : AbstractValidator<AllocateStatsRequest>
{
    public AllocateStatsRequestValidator()
    {
        RuleFor(x => x.ExpectedRevision)
            .GreaterThanOrEqualTo(0).WithMessage("ExpectedRevision must be non-negative.");

        RuleFor(x => x.Strength)
            .GreaterThanOrEqualTo(0).WithMessage("Strength must be non-negative.");

        RuleFor(x => x.Agility)
            .GreaterThanOrEqualTo(0).WithMessage("Agility must be non-negative.");

        RuleFor(x => x.Intuition)
            .GreaterThanOrEqualTo(0).WithMessage("Intuition must be non-negative.");

        RuleFor(x => x.Vitality)
            .GreaterThanOrEqualTo(0).WithMessage("Vitality must be non-negative.");
    }
}
