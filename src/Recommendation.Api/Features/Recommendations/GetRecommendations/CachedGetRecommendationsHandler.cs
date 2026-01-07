using FluentResults;
using MediatR;
using Microsoft.Extensions.Options;
using Recommendation.Api.Features.Recommendations.Contracts;
using Recommendation.Api.Infrastructure.Caching;

namespace Recommendation.Api.Features.Recommendations.GetRecommendations;

/// <summary>
/// Decorator that adds Redis caching to recommendation queries.
/// Wraps the actual handler and checks cache before hitting Neo4j.
/// </summary>
public class CachedGetRecommendationsHandler : IRequestHandler<GetRecommendationsQuery, Result<RecommendationDto>>
{
    private readonly IRequestHandler<GetRecommendationsQuery, Result<RecommendationDto>> _innerHandler;
    private readonly ICacheService _cache;
    private readonly CachingOptions _options;
    private readonly ILogger<CachedGetRecommendationsHandler> _logger;

    public CachedGetRecommendationsHandler(
        IRequestHandler<GetRecommendationsQuery, Result<RecommendationDto>> innerHandler,
        ICacheService cache,
        IOptions<CachingOptions> options,
        ILogger<CachedGetRecommendationsHandler> logger)
    {
        _innerHandler = innerHandler;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<RecommendationDto>> Handle(GetRecommendationsQuery request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await _innerHandler.Handle(request, cancellationToken);
        }

        var cacheKey = CacheKeys.Recommendations(request.ProductId, request.Page, request.PageSize);

        // Try cache first
        var cached = await _cache.GetAsync<RecommendationDto>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug("Cache HIT for recommendations: {CacheKey}", cacheKey);
            return Result.Ok(cached);
        }

        _logger.LogDebug("Cache MISS for recommendations: {CacheKey}", cacheKey);

        // Query Neo4j
        var result = await _innerHandler.Handle(request, cancellationToken);

        // Cache successful results
        if (result.IsSuccess)
        {
            var ttl = TimeSpan.FromMinutes(_options.RecommendationsTtlMinutes);
            await _cache.SetAsync(cacheKey, result.Value, ttl, cancellationToken);
        }

        return result;
    }
}

