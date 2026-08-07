# Spark Co-Purchase Pipeline Specification

## Purpose

Extend the recommendation engine with a **behavioral signal** computed by Apache Spark.
The existing GDS pipeline (FastRP + kNN) produces `SIMILAR_TO` edges from catalog
*structure* (categories, parameters). This pipeline produces `ALSO_BOUGHT` edges from
transaction *behavior* (co-purchase lift). The two signals are complementary and have
opposite failure modes:

| Signal | Edge | Strength | Fails on |
|--------|------|----------|----------|
| Content (GDS) | `SIMILAR_TO` | Cold-start products, substitutes | Cross-category intent (phone → case) |
| Behavioral (Spark) | `ALSO_BOUGHT` | Complements, real purchase intent | New products with no sales |

Neither pipeline overwrites the other's edges. The API blends both at query time.

## Architecture

```
generate_catalog.py ──▶ Neo4j (Product, Category, Brand, Parameter, CatalogSegment)
   (plain Python)              │
                                ▼
generate_transactions.py ──▶ Parquet ──▶ copurchase_lift.py ──▶ Parquet ──▶ Neo4j
     (plain Python)        (baskets)      (PySpark local[*])    (edges)   (ALSO_BOUGHT)
                                                                              │
GDS FastRP + kNN ─────────────────────────▶ SIMILAR_TO ──────────────────────┤
                                                                              ▼
                                                              API blends at query time
```

**No seed data exists in the repo today.** `deploy/scripts/seed-neo4j.cypher` only
declares constraints and indexes — it creates zero nodes. `generate_catalog.py` fills
that gap and is a prerequisite for everything below: `generate_transactions.py` reads
the catalog from Neo4j to correlate baskets, so an empty graph produces no usable
baskets. Run order: catalog → transactions → Spark job → GDS embeddings.

- Spark runs as PySpark on `local[*]` — no cluster. Recommended execution is via
  `deploy/Docker/docker-compose.spark.yml` (the `apache/spark` image already
  bundles PySpark, so `copurchase_lift.py` needs no custom build); running the
  script directly with a local `pip install pyspark` also works given a working
  JDK. The Neo4j Spark connector JAR (Stage 3 only) is baked into the image by
  `spark/Dockerfile`, so no submit-time flags are needed — see Stage 3.
- The Spark UI is on port 4040, live for the duration of the run only. `docker compose
  run` needs `--service-ports` to actually publish it — the `ports:` section alone only
  applies to `up`.
- Parquet is the intermediate at both hops so the Neo4j load can be re-run without
  re-running the aggregation (and vice versa).
- Aspire does not orchestrate Spark; the pipeline is a batch job run manually or by CI.

## Repository layout

```
spark/
├── catalog_seed.yaml           # catalog data (segments/categories/brands/parameters)
├── generate_catalog.py         # synthetic catalog generator (plain Python + neo4j driver)
├── generate_transactions.py    # synthetic basket generator (plain Python + neo4j driver)
├── copurchase_lift.py          # the Spark job: baskets → scored edges → Neo4j
├── Dockerfile                  # apache/spark + the Neo4j connector JAR baked in
├── requirements.txt            # neo4j, python-dotenv, pyyaml, pyarrow (no pyspark — see below)
├── .env.example                # NEO4J_URI / NEO4J_USER / NEO4J_PASSWORD / NEO4J_DATABASE template
└── README.md                   # how to run, expected output, plan-reading notes
deploy/Docker/docker-compose.spark.yml  # builds spark/Dockerfile, runs the full Stage 2+3 job
data/                           # gitignored Parquet output (transactions/, edges/)
```

## Stage 0 — Synthetic catalog generator

`generate_catalog.py`. Plain Python + the `neo4j` driver, **not** a Spark job — same
reasoning as the transaction generator: it manufactures input, it isn't the showcase.
Writes directly via batched `UNWIND ... MERGE` (no CSV/`LOAD CSV` plumbing needed for
demo scale).

Fills every node/relationship type the schema already defines but the repo never
seeds, matching `doc/GRAPH-SCHEMA.md` and what the GDS script and `/api/segments`
endpoint already assume exists:

