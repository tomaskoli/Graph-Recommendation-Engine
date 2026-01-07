using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Recommendation.Api.Infrastructure.Neo4j;

public interface INeo4jConnectionFactory
{
    IAsyncSession CreateSession();
}

public class Neo4jConnectionFactory : INeo4jConnectionFactory, IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly string _database;

    public Neo4jConnectionFactory(IOptions<Neo4jOptions> options)
    {
        var config = options.Value;
        _driver = GraphDatabase.Driver(config.Uri, AuthTokens.Basic(config.Username, config.Password));
        _database = config.Database;
    }

    public IAsyncSession CreateSession() => _driver.AsyncSession(o => o.WithDatabase(_database));

    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

