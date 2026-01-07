# Graph Recommendation Engine - Architecture

## Overview

A microservice architecture for product recommendation using graph-based relationships. The system imports data from JSON files into PostgreSQL, then streams changes through Kafka to build a Neo4j graph for recommendation queries.

```
┌─────────────────┐     ┌─────────────────────────────────┐     ┌─────────────────┐
│  JSON Import    │     │         PostgreSQL              │     │      React      │
│  (Files)        │────▶│  ┌─────────┐    ┌───────────┐  │     │      (UI)       │
└─────────────────┘     │  │  Data   │    │  Outbox   │  │     └─────────────────┘
                        │  │ Tables  │    │  Table    │  │             │
      DataIngestion     │  └─────────┘    └─────┬─────┘  │             │
      .Worker           └─────────────────────────────────┘             ▼
      (JSON → DB)                               │               ┌─────────────────┐
                                                │               │      BFF        │
                        ┌───────────────────────┘               │    Service      │
                        │                                       └─────────────────┘
                        ▼                                               │
               ┌─────────────────┐                                      │
               │  Outbox         │                                      │
               │  Processor      │                              ┌───────┴───────┐
               │  (Worker)       │                              │     Neo4j     │
               └────────┬────────┘                              │   (Graph DB)  │
                        │                                       └───────────────┘
                        ▼                                               ▲
               ┌─────────────────┐      ┌───────────────┐               │
               │     Kafka       │─────▶│   Neo4j-Sink  │───────────────┘
               │   (Strimzi)     │      │   Connector   │
               └─────────────────┘      └───────────────┘
```

### Data Flow (Outbox Pattern)

1. **JSON Import → PostgreSQL:** Worker reads JSON file, writes data + outbox messages in **same transaction**
2. **Outbox Processing:** Background job reads pending outbox messages and publishes to Kafka via Dapr
3. **Message Acknowledgment:** After successful Kafka publish, outbox message is marked as processed
4. **Kafka → Neo4j:** Neo4j Sink Connector consumes messages and populates the graph
5. **Neo4j → BFF → React:** BFF queries Neo4j for recommendations, React displays results

### Outbox Pattern Benefits

- **Atomicity:** Data changes and outbox messages are written in the same transaction
- **Reliability:** No message loss - if publish fails, message remains in outbox for retry
- **Idempotency:** Messages include unique ID for deduplication on consumer side
- **Ordering:** Messages are processed in order they were created

---

## Technology Stack

| Component | Technology | Version |
|-----------|------------|---------|
| Runtime | .NET | 10 |
| Orchestration | .NET Aspire | 13.1 |
| Service Mesh | Dapr | Latest |
| Message Broker | Apache Kafka (Strimzi) | Docker |
| Source Database | PostgreSQL | Docker |
| Graph Database | Neo4j | Docker |
| Cache | Redis | Docker |
| Frontend | React | 18+ |

---

## Services

### 1. DataIngestion.Worker

**Purpose:** Background worker that handles two pipelines:
1. **JSON Import Pipeline:** Imports data from JSON files into PostgreSQL (with outbox)
2. **Outbox Processor Pipeline:** Reads outbox messages and publishes to Kafka

**Responsibilities:**
- Load JSON files from `/import` folder and upsert into PostgreSQL
- Write outbox messages in the **same transaction** as data changes
- Apply import rules:
  - **Products:** Update if exists, insert if not (upsert)
  - **Categories:** Create if not exists (insert-only)
  - **Brands:** Create if not exists (insert-only)
  - **CatalogSegments:** Create if not exists (insert-only)
- Process outbox: read pending messages, publish to Kafka, mark as processed
- Handle retries for failed publishes with exponential backoff