1. **CatalogSegment** (~4–6: Electronics, Home, Fashion, Sports, ...).
2. **Category**, two levels deep, `CHILD_OF` parent, `IN_SEGMENT` a segment
   (~30–40 leaf categories across the segments).
3. **Brand** (~25–30).
4. **Product** (~400–600), `BELONGS_TO` a leaf category, `MADE_BY` a brand.
   Assign popularity from a Zipf distribution here — the transaction generator
   (Stage 1) reuses these weights so head/tail skew is consistent across both
   generators instead of resampled independently.
5. **Parameter** (2–5 per product: e.g. Color, Size), `HAS_PARAMETER`.

Note: `doc/GRAPH-SCHEMA.md` currently flags `CatalogSegment` and `IN_SEGMENT` as
"not yet ingested" — this generator resolves that gap. Update the doc once it lands.

Idempotent: run guarded by the existing `MATCH (n) DETACH DELETE n;` at the top of
`seed-neo4j.cypher`, or the script clears its own labels before writing. Seeded RNG
for reproducibility.

## Stage 1 — Synthetic transaction generator

`generate_transactions.py`. Plain Python, **not** a Spark job — it manufactures input,
it is not part of the showcase.

Uniformly random baskets produce lift ≈ 1.0 everywhere and the recommendations look
broken. The generator must correlate baskets with the existing graph:

1. Read product IDs, category membership, and popularity weights from Neo4j (written
   by Stage 0). `SIMILAR_TO` neighborhoods won't exist yet on a fresh graph — the GDS
   embeddings script runs *after* this pipeline, so basket walking must fall back to
   same-category siblings when `SIMILAR_TO` is empty (always true on first run).
2. Reuse the **Zipf popularity** weights Stage 0 assigned (real catalogs are
   head-heavy; this also creates the bestseller bias that lift must then correct).
3. Sample baskets: pick a seed product by popularity, then fill the basket by walking
   `SIMILAR_TO` edges and same-category siblings with decaying probability. Basket size
   drawn from a distribution centered on 2–5 items, with a small tail of large baskets
   (so the size cap in Stage 2 has something to cut).
4. Write one Parquet file: `order_id, customer_id, product_id, ts` (timestamps spread
   over ~180 days — required later for the `BOUGHT_NEXT` stretch goal).

Defaults: ~5 000 customers, ~50 000 orders. Seeded RNG for reproducibility.

## Stage 2 — Spark job: co-purchase lift

`copurchase_lift.py`. This is the showcase. Steps, in order:

### 2.1 Read + clean

```python
tx = spark.read.parquet("data/transactions")
```

- Deduplicate `(order_id, product_id)` (quantity > 1 is not a co-purchase signal).
- **Basket size cap: keep orders with 2–20 items.** Single-item orders carry no pair
  signal; a 200-item basket emits ~20k pairs of noise *and* is a hot key in the
  self-join below — the cap is the skew mitigation, not just denoising.
- **`clean.cache()`** — `clean` is consumed three times (total-order count, marginals,
  self-join). Without caching, the full read + filter lineage recomputes per action.

### 2.2 Marginals — written in SQL

One step is deliberately written as literal Spark SQL against a temp view, to show both
front-ends of the same Catalyst engine:

```python
clean.createOrReplaceTempView("baskets")
marg = spark.sql("""
    SELECT product_id, COUNT(DISTINCT order_id) AS cnt
    FROM baskets GROUP BY product_id
""")
n = clean.select("order_id").distinct().count()
```

### 2.3 Pair generation (self-join)

Self-join on `order_id` with `a.product_id < b.product_id` (each unordered pair once),
then count pairs and **prune `count >= minSupport` (default 5) before joining
marginals** — pruning after would shuffle the long tail for nothing.

### 2.4 Lift, confidence, and symmetrization

Raw co-purchase counts rank bestsellers: everything co-occurs with the top seller.
Score with **lift** instead:

```
lift(a,b) = P(a,b) / (P(a) · P(b)) = (pairCount · totalOrders) / (cntA · cntB)
```

