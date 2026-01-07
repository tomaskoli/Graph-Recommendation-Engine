using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Products.Contracts;
using Recommendation.Api.Features.Recommendations.Contracts;
using Recommendation.Api.Features.Recommendations.GetRecommendations;
using Recommendation.Api.Infrastructure.Caching;

namespace Recommendation.Api.Tests.Features.Recommendations;

public class CachedGetRecommendationsHandlerTests
{
    private readonly IRequestHandler<GetRecommendationsQuery, Result<RecommendationDto>> _innerHandler;
    private readonly ICacheService _cacheService;
    private readonly ILogger<CachedGetRecommendationsHandler> _logger;
    private readonly CachedGetRecommendationsHandler _sut;

    public CachedGetRecommendationsHandlerTests()
    {
        _innerHandler = Substitute.For<IRequestHandler<GetRecommendationsQuery, Result<RecommendationDto>>>();
        _cacheService = Substitute.For<ICacheService>();
        _logger = Substitute.For<ILogger<CachedGetRecommendationsHandler>>();

        var options = Options.Create(new CachingOptions { Enabled = true, RecommendationsTtlMinutes = 30 });

        _sut = new CachedGetRecommendationsHandler(_innerHandler, _cacheService, options, _logger);
    }

    [Fact]
    public async Task Handle_CacheHit_ReturnsFromCache()
    {
        var query = new GetRecommendationsQuery(123, 1, 10);
        var cachedDto = CreateRecommendationDto();
        _cacheService.GetAsync<RecommendationDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cachedDto);

        var result = await _sut.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(cachedDto, result.Value);
        await _innerHandler.DidNotReceive().Handle(Arg.Any<GetRecommendationsQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheMiss_QueriesInnerHandler()
    {
        var query = new GetRecommendationsQuery(123, 1, 10);
        var dto = CreateRecommendationDto();
        _cacheService.GetAsync<RecommendationDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RecommendationDto?)null);
        _innerHandler.Handle(query, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(dto));

        var result = await _sut.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _innerHandler.Received(1).Handle(query, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheMiss_StoresResultInCache()
    {
        var query = new GetRecommendationsQuery(123, 1, 10);
        var dto = CreateRecommendationDto();
        _cacheService.GetAsync<RecommendationDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RecommendationDto?)null);
        _innerHandler.Handle(query, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(dto));

        await _sut.Handle(query, CancellationToken.None);

        await _cacheService.Received(1).SetAsync(
            Arg.Is<string>(k => k == CacheKeys.Recommendations(123, 1, 10)),
            dto,
            TimeSpan.FromMinutes(30),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCachingDisabled_BypassesCache()
    {
        var options = Options.Create(new CachingOptions { Enabled = false });
        var sut = new CachedGetRecommendationsHandler(_innerHandler, _cacheService, options, _logger);
        var query = new GetRecommendationsQuery(123, 1, 10);
        var dto = CreateRecommendationDto();
        _innerHandler.Handle(query, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(dto));

        await sut.Handle(query, CancellationToken.None);

        await _cacheService.DidNotReceive().GetAsync<RecommendationDto>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _innerHandler.Received(1).Handle(query, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InnerHandlerFails_DoesNotCacheResult()
    {
        var query = new GetRecommendationsQuery(123, 1, 10);
        _cacheService.GetAsync<RecommendationDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RecommendationDto?)null);
        _innerHandler.Handle(query, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<RecommendationDto>("Database error"));

        var result = await _sut.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailed);
        await _cacheService.DidNotReceive().SetAsync(
            Arg.Any<string>(),
            Arg.Any<RecommendationDto>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    private static RecommendationDto CreateRecommendationDto()
    {
        var items = new List<ScoredProductDto>
        {
            new(1, "Product 1", "Description", 1, "Brand", 0.95, false)
        };
        var paginatedResult = PaginatedResult.Create<ScoredProductDto>(items, 1, 1, 10);
        return new RecommendationDto(paginatedResult);
    }
}

