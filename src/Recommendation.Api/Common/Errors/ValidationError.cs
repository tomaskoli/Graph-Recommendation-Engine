using FluentResults;

namespace Recommendation.Api.Common.Errors;

public class ValidationError : Error
{
    public ValidationError(string message) : base(message)
    {
    }

    public ValidationError(IEnumerable<string> errors)
        : base(string.Join("; ", errors))
    {
    }
}

