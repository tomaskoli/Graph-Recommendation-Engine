using FluentResults;
using MediatR;
using Recommendation.Api.Features.Recommendations.Contracts;

namespace Recommendation.Api.Features.Recommendations.GetRecommendations;

public record GetRecommendationsQuery(int ProductId, int Page, int PageSize) : IRequest<Result<RecommendationDto>>;
