using FluentResults;
using MediatR;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Segments.Contracts;

namespace Recommendation.Api.Features.Segments.GetAllSegments;

public record GetAllSegmentsQuery(int Page, int PageSize) : IRequest<Result<PaginatedResult<CatalogSegmentDto>>>;
