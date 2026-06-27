using FluentValidation;
using Kombats.Bff.Api.Models.Requests;

namespace Kombats.Bff.Api.Validation;

public sealed class ChangeAvatarRequestValidator : AbstractValidator<ChangeAvatarRequest>
{
    public ChangeAvatarRequestValidator()
    {
        RuleFor(x => x.ExpectedRevision)
            .GreaterThanOrEqualTo(1).WithMessage("ExpectedRevision must be greater than or equal to 1.");

        RuleFor(x => x.AvatarId)
            .NotEmpty().WithMessage("AvatarId is required.")
            .MaximumLength(64).WithMessage("AvatarId must not exceed 64 characters.");
    }
}
