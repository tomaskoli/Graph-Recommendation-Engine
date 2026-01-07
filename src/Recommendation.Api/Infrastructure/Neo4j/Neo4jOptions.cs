namespace Recommendation.Api.Infrastructure.Neo4j;

public class Neo4jOptions
{
    public const string SECTION_NAME = "Neo4j";

    public string Uri { get; set; } = "neo4j://localhost:7687";
    public string Username { get; set; } = "neo4j";
    public string Password { get; set; } = "12345678";
    public string Database { get; set; } = "recommendation";
}