**Project Structure (Vertical Slice):**
```
src/
└── DataIngestion.Worker/
    ├── Program.cs
    ├── appsettings.json
    ├── Features/
    │   ├── JsonImport/
    │   │   ├── ImportDataCommand.cs
    │   │   ├── ImportDataHandler.cs
    │   │   ├── ImportDataDto.cs
    │   │   ├── JsonImportJob.cs
    │   │   └── IJsonFileReader.cs
    │   └── Outbox/
    │       ├── OutboxMessage.cs
    │       ├── OutboxProcessorJob.cs
    │       ├── IOutboxRepository.cs
    │       └── Messages/
    │           ├── ProductMessage.cs
    │           ├── CategoryMessage.cs
    │           ├── BrandMessage.cs
    │           └── ProductCategoryMessage.cs
    ├── Infrastructure/
    │   ├── Postgres/
    │   │   ├── ProductRepository.cs
    │   │   ├── CategoryRepository.cs
    │   │   ├── BrandRepository.cs
    │   │   ├── SegmentRepository.cs
    │   │   └── OutboxRepository.cs
    │   ├── Json/
    │   │   └── JsonFileReader.cs
    │   └── Kafka/
    │       └── DaprMessagePublisher.cs
    └── Configuration/
        └── IngestionOptions.cs
```

**Import Rules Detail:**

| Entity | JSON Field Match | Operations | Behavior |
|--------|------------------|------------|----------|
| Product | `productId` | UPSERT, DELETE | Update if exists, insert if not; hard delete supported |
| Category | `categoryId` | UPSERT | Create if not exists, skip if exists (append-only) |
| Brand | `brandId` | UPSERT | Create if not exists, skip if exists (append-only) |
| CatalogSegment | `segmentId` | UPSERT | Create if not exists, skip if exists (append-only) |

**Product Deletion in JSON:**
```json
{
  "products": [
    {
      "productId": 123,
      "operation": "DELETE"
    }
  ]
}
```

When `operation: "DELETE"` is specified, the worker:
1. Deletes the product from PostgreSQL (cascade deletes ProductCategories)
2. Creates outbox message with `operation: DELETE`
3. Neo4j Sink uses `DETACH DELETE` to remove node and all relationships

**Outbox Processing Flow:**

```
┌─────────────────────────────────────────────────────────────────┐
│                    Single Transaction                           │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────────────┐ │
│  │ Upsert      │    │ Upsert      │    │ Insert Outbox       │ │
│  │ Product     │ +  │ Relations   │ +  │ Message             │ │
│  └─────────────┘    └─────────────┘    └─────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                                                   │
                                                   ▼
┌─────────────────────────────────────────────────────────────────┐
│                  Outbox Processor (Background Job)              │
│  1. SELECT * FROM Outbox WHERE ProcessedAt IS NULL              │
│     ORDER BY CreatedAt LIMIT 100                                │
│  2. For each message:                                           │
│     - Publish to Kafka via Dapr                                 │
│     - On success: UPDATE Outbox SET ProcessedAt = NOW()         │
│     - On failure: UPDATE Outbox SET RetryCount++, LastError=... │
│  3. Sleep and repeat                                            │
└─────────────────────────────────────────────────────────────────┘
```

**Retry Strategy:**
- Max retries: 5
- Backoff: Exponential (1s, 2s, 4s, 8s, 16s)
- Dead letter: After max retries, message remains for manual inspection

### 2. Recommendation.Api

**Purpose:** Backend-for-Frontend (BFF) service exposing recommendation queries via REST API.

**Responsibilities:**
- Query Neo4j graph for product recommendations
- Provide endpoints for:
  - Products by category
  - Related products (same brand, same category)
  - Category hierarchy navigation
  - Product search with recommendations