- Join marginals with **`F.broadcast(marg)`** — marg is one row per product, tiny;
  broadcasting eliminates two shuffle stages (`BroadcastHashJoin` in the plan).
- Also compute **confidence** `P(b|a) = pairCount / cntA`, which is asymmetric
  (case → phone ≠ phone → case).
- Emit both directions: union the frame with src/dst swapped, confidence recomputed
  per direction. Lift and count are symmetric and carried through.

### 2.5 Plan inspection

The job calls `edges.explain()` once; `spark/README.md` documents how to read the
output. Confirmed against a real run: the marginal joins show as `BroadcastHashJoin`
as expected, and at demo data scale **AQE also converts the self-join into a
`BroadcastHashJoin`** once it sees how small `clean` is post-filter — a genuine
runtime re-optimization, not a static plan choice. At a scale where `clean` exceeds
`spark.sql.autoBroadcastJoinThreshold`, expect a shuffled `SortMergeJoin` there instead.

### 2.6 Output

Write the edge frame to `data/edges` (Parquet): `src, dst, count, lift, confidence`.

### 2.7 Worked example

One small example traced end to end, with round numbers instead of the real
137,970-row run — same math, easier to follow by hand.

**Input** (`data/transactions/*.parquet`) — flat order lines, no structure beyond one
row per `(order, product)`:

| order_id | customer_id | product_id | ts |
|---|---|---|---|
| 1 | 94 | Phone | ... |
| 1 | 94 | Case | ... |
| 2 | 16 | Phone | ... |
| ... | | | |

**2.1 dedupe + basket-size filter.** Say there's also a 1-item order and a 25-item
bulk order in the mix:

| order_id | kept? | why |
|---|---|---|
| single-item order | ❌ dropped | one item can't form a pair |
| 25-item bulk order | ❌ dropped | would flood the self-join with noise |
| everything else (2–20 items) | ✅ kept | this is `clean` |

Transform: raw order lines → order lines belonging only to "normal-sized" baskets.

**2.2 marginals.** Count how many distinct orders contain each product (out of, say,
`n = 10` valid orders total):

| product_id | cnt |
|---|---|
| Phone | 4 |
| Case | 3 |
| Bread | 8 |
| Milk | 8 |

Transform: order-lines → one row per product, "how popular is this."

**2.3 self-join into pairs.** The real shape change: rows stop being "one product in
one order" and become "two products that showed up in the *same* order." Group +
count how many orders each pair appeared together in:

| src | dst | count |
|---|---|---|
| Phone | Case | 3 |
| Bread | Milk | **6** |

Bread+Milk has the *higher* raw count. Sorted by `count` alone, it would look like
the best pair — it isn't. That's the exact trap lift exists to catch: Bread and Milk
are just both independently popular, so of course they co-occur a lot.

**2.4 lift + confidence.** `lift = (count × n) / (cs × cd)`:

| pair | count | cs | cd | lift | meaning |
|---|---|---|---|---|---|
| Phone, Case | 3 | 4 | 3 | (3×10)/(4×3) = **2.5** | co-occurs 2.5× more than chance — real signal |
| Bread, Milk | 6 | 8 | 8 | (6×10)/(8×8) = **0.94** | co-occurs about as often as chance predicts — noise |

Despite the lower raw count, Phone+Case is the meaningful pairing; Bread+Milk drops
below 1 once popularity is divided out. This is the entire reason this pipeline
exists instead of just counting co-purchases.

Confidence is directional, unlike lift:

```
confidence(Phone → Case) = count / cs = 3/4 = 0.75   (75% of Phone buyers also bought Case)
confidence(Case → Phone) = count / cd = 3/3 = 1.00   (100% of Case buyers also bought Phone)
```

A case is useless without a phone, but plenty of phone buyers skip the case.

**2.6 symmetrize.** The self-join only emits each pair once (`src < dst`); the union
duplicates each row with `src`/`dst` swapped and confidence recomputed per direction
(`lift`/`count` unchanged either way):

