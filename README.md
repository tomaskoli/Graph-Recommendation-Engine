# Graph Recommendation Engine

**Product recommendation** app using Neo4j Graph Data Science (GDS).

## Tech Stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 10, ASP.NET Core Minimal APIs |
| Orchestration | .NET Aspire 13.1 |
| Graph Database | Neo4j 2025.10.1 + GDS Plugin |
| Cache | Redis 8 |
| Frontend | React 18 + Vite |
| Containerization | Docker |

## Features

- **Graph-Based Recommendations** - ML-powered similarity using Neo4j GDS (FastRP + kNN)
- **Vertical Slice Architecture** - Feature-based organization with MediatR
- **.NET Aspire** - Cloud-ready orchestration with service discovery
- **Caching** - Redis caching for recommendation results
- **Category Hierarchy** - Recursive category tree traversal via Cypher
- **React** - Frontend displays products and recommendations

## Architecture

```
┌─────────────────┐                              ┌─────────────────┐
│      React      │                              │     Neo4j       │
│      (UI)       │◀────────────────────────────▶│   + GDS         │
└─────────────────┘                              └─────────────────┘
        │                                                ▲
        │                                                │
        ▼                                                │
┌─────────────────┐                                      │
│ Recommendation  │──────────────────────────────────────┘
│      API        │
└─────────────────┘
        │
        ▼
┌─────────────────┐
│     Redis       │
│    (Cache)      │
└─────────────────┘
```

## Design Patterns

| Pattern | Where Used | Purpose |
|---------|------------|---------|
| CQRS | `Commands/` and `Queries/` | Separate read/write models |
| Mediator | MediatR handlers | Decouple request/response |
| Decorator | `CachedGetRecommendationsHandler` | Transparent caching layer |
| Result Pattern | FluentResults | Explicit error handling |
| Vertical Slice | `Features/` folders | Feature-based organization |

## Project Structure

```
Graph-Recommendation-Engine/
├── src/
│   ├── Recommendation.AppHost/       # .NET Aspire orchestrator
│   ├── Recommendation.Api/           # REST API for recommendations
│   │   ├── Features/
│   │   │   ├── Categories/           # Category hierarchy endpoints
│   │   │   ├── Products/             # Product detail & related
│   │   │   ├── Recommendations/      # ML-based recommendations
│   │   │   ├── Search/               # Global search autocomplete
│   │   │   └── Segments/             # Catalog segments
│   │   ├── Common/                   # Shared contracts, errors
│   │   └── Infrastructure/           # Neo4j, Redis clients
│   ├── Recommendation.ServiceDefaults/ # Aspire defaults
│   └── Recommendation.Web/           # React frontend
├── deploy/
│   ├── Docker/                       # Docker Compose files
│   └── scripts/                      # Cypher scripts (GDS similarity)
└── doc/                              # Documentation
```

## NuGet Packages

### API
| Package | Version | Purpose |
|---------|---------|---------|
| MediatR | 14.0.0 | CQRS in-process messaging |
| FluentResults | 4.0.0 | Result pattern |
| FluentValidation | 12.1.1 | Request validation |
| Neo4j.Driver | 5.28.4 | Neo4j client |
| StackExchange.Redis | 2.10.1 | Redis client |
| Scrutor | 7.0.0 | Decorator registration |
| Swashbuckle.AspNetCore | 10.1.0 | Swagger/OpenAPI |

### Aspire
| Package | Version | Purpose |
|---------|---------|---------|
| Aspire.AppHost.Sdk | 13.1.0 | Orchestration SDK |
| Aspire.Hosting.Redis | 13.1.0 | Redis resource |

## API Endpoints

### Segments (`/api/segments`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/` | List catalog segments |
| GET | `/{id}/categories` | Categories in segment (hierarchical) |

### Categories (`/api/categories`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/` | Root categories with hierarchy |
| GET | `/{id}` | Category by ID with subtree |
| GET | `/{id}/products` | Products in category |

### Products (`/api/products`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/{id}` | Product details with parameters |
| GET | `/{id}/related` | Related products (category-based) |

### Recommendations (`/api/recommendations`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/?productId={id}` | ML-based similar products (GDS) |

### Search (`/api/search`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/?q={term}&limit={n}` | Global search (products, categories, brands) |

## Caching

- **TTL:** 30 minutes (configurable)
- **Cache Key Pattern:** `recs:{productId}:{page}:{pageSize}`

## Getting Started

### Prerequisites

- .NET 10 SDK
- Docker & Docker Compose
- Node.js 18+ (for React frontend)

### Run Infrastructure

```bash
# Start Neo4j and Redis
docker-compose -f deploy/Docker/docker-compose.neo4j.yml up -d
docker-compose -f deploy/Docker/docker-compose.redis.yml up -d
```

### Initialize Neo4j

```bash
# Seed data (in Neo4j Browser)
# File: deploy/scripts/seed-neo4j.cypher

# Run GDS similarity computation (in Neo4j Browser)
# File: deploy/scripts/compute-similarity-embeddings.cypher
```

### Run Application

```bash
# With Aspire (orchestrates all services)
dotnet run --project src/Recommendation.AppHost

# Or run API individually
dotnet run --project src/Recommendation.Api

# React frontend
cd src/Recommendation.Web
npm install
npm run dev

# Or start in Docker
docker-compose -f deploy/Docker/docker-compose.services.yml up -d
```

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ConnectionStrings__Redis` | Redis connection | `localhost:6379` |
| `Neo4j__Uri` | Neo4j bolt URI | `neo4j://localhost:7687` |
| `Neo4j__Username` | Neo4j username | `neo4j` |
| `Neo4j__Password` | Neo4j password | - |
| `Neo4j__Database` | Neo4j database name | `recommendation` |
| `Caching__Enabled` | Enable Redis caching | `true` |
| `Caching__RecommendationsTtlMinutes` | Cache TTL | `30` |

## URLs

| Service | URL | Purpose |
|---------|-----|---------|
| Aspire Dashboard | https://localhost:17168 | Logs, traces, metrics |
| Swagger | http://localhost:5188/swagger | API documentation |
| React App | http://localhost:5173 | Frontend |
| Neo4j Browser | http://localhost:7474 | Graph visualization |

## Documentation

- [Web Frontend](doc/RECOMMENDATION_WEB.md) - React frontend documentation

## License

MIT License - see [LICENSE](LICENSE) for details.

---

*Built by [tomaskoli](https://github.com/tomaskoli)*