**Project Structure (Vertical Slice + CQRS):**
```
src/
└── Recommendation.Api/
    ├── Program.cs
    ├── appsettings.json
    ├── Features/
    │   ├── Products/
    │   │   ├── GetProductById/
    │   │   │   ├── GetProductByIdQuery.cs
    │   │   │   ├── GetProductByIdHandler.cs
    │   │   │   └── GetProductByIdEndpoint.cs
    │   │   ├── GetRelatedProducts/
    │   │   │   ├── GetRelatedProductsQuery.cs
    │   │   │   ├── GetRelatedProductsHandler.cs
    │   │   │   └── GetRelatedProductsEndpoint.cs
    │   │   └── Contracts/
    │   │       └── ProductDto.cs
    │   ├── Categories/
    │   │   ├── GetCategoryHierarchy/
    │   │   │   ├── GetCategoryHierarchyQuery.cs
    │   │   │   ├── GetCategoryHierarchyHandler.cs
    │   │   │   └── GetCategoryHierarchyEndpoint.cs
    │   │   ├── GetProductsByCategory/
    │   │   │   ├── GetProductsByCategoryQuery.cs
    │   │   │   ├── GetProductsByCategoryHandler.cs
    │   │   │   └── GetProductsByCategoryEndpoint.cs
    │   │   └── Contracts/
    │   │       └── CategoryDto.cs
    │   └── Recommendations/
    │       ├── GetRecommendations/
    │       │   ├── GetRecommendationsQuery.cs
    │       │   ├── GetRecommendationsHandler.cs
    │       │   └── GetRecommendationsEndpoint.cs
    │       └── Contracts/
    │           └── RecommendationDto.cs
    ├── Infrastructure/
    │   └── Neo4j/
    │       ├── Neo4jConnectionFactory.cs
    │       └── INeo4jSession.cs
    └── Common/
        ├── Behaviors/
        │   └── ValidationBehavior.cs
        └── Errors/
            ├── NotFoundError.cs
            └── ValidationError.cs
```

### 3. Recommendation.AppHost (Aspire)

**Purpose:** Orchestrates all services, databases, and infrastructure for local development.

```
src/
└── Recommendation.AppHost/
    ├── Program.cs
    └── appsettings.json
```

### 4. Recommendation.ServiceDefaults

**Purpose:** Shared service defaults for observability, health checks, and common configuration.

```
src/
└── Recommendation.ServiceDefaults/
    ├── Extensions.cs
    └── ServiceDefaultsExtensions.cs
```

### 5. Recommendation.Web (React Frontend)

**Purpose:** React SPA for browsing products and viewing recommendations.

```
src/
└── Recommendation.Web/
    ├── package.json
    ├── src/
    │   ├── App.tsx
    │   ├── components/
    │   │   ├── ProductCard/
    │   │   ├── CategoryTree/
    │   │   └── RecommendationList/
    │   ├── pages/
    │   │   ├── HomePage.tsx
    │   │   ├── ProductPage.tsx
    │   │   └── CategoryPage.tsx
    │   └── services/
    │       └── api.ts
    └── vite.config.ts
```

---

## Data Schema

### PostgreSQL (Source)