| src | dst | count | lift | confidence |
|---|---|---|---|---|
| Phone | Case | 3 | 2.5 | 0.75 |
| Case | Phone | 3 | 2.5 | 1.00 |

That's `data/edges/*.parquet` — matches the shape of the real run, where product
245→250 had `lift=286.3` in both directions but `confidence=0.321` one way and
`0.220` the other.

End to end: **order lines → basket table → per-product popularity → per-pair
co-occurrence counts → scored, directional pairs.** Each step reshapes the data into
what the next step needs; the whole job exists to turn raw counts (which just
flatter bestsellers) into lift (which measures whether two products are actually
related).

## Stage 3 — Neo4j write

Same script (`copurchase_lift.py`), gated behind `--write-neo4j` so the aggregation
can run standalone.

Writes via the **Neo4j Spark Connector** as originally spec'd, so the write is
distributed across executors rather than collected to the driver. The artifact
coordinates changed from the original plan, though:

- **The JAR is baked into the image (`spark/Dockerfile`), not fetched per run.**
  `/opt/spark/jars` is on the default classpath, so no `--packages` is needed. This
  is a deliberate simplification: `--packages` *cannot* be set from
  `SparkSession.config()` (Ivy resolution runs in the launcher process before the
  driver JVM starts, so it's silently ignored and surfaces later as
  `DATA_SOURCE_NOT_FOUND: org.neo4j.spark.DataSource`), and going through Ivy also
  required overriding the JVM's `user.home` — the image user's home is
  `/nonexistent` and `HOME` alone doesn't move Ivy's cache, since the JVM reads
  `user.home` from the OS passwd entry (and on Windows, passing
  `-Duser.home=/tmp` through `docker compose` additionally needed
  `MSYS_NO_PATHCONV=1` to stop Git Bash rewriting that path). Baking the JAR in
  removes all of it, plus the per-run Maven Central round-trip. Build verifies the
  published SHA-512.
  Only the connector's *runtime* config (`neo4j.url`, `neo4j.authentication.*`,
  `neo4j.database`) is set on the session builder.

1. **Delete stale edges first, plus create the index** — an `Append`/`CREATE` write
   never removes pairs that dropped below `min_support` since the last run. Both run
   via the connector's indexed `script.N` options, which execute once per write
   operation (not per partition), in ascending suffix order, *before* the main write:

```python
.option("script.1", "MATCH ()-[r:ALSO_BOUGHT]->() DELETE r")
.option("script.2", "CREATE INDEX also_bought_lift_idx IF NOT EXISTS FOR ()-[r:ALSO_BOUGHT]-() ON (r.lift)")
```

Note the older single-`script`-with-semicolons form is deprecated, and `script` and
`script.N` cannot be combined. The index mirrors where `similar_to_score_idx` lives
(in the compute step, not the base seed script — the relationship type doesn't exist
until this stage creates it) and `IF NOT EXISTS` makes re-running harmless.

2. Relationship write:

```python
(edges_df.coalesce(1)                       # parallel rel writes deadlock on node locks
  .write.format("org.neo4j.spark.DataSource")
  .mode("Append")
  .option("relationship", "ALSO_BOUGHT")
  .option("relationship.save.strategy", "keys")
  .option("relationship.source.labels", ":Product")
  .option("relationship.source.save.mode", "Match")   # never create products from tx data
  .option("relationship.source.node.keys", "src:productId")
  .option("relationship.target.labels", ":Product")
  .option("relationship.target.save.mode", "Match")
  .option("relationship.target.node.keys", "dst:productId")
  .option("relationship.properties", "count,lift,confidence")
  .save())
```

`Product.productId` uniqueness constraint already exists (from `seed-neo4j.cypher`).

