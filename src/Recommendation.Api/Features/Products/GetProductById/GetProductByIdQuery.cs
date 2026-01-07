using FluentResults;
using MediatR;
using Recommendation.Api.Features.Products.Contracts;

namespace Recommendation.Api.Features.Products.GetProductById;

public record GetProductByIdQuery(int ProductId) : IRequest<Result<ProductDetailDto>>;

