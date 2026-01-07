using FluentResults;
using MediatR;
using Recommendation.Api.Features.Categories.GetCategoryHierarchy;
using Recommendation.Api.Features.Categories.GetProductsByCategory;
using Recommendation.Api.Features.Products.GetProductById;
using Recommendation.Api.Features.Products.GetRelatedProducts;
using Recommendation.Api.Features.Recommendations.Contracts;
using Recommendation.Api.Features.Recommendations.GetRecommendations;
using Recommendation.Api.Features.Segments.GetAllSegments;
using Recommendation.Api.Features.Search.GlobalSearch;
using Recommendation.Api.Features.Segments.GetCategoriesBySegment;
using Recommendation.Api.Infrastructure.Caching;
using Recommendation.Api.Infrastructure.Neo4j;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Neo4j
builder.Services.Configure<Neo4jOptions>(
    builder.Configuration.GetSection(Neo4jOptions.SECTION_NAME));
builder.Services.AddSingleton<INeo4jConnectionFactory, Neo4jConnectionFactory>();

// Redis caching
builder.Services.Configure<CachingOptions>(
    builder.Configuration.GetSection(CachingOptions.SectionName));

var redisConnection = builder.Configuration.GetConnectionString("Redis");
var redisEnabled = !string.IsNullOrEmpty(redisConnection);

if (redisEnabled)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect(redisConnection!));
    builder.Services.AddSingleton<ICacheService, RedisCacheService>();
}

// Exclude decorator class CachedGetRecommendationsHandler and register only when Redis is available
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.TypeEvaluator = type => type != typeof(CachedGetRecommendationsHandler);
});

if (redisEnabled)
{
    builder.Services.Decorate<IRequestHandler<GetRecommendationsQuery, Result<RecommendationDto>>, CachedGetRecommendationsHandler>();
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Recommendation API v1"));
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors();

app.MapDefaultEndpoints();

app.MapGetProductByIdEndpoint();
app.MapGetRelatedProductsEndpoint();
app.MapGetCategoryHierarchyEndpoint();
app.MapGetProductsByCategoryEndpoint();
app.MapGetRecommendationsEndpoint();
app.MapGetAllSegmentsEndpoint();
app.MapGetCategoriesBySegmentEndpoint();
app.MapGlobalSearchEndpoint();

app.Run();
