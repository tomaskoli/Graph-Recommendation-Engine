using FluentResults;
using MediatR;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Categories.Contracts;

namespace Recommendation.Api.Features.Categories.GetCategoryHierarchy;

public record GetCategoryHierarchyQuery(int? CategoryId, int Page, int PageSize) : IRequest<Result<PaginatedResult<CategoryDto>>>;
