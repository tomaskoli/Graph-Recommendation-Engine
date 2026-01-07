using FluentResults;
using MediatR;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Products.Contracts;

namespace Recommendation.Api.Features.Products.GetRelatedProducts;

public record GetRelatedProductsQuery(int ProductId, int Page, int PageSize) : IRequest<Result<PaginatedResult<ProductDto>>>;
