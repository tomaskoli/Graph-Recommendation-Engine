using FluentResults;
using MediatR;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Products.Contracts;

namespace Recommendation.Api.Features.Categories.GetProductsByCategory;

public record GetProductsByCategoryQuery(int CategoryId, int Page, int PageSize) : IRequest<Result<PaginatedResult<ProductDto>>>;
