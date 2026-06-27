using FluentValidation;

namespace Kombats.Players.Api.Validators;

internal sealed class SetCharacterNameRequestValidator : AbstractValidator<Endpoints.CharacterName.SetCharacterNameRequest>
{
    public SetCharacterNameRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotNull()
            .WithMessage("Name is required.")
            .Must(n => n != null && n.Trim().Length >= 3 && n.Trim().Length <= 16)
            .WithMessage("Name must be between 3 and 16 characters after trimming.");
    }
}