```sql
-- Products table
CREATE TABLE Products (
    Product_ID INT PRIMARY KEY,
    ProductName NVARCHAR(255) NOT NULL,
    ProductDescription NVARCHAR(MAX),
    Brand_ID INT
);

-- Brands table
CREATE TABLE Brands (
    Brand_ID INT PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL
);

-- Categories table (self-referencing for hierarchy)
CREATE TABLE Categories (
    Category_ID INT PRIMARY KEY,
    CategoryName NVARCHAR(255) NOT NULL,
    ParentCategory_ID INT NULL,
    CatalogSegment_ID INT NULL,
    FOREIGN KEY (ParentCategory_ID) REFERENCES Categories(Category_ID)
);

-- CatalogSegments table
CREATE TABLE CatalogSegments (
    CatalogSegments_ID INT PRIMARY KEY,
    SegmentName NVARCHAR(255) NOT NULL
);

-- ProductCategories junction table
CREATE TABLE ProductCategories (
    ProductCategory_ID INT PRIMARY KEY,
    Product_ID INT NOT NULL,
    Category_ID INT NOT NULL,
    FOREIGN KEY (Product_ID) REFERENCES Products(Product_ID),
    FOREIGN KEY (Category_ID) REFERENCES Categories(Category_ID)
);

-- Foreign key for Products -> Brands
ALTER TABLE Products
ADD FOREIGN KEY (Brand_ID) REFERENCES Brands(Brand_ID);

-- Foreign key for Categories -> CatalogSegments
ALTER TABLE Categories
ADD FOREIGN KEY (CatalogSegment_ID) REFERENCES CatalogSegments(CatalogSegments_ID);

-- Outbox table for reliable messaging
CREATE TABLE Outbox (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    AggregateType VARCHAR(100) NOT NULL,
    AggregateId VARCHAR(100) NOT NULL,
    EventType VARCHAR(100) NOT NULL,
    Topic VARCHAR(100) NOT NULL,
    Payload JSONB NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    ProcessedAt TIMESTAMP WITH TIME ZONE NULL,
    RetryCount INT NOT NULL DEFAULT 0,
    LastError TEXT NULL
);

-- Index for efficient polling of pending messages
CREATE INDEX IX_Outbox_Pending ON Outbox (CreatedAt) 
WHERE ProcessedAt IS NULL;

-- Index for cleanup of processed messages
CREATE INDEX IX_Outbox_Processed ON Outbox (ProcessedAt) 
WHERE ProcessedAt IS NOT NULL;
```

### Outbox Message Structure

| Column | Type | Description |
|--------|------|-------------|
| Id | UUID | Unique message identifier, included in payload as `outboxId` for consumer idempotency |
| AggregateType | VARCHAR | Entity type (Product, Category, Brand, etc.) |
| AggregateId | VARCHAR | Entity ID - **used as Kafka message key** for partition routing |
| EventType | VARCHAR | Type of change (Created, Updated) |
| Topic | VARCHAR | Kafka topic to publish to |
| Payload | JSONB | Serialized message content (includes `outboxId`) |
| CreatedAt | TIMESTAMP | When message was created - **determines processing order within partition** |
| ProcessedAt | TIMESTAMP | When message was successfully published (NULL if pending) |
| RetryCount | INT | Number of publish attempts |
| LastError | TEXT | Last error message if publish failed |

### JSON Import File Structure

Single file `import/data.json` contains all entities. The worker processes entities in dependency order automatically.

**data.json:**
```json
{
  "segments": [
    {
      "segmentId": 1,
      "segmentName": "Electronics"
    }
  ],
  "brands": [
    {
      "brandId": 456,
      "name": "Brand Name"
    }
  ],
  "categories": [
    {
      "categoryId": 789,
      "categoryName": "Category Name",
      "parentCategoryId": null,
      "catalogSegmentId": 1
    },
    {
      "categoryId": 790,
      "categoryName": "Subcategory",
      "parentCategoryId": 789,
      "catalogSegmentId": 1
    }
  ],
  "products": [
    {
      "productId": 123,
      "productName": "Product Name",
      "productDescription": "Description text",
      "brandId": 456,
      "categoryIds": [789, 790]
    }
  ]
}
```

**Processing Order (handled by worker):**
1. `segments` - No dependencies
2. `brands` - No dependencies
3. `categories` - Depends on segments (optional), self-referencing for hierarchy
4. `products` - Depends on brands and categories

### Kafka Topics

| Topic Name | Message Type | Message Key | Partitions | Operations | Purpose |
|------------|--------------|-------------|------------|------------|---------|
| `products` | ProductMessage | `productId` | 3 | UPSERT, DELETE | Product data changes |
| `categories` | CategoryMessage | `categoryId` | 3 | UPSERT | Category data (append-only) |
| `brands` | BrandMessage | `brandId` | 3 | UPSERT | Brand data (append-only) |
| `product-categories` | ProductCategoryMessage | `productId` | 3 | UPSERT | Product-Category relationships |
| `deadletter` | Any | original key | 1 | - | Failed messages from worker |
| `neo4j-dlq` | Any | original key | 1 | - | Failed messages from Neo4j Sink |

