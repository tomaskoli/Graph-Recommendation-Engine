using FluentResults;
using MediatR;
using Recommendation.Api.Features.Search.Contracts;

namespace Recommendation.Api.Features.Search.GlobalSearch;

public record GlobalSearchQuery(string SearchTerm, int Limit = 5) : IRequest<Result<SearchResultDto>>;

