using FluentResults;
using MediatR;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Categories.Contracts;

namespace Recommendation.Api.Features.Segments.GetCategoriesBySegment;

public record GetCategoriesBySegmentQuery(int SegmentId, int Page, int PageSize) : IRequest<Result<PaginatedResult<CategoryDto>>>;