### Kafka Partitioning & Ordering Strategy

**Message Keys:**
- Each message uses the aggregate ID as the Kafka key
- Key ensures all messages for the same entity go to the same partition
- Same partition = guaranteed ordering per aggregate

**Ordering Guarantees:**

| Topic | Key | Guarantee |
|-------|-----|-----------|
| `products` | `productId` | All updates for product X are processed in order |
| `categories` | `categoryId` | All updates for category Y are processed in order |
| `brands` | `brandId` | All updates for brand Z are processed in order |
| `product-categories` | `productId` | All category assignments for product X are processed in order |

**Why `productId` for product-categories?**
- Product is the "owner" of the relationship
- Ensures all category assignments for a product are ordered
- Trade-off: category assignments across different products may interleave (acceptable)

**Outbox → Kafka Key Mapping:**

The outbox processor uses `AggregateId` as the Kafka message key:

```csharp
// In DaprMessagePublisher
await daprClient.PublishEventAsync(
    pubsubName: "pubsub",
    topicName: outboxMessage.Topic,
    data: outboxMessage.Payload,
    metadata: new Dictionary<string, string>
    {
        { "partitionKey", outboxMessage.AggregateId }  // Kafka key
    });
```

**Consumer Idempotency:**
- Neo4j Sink Connector receives messages with `outboxId` in payload
- MERGE operations are inherently idempotent
- Duplicate messages (retries) result in same graph state

**Message Schemas:**

```json
// ProductMessage - UPSERT
{
  "outboxId": "550e8400-e29b-41d4-a716-446655440000",
  "productId": 123,
  "productName": "Product Name",
  "productDescription": "Description",
  "brandId": 456,
  "operation": "UPSERT"
}

// ProductMessage - DELETE (tombstone)
{
  "outboxId": "550e8400-e29b-41d4-a716-446655440004",
  "productId": 123,
  "operation": "DELETE"
}

// CategoryMessage
{
  "outboxId": "550e8400-e29b-41d4-a716-446655440001",
  "categoryId": 789,
  "categoryName": "Category Name",
  "parentCategoryId": 100,
  "catalogSegmentId": 1,
  "operation": "UPSERT"
}

// BrandMessage
{
  "outboxId": "550e8400-e29b-41d4-a716-446655440002",
  "brandId": 456,
  "name": "Brand Name",
  "operation": "UPSERT"
}

// ProductCategoryMessage
{
  "outboxId": "550e8400-e29b-41d4-a716-446655440003",
  "productCategoryId": 111,
  "productId": 123,
  "categoryId": 789,
  "operation": "UPSERT"
}
```

### Neo4j Graph Model

**Nodes:**

```cypher
// Product node
(:Product {
    productId: INT,
    productName: STRING,
    productDescription: STRING
})

// Category node
(:Category {
    categoryId: INT,
    categoryName: STRING
})

// Brand node
(:Brand {
    brandId: INT,
    name: STRING
})

// CatalogSegment node
(:CatalogSegment {
    segmentId: INT,
    segmentName: STRING
})
```

**Relationships:**

```cypher
// Product belongs to Category
(:Product)-[:BELONGS_TO]->(:Category)

// Product manufactured by Brand
(:Product)-[:MADE_BY]->(:Brand)

// Category has parent Category (hierarchy)
(:Category)-[:CHILD_OF]->(:Category)

// Category belongs to CatalogSegment
(:Category)-[:IN_SEGMENT]->(:CatalogSegment)
```

**Graph Visualization:**

