# Graph Recommendation Engine

**Product recommendation** app using Neo4j Graph Data Science (GDS) and Apache Spark.

## Tech Stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 10, ASP.NET Core Minimal APIs |
| Orchestration | .NET Aspire 13.1 |
| Graph Database | Neo4j 2025.10.1 + GDS Plugin |
| Batch Processing | Apache Spark 4.1.3 (co-purchase lift, Dockerized) |
| Cache | Redis 8 |
| Frontend | React 18 + Vite |
| Containerization | Docker |

## Features

- **Graph-Based Recommendations** - blends two independent signals: content similarity from
  Neo4j GDS (FastRP + kNN) and co-purchase behavior from an Apache Spark batch pipeline
  (see [Spark Pipeline](doc/SPARK-PIPELINE.md))
- **Vertical Slice Architecture** - Feature-based organization with MediatR
- **.NET Aspire** - Cloud-ready orchestration with service discovery
- **Caching** - Redis caching for recommendation results
- **Category Hierarchy** - Recursive category tree traversal via Cypher
- **React** - Frontend displays products and recommendations

## Architecture

```
┌─────────────────┐                               ┌─────────────────┐       ┌─────────────────┐
│      React      │                               │     Neo4j       │◀─────│  Apache Spark    │
│      (UI)       │◀────────────────────────────▶│   + GDS         │       │ (batch, offline)│
└─────────────────┘                               └─────────────────┘       └─────────────────┘
        │                                                ▲                   writes ALSO_BOUGHT;
        │                                                │                   GDS writes SIMILAR_TO
        ▼                                                │                   independently
┌─────────────────┐                                      │
│ Recommendation  │──────────────────────────────────────┘
│      API        │        blends both signals at query time
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
├── spark/                            # Co-purchase lift pipeline (see doc/SPARK-PIPELINE.md)
├── deploy/
│   ├── Docker/                       # Docker Compose files
│   └── scripts/                      # Cypher scripts (GDS similarity)
└── doc/                              # Documentation
```

## NuGet Packages

### API
| Package | Version | Purpose |
|---------|---------|---------|
| MediatR | 14.2.0 | CQRS in-process messaging |
| FluentResults | 4.0.0 | Result pattern |
| FluentValidation | 12.1.1 | Request validation |
| Neo4j.Driver | 6.3.0 | Neo4j client |
| StackExchange.Redis | 3.1.0 | Redis client |
| Scrutor | 7.0.0 | Decorator registration |
| Swashbuckle.AspNetCore | 10.2.3 | Swagger/OpenAPI |

### Aspire
| Package | Version | Purpose |
|---------|---------|---------|
| Aspire.AppHost.Sdk | 13.4.6 | Orchestration SDK |
| Aspire.Hosting.Redis | 13.4.6 | Redis resource |

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
| GET | `/?productId={id}&strategy={content\|behavioral\|hybrid}` | Similar products; `strategy` defaults to `hybrid` (blends GDS similarity + Spark co-purchase lift) |

### Search (`/api/search`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/?q={term}&limit={n}` | Global search (products, categories, brands) |

## Caching

- **TTL:** 30 minutes (configurable)
- **Cache Key Pattern:** `recs:{productId}:{strategy}:{page}:{pageSize}`

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

The graph starts empty — constraints only, no nodes. Run in order (full details, options,
and troubleshooting in [spark/README.md](spark/README.md) and
[Spark Pipeline](doc/SPARK-PIPELINE.md)):

```bash
# 1. Constraints/indexes (in Neo4j Browser)
# File: deploy/scripts/seed-neo4j.cypher

# 2. Synthetic catalog -> Neo4j (Product, Category, Brand, Parameter, CatalogSegment)
python spark/generate_catalog.py

# 3. Synthetic transactions -> Parquet
python spark/generate_transactions.py

# 4. Spark: co-purchase lift -> Parquet + Neo4j (ALSO_BOUGHT), via Docker
docker compose -f deploy/Docker/docker-compose.spark.yml run --rm --service-ports spark

# 5. GDS similarity (SIMILAR_TO) - run in Neo4j Browser
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
| Spark UI | http://localhost:4040 | Apache Spark dashboard |

## Documentation

- [Web Frontend](doc/RECOMMENDATION_WEB.md) - React frontend documentation
- [Graph Schema](doc/GRAPH-SCHEMA.md) -  Neo4j graph schema documentation
- [Spark Pipeline](doc/SPARK-PIPELINE.md) - Spark co-purchase pipeline specification

## License

MIT License - see [LICENSE](LICENSE) for details.

---

*Built by [tomaskoli](https://github.com/tomaskoli)*
