namespace Recommendation.Api.Features.Products.Contracts;

public record ParameterDto(
    int ParameterId,
    string ParameterName,
    string Value);