```
                    ┌─────────────────┐
                    │ CatalogSegment  │
                    └────────▲────────┘
                             │
                        IN_SEGMENT
                             │
┌──────────────┐      ┌──────┴───────┐      ┌──────────────┐
│ Brand │◀─────│   Category   │─────▶│   Category   │
└──────────────┘      └──────▲───────┘      │   (Parent)   │
        ▲                    │              └──────────────┘
        │               BELONGS_TO               CHILD_OF
   MADE_BY           │                     │
        │              ┌─────┴─────┐               │
        └──────────────│  Product  │───────────────┘
                       └───────────┘
```

---

## Neo4j Schema Setup

Run these constraints and indexes **before** starting the connector:

```cypher
// Unique constraints (also create implicit indexes)
CREATE CONSTRAINT product_id_unique IF NOT EXISTS
FOR (p:Product) REQUIRE p.productId IS UNIQUE;

CREATE CONSTRAINT category_id_unique IF NOT EXISTS
FOR (c:Category) REQUIRE c.categoryId IS UNIQUE;

CREATE CONSTRAINT brand_id_unique IF NOT EXISTS
FOR (b:Brand) REQUIRE b.brandId IS UNIQUE;

CREATE CONSTRAINT segment_id_unique IF NOT EXISTS
FOR (s:CatalogSegment) REQUIRE s.segmentId IS UNIQUE;

// Additional indexes for query performance
CREATE INDEX product_name_idx IF NOT EXISTS
FOR (p:Product) ON (p.productName);

CREATE INDEX category_name_idx IF NOT EXISTS
FOR (c:Category) ON (c.categoryName);

CREATE INDEX brand_name_idx IF NOT EXISTS
FOR (b:Brand) ON (b.name);
```

---

## Neo4j Sink Connector Configuration

Located at `deploy/kafka-connect/connectors/neo4j-sink.json`:

**Out-of-Order Handling Strategy:**
- All topics use `MERGE` to handle messages arriving in any order
- Product-categories uses `MERGE` for both nodes to create placeholders if not yet received
- Brand relationship from products creates placeholder Brand node; `brands` topic fills in the name
- Products topic handles DELETE operation with `DETACH DELETE` (removes node and all relationships)

```json
{
  "name": "neo4j-sink-connector",
  "config": {
    "connector.class": "streams.kafka.connect.sink.Neo4jSinkConnector",
    "topics": "products,categories,brands,product-categories",
    "neo4j.server.uri": "bolt://neo4j:7687",
    "neo4j.authentication.basic.username": "neo4j",
    "neo4j.authentication.basic.password": "${NEO4J_PASSWORD}",
    
    "neo4j.topic.cypher.products": "CALL { WITH event WHERE event.operation = 'DELETE' MATCH (p:Product {productId: event.productId}) DETACH DELETE p RETURN 1 AS done UNION WITH event WHERE event.operation <> 'DELETE' MERGE (p:Product {productId: event.productId}) SET p.productName = event.productName, p.productDescription = event.productDescription WITH p, event WHERE event.brandId IS NOT NULL MERGE (b:Brand {brandId: event.brandId}) MERGE (p)-[:MADE_BY]->(b) RETURN 1 AS done }",
    
    "neo4j.topic.cypher.categories": "MERGE (c:Category {categoryId: event.categoryId}) SET c.categoryName = event.categoryName WITH c, event WHERE event.parentCategoryId IS NOT NULL MERGE (parent:Category {categoryId: event.parentCategoryId}) MERGE (c)-[:CHILD_OF]->(parent)",
    
    "neo4j.topic.cypher.brands": "MERGE (b:Brand {brandId: event.brandId}) SET b.name = event.name",
    
    "neo4j.topic.cypher.product-categories": "MERGE (p:Product {productId: event.productId}) MERGE (c:Category {categoryId: event.categoryId}) MERGE (p)-[:BELONGS_TO]->(c)",
    
    "errors.tolerance": "all",
    "errors.deadletterqueue.topic.name": "neo4j-dlq",
    "errors.deadletterqueue.topic.replication.factor": 1,
    "errors.deadletterqueue.context.headers.enable": true,
    "errors.log.enable": true,
    "errors.log.include.messages": true
  }
}
```