3. **JVM hang after the write.** The connector's Neo4j driver leaves non-daemon
   threads running, so `spark.stop()` *and* the JVM's own shutdown both block
   forever — `spark-submit` hangs on an otherwise-successful job. Two non-obvious
   parts:
   - The hang is *inside* `spark.stop()`, so a force-exit placed after the graceful
     stop never executes.
   - Killing only the Python process (`os._exit`) is insufficient: the JVM is
     `spark-submit`'s (and the container's) main process. That alone yields a race —
     sometimes the JVM notices the dead py4j socket and exits with a spurious
     non-zero code, sometimes it hangs indefinitely.

   So on the `--write-neo4j` success path only, immediately after the write and
   before reaching `spark.stop()`: flush stdio, kill the JVM via
   `spark._jvm.System.exit(0)` (guarded — the gateway dies with it), then
   `os._exit(0)`. The write is already committed server-side. Failures still fall
   through to the graceful stop, preserving tracebacks and exit codes.

Environment: connection settings come from `spark/.env` via Compose's `env_file`,
with `NEO4J_URI` overridden to the `neo4j` service's container-name DNS.
`python-dotenv` is loaded best-effort for local runs only. With the JAR baked into
the image, the whole pipeline is a single flagless command — see `spark/README.md`.

## Stage 4 — API blend

`GetRecommendationsHandler` gains a query parameter `strategy=content|behavioral|hybrid`
(default `hybrid`). Hybrid query:

```cypher
MATCH (p:Product {productId: $productId})
CALL {
    WITH p MATCH (p)-[r:SIMILAR_TO]->(c:Product) WHERE r.score >= $minScore
    RETURN c, r.score * $wContent AS s
  UNION ALL
    WITH p MATCH (p)-[r:ALSO_BOUGHT]->(c:Product) WHERE r.lift > 1.0
    RETURN c, (1.0 - 1.0/r.lift) * $wBehavior AS s
}
WITH c, sum(s) AS score
OPTIONAL MATCH (c)-[:MADE_BY]->(b:Brand)
RETURN c.productId AS productId, c.productName AS productName,
       c.productDescription AS productDescription,
       b.brandId AS brandId, b.name AS brandName, score
ORDER BY score DESC SKIP $skip LIMIT $take
```

- `UNION ALL`, not `UNION` — the branches never produce identical rows; `UNION`'s
  dedup is wasted work.
- `1 - 1/lift` squashes unbounded lift into 0..1 so the two signals are commensurable.
- `wContent` / `wBehavior` are hardcoded in `RecommendationConstants` (0.5/0.5)
  next to `MinSimilarityScore` — no config knobs for weights nobody tunes.
- Cache key gains the strategy segment: `recs:{productId}:{strategy}:{page}:{pageSize}`.
- A product appearing on both edges scores from both branches — intentional: agreement
  between independent signals is the strongest recommendation.
- `content` and `behavioral` are the same query with one branch of the `CALL` dropped (and
  their own single-signal count query) — not just the hybrid query with a weight zeroed out,
  so each strategy only pays for the edge type it needs.
- `ALSO_BOUGHT` carries no `sameBrand` property (unlike `SIMILAR_TO`, where GDS writes it
  directly) — `behavioral` and `hybrid` derive it by comparing `p` and the candidate's
  `MADE_BY` brand inline.
- The count query for `hybrid` uses `UNION` (not `UNION ALL`) to get a *distinct*-product
  total for pagination — the scoring query still needs `UNION ALL` per the point above, so
  the two aren't the same query.
- Invalid `strategy` values 400 at the endpoint, before touching Neo4j.

### 4.1 Worked example

Real edge data for `productId=409` → `productId=410` (a shampoo pair, both signals present):

| Edge | Property | Value |
|------|----------|-------|
| `SIMILAR_TO` | `score` | `0.9618094563484192` |
| `ALSO_BOUGHT` | `lift` | `20.920026598546297` (348 shared orders) |

```
content contribution    = 0.9618094563484192 × 0.5            = 0.48090473
behavioral contribution = (1 − 1/20.920026598546297) × 0.5
                         = (1 − 0.04780) × 0.5 = 0.95220 × 0.5  = 0.47610
hybrid score             = 0.48090473 + 0.47610                = 0.95700...
```

The API returns `0.9570041849485741` for this pair under `strategy=hybrid`, `0.9618094563484192`
under `strategy=content`, and `20.920026598546297` under `strategy=behavioral` — same edges,
three different response shapes.

## Spark concepts demonstrated

