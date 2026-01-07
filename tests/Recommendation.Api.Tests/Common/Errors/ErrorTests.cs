using Recommendation.Api.Common.Errors;

namespace Recommendation.Api.Tests.Common.Errors;

public class ErrorTests
{
    [Fact]
    public void NotFoundError_FormatsMessageCorrectly()
    {
        var error = new NotFoundError("Product", 123);

        Assert.Equal("Product with id '123' was not found.", error.Message);
    }

    [Fact]
    public void NotFoundError_StoresMetadata()
    {
        var error = new NotFoundError("Category", 456);

        Assert.Equal("Category", error.Metadata["EntityName"]);
        Assert.Equal(456, error.Metadata["EntityId"]);
    }

    [Fact]
    public void ValidationError_WithSingleMessage_FormatsCorrectly()
    {
        var error = new ValidationError("Invalid input");

        Assert.Equal("Invalid input", error.Message);
    }

    [Fact]
    public void ValidationError_WithMultipleMessages_JoinsWithSemicolon()
    {
        var errors = new[] { "Field A is required", "Field B must be positive" };

        var error = new ValidationError(errors);

        Assert.Equal("Field A is required; Field B must be positive", error.Message);
    }
}