**Topic Processing Notes:**

| Topic | Strategy | Operations | Notes |
|-------|----------|------------|-------|
| `products` | MERGE/DELETE | UPSERT, DELETE | DELETE uses `DETACH DELETE` to remove node + all relationships |
| `categories` | MERGE | UPSERT only | Append-only, parent placeholder created if needed |
| `brands` | MERGE | UPSERT only | Append-only, fills `name` on existing placeholder |
| `product-categories` | MERGE both nodes, MERGE relationship | Creates placeholder Product/Category if not exists; filled when their messages arrive |

**Trade-off:** Placeholder nodes may temporarily have incomplete data (e.g., Brand without name). The API should handle this gracefully (return brandId even if name is null).

---

## Dapr Configuration

### Pub/Sub Component (Kafka with DLQ)

`deploy/dapr/components/pubsub.yaml`:
```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: pubsub
spec:
  type: pubsub.kafka
  version: v1
  metadata:
    - name: brokers
      value: "localhost:9092"
    - name: consumerGroup
      value: "recommendation-engine"
    - name: authType
      value: "none"
    - name: maxRetries
      value: "3"
    - name: backOffDuration
      value: "2s"
```

### Dead Letter Queue Subscription

`deploy/dapr/components/subscription.yaml`:
```yaml
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: product-subscription
spec:
  pubsubname: pubsub
  topic: products
  routes:
    default: /api/products/process
  deadLetterTopic: deadletter
---
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: category-subscription
spec:
  pubsubname: pubsub
  topic: categories
  routes:
    default: /api/categories/process
  deadLetterTopic: deadletter
---
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: brand-subscription
spec:
  pubsubname: pubsub
  topic: brands
  routes:
    default: /api/brands/process
  deadLetterTopic: deadletter
```

### State Store (Redis)

`deploy/dapr/components/statestore.yaml`:
```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: statestore
spec:
  type: state.redis
  version: v1
  metadata:
    - name: redisHost
      value: "localhost:6379"
    - name: redisPassword
      value: ""
```

---

## API Endpoints

### Recommendation.Api

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/products/{id}` | Get product by ID |
| GET | `/api/products/{id}/related` | Get related products |
| GET | `/api/categories` | Get root categories |
| GET | `/api/categories/{id}` | Get category with subcategories |
| GET | `/api/categories/{id}/products` | Get products in category |
| GET | `/api/recommendations?productId={id}` | Get recommendations for product |

---

## Recommendation Queries (Cypher)

### Products in Same Category
```cypher
MATCH (p:Product {productId: $productId})-[:BELONGS_TO]->(c:Category)<-[:BELONGS_TO]-(related:Product)
WHERE related.productId <> $productId
RETURN related
LIMIT 10
```

### Products by Same Brand
```cypher
MATCH (p:Product {productId: $productId})-[:MADE_BY]->(m:Brand)<-[:MADE_BY]-(related:Product)
WHERE related.productId <> $productId
RETURN related
LIMIT 10
```

### Category Hierarchy (Ancestors)
```cypher
MATCH (c:Category {categoryId: $categoryId})-[:CHILD_OF*0..]->(ancestor:Category)
RETURN ancestor
```

### Category Hierarchy (Descendants)
```cypher
MATCH (c:Category {categoryId: $categoryId})<-[:CHILD_OF*0..]-(descendant:Category)
RETURN descendant
```

---

## Solution Structure

```
Graph-Recommendation-Engine.sln
│
├── src/
│   ├── Recommendation.AppHost/           # Aspire orchestration
│   ├── Recommendation.ServiceDefaults/   # Shared defaults
│   ├── DataIngestion.Worker/             # Background worker
│   ├── Recommendation.Api/               # BFF API
│   └── Recommendation.Web/               # React frontend
│
├── tests/
│   ├── DataIngestion.Worker.Tests/
│   └── Recommendation.Api.Tests/
│
├── deploy/
│   ├── Docker/
│   │   ├── docker-compose.kafka.yml
│   │   ├── docker-compose.neo4j.yml
│   │   ├── docker-compose.postgres.yml
│   │   └── docker-compose.redis.yml
│   ├── neo4j/
│   │   └── schema.cypher              # Constraints and indexes
│   ├── kafka-connect/
│   │   └── connectors/
│   │       └── neo4j-sink.json
│   └── dapr/
│       └── components/
│           ├── pubsub.yaml
│           ├── statestore.yaml
│           └── subscription.yaml
│
├── import/                               # JSON import files
│   └── data.json                         # Single file with all entities
│
└── doc/
    └── ARCHITECTURE.md
