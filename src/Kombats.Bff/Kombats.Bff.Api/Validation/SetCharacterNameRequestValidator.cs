using FluentValidation;
using Kombats.Bff.Api.Models.Requests;

namespace Kombats.Bff.Api.Validation;

public sealed class SetCharacterNameRequestValidator : AbstractValidator<SetCharacterNameRequest>
{
    public SetCharacterNameRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(50).WithMessage("Name must not exceed 50 characters.");
    }
}
