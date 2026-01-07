using FluentResults;

namespace Recommendation.Api.Common.Errors;

public class NotFoundError : Error
{
    public NotFoundError(string entityName, object id)
        : base($"{entityName} with id '{id}' was not found.")
    {
        Metadata.Add("EntityName", entityName);
        Metadata.Add("EntityId", id);
    }
}