```

---

## Development Workflow

1. **Start Infrastructure:**
   ```powershell
   docker-compose -f deploy/Docker/docker-compose.postgres.yml up -d
   docker-compose -f deploy/Docker/docker-compose.kafka.yml up -d
   docker-compose -f deploy/Docker/docker-compose.neo4j.yml up -d
   docker-compose -f deploy/Docker/docker-compose.redis.yml up -d
   ```

2. **Setup Neo4j Schema (constraints & indexes):**
   ```powershell
   # Connect to Neo4j and run schema setup from deploy/neo4j/schema.cypher
   # Or via Neo4j Browser at http://localhost:7474
   ```

3. **Deploy Neo4j Sink Connector:**
   ```powershell
   .\deploy\kafka-connect\deploy-connectors.ps1
   ```

4. **Run with Aspire:**
   ```powershell
   dotnet run --project src/Recommendation.AppHost
   ```

5. **Trigger Data Ingestion:**
   - Worker will automatically start ingestion job
   - Or call manual trigger endpoint if implemented

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Vertical Slices | Features are self-contained, easier to understand and modify |
| CQRS (Read-only) | Queries go to Neo4j, no write side in BFF (writes via Kafka) |
| Outbox Pattern | Guarantees atomicity between DB writes and message publishing, no message loss |
| Hard Delete (Products only) | Products can be removed; `DETACH DELETE` cleans up all relationships in Neo4j |
| Append-only (Reference Data) | Categories, Brands, Segments are never deleted to maintain referential integrity |
| Dead Letter Queue | Failed messages routed to DLQ for investigation; prevents poison messages blocking pipeline |
| Dapr Pub/Sub | Abstracts Kafka, enables local development and testing |
| Neo4j Sink Connector | Kafka Connect handles graph population, decouples worker from Neo4j |
| Aspire Orchestration | Simplifies local development, built-in observability |
| FluentResults | Typed errors instead of exceptions for expected failures |

---

## Dead Letter Queue Monitoring

| DLQ Topic | Source | Action |
|-----------|--------|--------|
| `deadletter` | Dapr Pub/Sub (Worker) | Messages that failed after 3 retries from outbox processor |
| `neo4j-dlq` | Kafka Connect (Neo4j Sink) | Messages that failed Cypher execution |

**Monitoring:**
- Check DLQ topics periodically via Kafka UI or CLI
- Investigate `errors.deadletterqueue.context.headers` for failure context
- Fix root cause, then replay messages manually or via tooling

---

## Dependencies (NuGet Packages)

### DataIngestion.Worker
- `Aspire.Npgsql.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore`
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Dapr.AspNetCore`
- `Dapr.Client`
- `Microsoft.Extensions.Hosting`
- `MediatR`

### Recommendation.Api
- `Neo4j.Driver`
- `MediatR`
- `FluentValidation`
- `FluentResults`
- `Dapr.AspNetCore`

### Shared
- `Aspire.Hosting.AppHost`
- `Microsoft.Extensions.ServiceDiscovery`

