using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Kombats.Bff.Api.Models.Requests;
using Kombats.Bff.Api.Validation;
using Kombats.Bff.Application.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace Kombats.Bff.Api.Tests;

public sealed class ValidationFilterTests
{
    [Fact]
    public async Task ValidRequest_CallsNext()
    {
        var validator = Substitute.For<IValidator<SetCharacterNameRequest>>();
        validator.ValidateAsync(Arg.Any<SetCharacterNameRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var filter = new ValidationFilter<SetCharacterNameRequest>(validator);
        var request = new SetCharacterNameRequest("ValidName");

        bool nextCalled = false;
        var context = CreateContext(request);
        object? result = await filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidRequest_ReturnsBadRequest()
    {
        var validator = Substitute.For<IValidator<SetCharacterNameRequest>>();
        validator.ValidateAsync(Arg.Any<SetCharacterNameRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[]
            {
                new ValidationFailure("Name", "Name is required.")
            }));

        var filter = new ValidationFilter<SetCharacterNameRequest>(validator);
        var request = new SetCharacterNameRequest("");

        var context = CreateContext(request);
        object? result = await filter.InvokeAsync(context, _ =>
            ValueTask.FromResult<object?>(Results.Ok()));

        result.Should().BeAssignableTo<IResult>();
    }

    private static EndpointFilterInvocationContext CreateContext(object argument)
    {
        var httpContext = new DefaultHttpContext();
        return new DefaultEndpointFilterInvocationContext(httpContext, argument);
    }
}

public sealed class SetCharacterNameRequestValidatorTests
{
    private readonly SetCharacterNameRequestValidator _validator = new();

    [Fact]
    public void ValidName_Passes()
    {
        var result = _validator.Validate(new SetCharacterNameRequest("ValidName"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmptyName_Fails()
    {
        var result = _validator.Validate(new SetCharacterNameRequest(""));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void NameTooLong_Fails()
    {
        var result = _validator.Validate(new SetCharacterNameRequest(new string('a', 51)));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void NameAtMaxLength_Passes()
    {
        var result = _validator.Validate(new SetCharacterNameRequest(new string('a', 50)));
        result.IsValid.Should().BeTrue();
    }
}

public sealed class AllocateStatsRequestValidatorTests
{
    private readonly AllocateStatsRequestValidator _validator = new();

    [Fact]
    public void ValidRequest_Passes()
    {
        var result = _validator.Validate(new AllocateStatsRequest(1, 5, 3, 2, 4));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ZeroValues_Passes()
    {
        var result = _validator.Validate(new AllocateStatsRequest(0, 0, 0, 0, 0));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void NegativeExpectedRevision_Fails()
    {
        var result = _validator.Validate(new AllocateStatsRequest(-1, 1, 1, 1, 1));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ExpectedRevision");
    }

    [Fact]
    public void NegativeStrength_Fails()
    {
        var result = _validator.Validate(new AllocateStatsRequest(0, -1, 0, 0, 0));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Strength");
    }

    [Fact]
    public void NegativeAgility_Fails()
    {
        var result = _validator.Validate(new AllocateStatsRequest(0, 0, -1, 0, 0));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Agility");
    }

    [Fact]
    public void NegativeIntuition_Fails()
    {
        var result = _validator.Validate(new AllocateStatsRequest(0, 0, 0, -1, 0));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Intuition");
    }

    [Fact]
    public void NegativeVitality_Fails()
    {
        var result = _validator.Validate(new AllocateStatsRequest(0, 0, 0, 0, -1));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Vitality");
    }
}
