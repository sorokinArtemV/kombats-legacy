using FluentValidation;
using Kombats.Players.Api.Endpoints.Avatar;
using Kombats.Players.Domain.Entities;

namespace Kombats.Players.Api.Validators;

/// <summary>
/// Validator for ChangeAvatarRequest. Structural validation only; catalog membership
/// is checked in the domain (Character.ChangeAvatar) so the allowed set stays in one place.
/// </summary>
internal sealed class ChangeAvatarRequestValidator : AbstractValidator<ChangeAvatarRequest>
{
    public ChangeAvatarRequestValidator()
    {
        RuleFor(x => x.ExpectedRevision)
            .GreaterThanOrEqualTo(1)
            .WithMessage("ExpectedRevision must be greater than or equal to 1.");

        RuleFor(x => x.AvatarId)
            .NotEmpty()
            .WithMessage("AvatarId is required.")
            .MaximumLength(AvatarCatalog.MaxLength)
            .WithMessage($"AvatarId must be at most {AvatarCatalog.MaxLength} characters.");
    }
}