| Concept | Where |
|---------|-------|
| DataFrame API (Spark SQL / Catalyst) | entire job |
| Literal SQL front-end | marginals via temp view (2.2) |
| Transformations vs actions, lineage | `cache()` on `clean` (2.1) |
| Shuffle vs broadcast joins | `F.broadcast(marg)` (2.4) |
| Adaptive Query Execution | self-join runtime-converted to `BroadcastHashJoin` at demo scale (2.5) |
| Skew mitigation | basket-size cap on the self-join key (2.1) |
| Early pruning before wide joins | `minSupport` filter placement (2.3) |
| Plan reading | `explain()` + README notes (2.5) |
| Window functions | `BOUGHT_NEXT` stretch goal |

Deliberately absent: raw RDD API (obsolete in application code) and streaming (out of
scope for a batch demo).

## Stretch goals (not in v1)

1. **`BOUGHT_NEXT`** — sequential purchases ("A, then B within 30 days") via
   `Window.partitionBy("customer_id").orderBy("ts")`. Better signal than co-purchase
   for accessories/consumables; the generator's timestamps already support it.
2. **Spark ↔ Neo4j read-back** — read `SIMILAR_TO` scores back into Spark via the
   connector's `query` read mode and join with behavioral scores offline.
3. **`Customer` nodes + `BOUGHT` edges** — only alongside a consuming feature
   (customer-level collaborative filtering endpoint, or GDS Louvain segmentation on
   the customer↔product bipartite graph). If added, aggregate to one
   `BOUGHT {purchaseCount, lastAt}` edge per customer–product pair; the graph does
   not need individual transactions.

## Decisions log

| Decision | Choice | Why |
|----------|--------|-----|
| Edge type | New `ALSO_BOUGHT`, never touch `SIMILAR_TO` | Opposite failure modes; blending needs both |
| Scoring | Lift (+ confidence), not raw counts | Raw counts rank bestsellers everywhere |
| Blend point | API query time | Keeps pipelines independent and re-runnable |
| Spark runtime | Bare PySpark `local[*]` | Demo scale; no infra to maintain |
| Catalog seed | New `generate_catalog.py`, plain Python | Repo has zero node-creation Cypher today — only constraints/indexes exist |
| Generator | Plain Python, graph-correlated, Zipf popularity | Uniform random data makes lift ≈ 1.0 everywhere |
| Stale edges | Delete-then-append, via connector `script.1` | `Append`/`Overwrite` never deletes dropped pairs |
| Rel write parallelism | `coalesce(1)` | Concurrent partitions deadlock on node locks |
| Docker image | `apache/spark:4.1.3-scala2.13-…` | `bitnami/spark` unmaintained (moved to `bitnamilegacy`); Scala 2.13 required by connector 6.x |
| Neo4j write path | Spark Connector `org.neo4j.connectors:spark:6.0.0-s_2.13` | Distributed write, no driver-side `collect()`; 6.0.0 supports Spark 4.x on Scala 2.13, so no Spark downgrade |
| Connector on classpath | Baked into image via `spark/Dockerfile`, not `--packages` | Kills 3 runtime flags at once (Ivy `user.home` hack, its Windows path-conversion workaround, per-run Maven fetch); `--packages` can't be set from `SparkSession.config()` anyway |
| Spark UI | `ports: 4040:4040` + `--service-ports` on `run`, no history server | Live during the run is enough to show stages/DAG; event log + history server is more infra than a demo needs |
| Post-write exit | `System.exit(0)` + `os._exit(0)` before `spark.stop()`, `--write-neo4j` only | Connector's non-daemon threads hang the JVM; killing only Python leaves the JVM (container PID 1) stuck |
| `strategy` query values | `content`/`behavioral`/`hybrid`, invalid → 400 at the endpoint | Fails fast before a Neo4j round-trip; matches the enum used internally |
| `sameBrand` on `ALSO_BOUGHT` results | Derived inline (`p`'s brand vs candidate's brand), not stored on the edge | Only `SIMILAR_TO` has it as a GDS-written property; storing it on `ALSO_BOUGHT` too would duplicate data already reachable via `MADE_BY` |
