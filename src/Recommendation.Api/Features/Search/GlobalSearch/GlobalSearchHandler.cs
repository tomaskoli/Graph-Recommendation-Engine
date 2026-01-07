using FluentResults;
using MediatR;
using Neo4j.Driver;
using Recommendation.Api.Features.Search.Contracts;
using Recommendation.Api.Infrastructure.Neo4j;

namespace Recommendation.Api.Features.Search.GlobalSearch;

public class GlobalSearchHandler(INeo4jConnectionFactory connectionFactory) 
    : IRequestHandler<GlobalSearchQuery, Result<SearchResultDto>>
{
    public async Task<Result<SearchResultDto>> Handle(GlobalSearchQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SearchTerm) || request.SearchTerm.Length < 2)
        {
            return Result.Ok(new SearchResultDto([], [], []));
        }

        await using var session = connectionFactory.CreateSession();

        var searchPattern = $"(?i).*{EscapeRegex(request.SearchTerm)}.*";

        const string query = """
            // Search Products
            OPTIONAL MATCH (p:Product)
            WHERE p.productName =~ $searchPattern
            OPTIONAL MATCH (p)-[:MADE_BY]->(pb:Brand)
            WITH collect(DISTINCT {
                productId: p.productId, 
                productName: p.productName, 
                brandName: pb.name
            })[0..$limit] AS products

            // Search Categories
            OPTIONAL MATCH (c:Category)
            WHERE c.categoryName =~ $searchPattern
            OPTIONAL MATCH (c)-[:CHILD_OF]->(parent:Category)
            WITH products, collect(DISTINCT {
                categoryId: c.categoryId, 
                categoryName: c.categoryName, 
                parentCategoryName: parent.categoryName
            })[0..$limit] AS categories

            // Search Brands
            OPTIONAL MATCH (b:Brand)
            WHERE b.name =~ $searchPattern
            OPTIONAL MATCH (bp:Product)-[:MADE_BY]->(b)
            WITH products, categories, b, count(bp) AS productCount
            ORDER BY productCount DESC
            WITH products, categories, collect(DISTINCT {
                brandId: b.brandId, 
                brandName: b.name, 
                productCount: productCount
            })[0..$limit] AS brands

            RETURN products, categories, brands
            """;

        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(query, new 
            { 
                searchPattern, 
                limit = request.Limit 
            });
            return await cursor.SingleAsync(cancellationToken);
        });

        var products = result["products"].As<List<IDictionary<string, object>>>()
            .Where(p => p["productId"] != null)
            .Select(p => new SearchProductResultDto(
                p["productId"].As<int>(),
                p["productName"].As<string>(),
                p["brandName"].As<string?>()))
            .ToList();

        var categories = result["categories"].As<List<IDictionary<string, object>>>()
            .Where(c => c["categoryId"] != null)
            .Select(c => new SearchCategoryResultDto(
                c["categoryId"].As<int>(),
                c["categoryName"].As<string>(),
                c["parentCategoryName"].As<string?>()))
            .ToList();

        var brands = result["brands"].As<List<IDictionary<string, object>>>()
            .Where(b => b["brandId"] != null)
            .Select(b => new SearchBrandResultDto(
                b["brandId"].As<int>(),
                b["brandName"].As<string>(),
                b["productCount"].As<int>()))
            .ToList();

        return Result.Ok(new SearchResultDto(products, categories, brands));
    }

    private static string EscapeRegex(string input)
    {
        return System.Text.RegularExpressions.Regex.Escape(input);
    }
}

