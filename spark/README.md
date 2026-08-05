# Spark Co-Purchase Pipeline

See [doc/SPARK-PIPELINE.md](../doc/SPARK-PIPELINE.md) for the full design.

## Stage 0 — Synthetic catalog generator

Populates `CatalogSegment` / `Category` / `Brand` / `Product` / `Parameter`.
The repo's `deploy/scripts/seed-neo4j.cypher` only creates constraints and
indexes — no nodes — so this must run before anything else.

```bash
pip install -r spark/requirements.txt
cp spark/.env.example spark/.env   # already present locally with the compose defaults

# 1. constraints/indexes first
# (run deploy/scripts/seed-neo4j.cypher in Neo4j Browser)

# 2. catalog
python spark/generate_catalog.py
```

Connection comes from `spark/.env` (`NEO4J_URI` / `NEO4J_USER` / `NEO4J_PASSWORD` /
`NEO4J_DATABASE`, loaded via `python-dotenv`), not CLI flags — matches `Neo4jOptions`'
defaults (`neo4j://localhost:7687`, user `neo4j`, password `12345678`, database
`recommendation`) unless you edit the file.

Deterministic given `--seed` (default 42) and `--products-per-leaf` (default 12):
reruns `MERGE` onto the same node set instead of duplicating. The script clears
its own labels first, so it's always safe to rerun standalone.

Segments, category tree, brands, parameters, and product adjectives live in
[catalog_seed.yaml](catalog_seed.yaml) — edit that file to reshape the demo
catalog, no code changes needed. Override the path with `--seed-data`.

`--dry-run` generates and validates the catalog in memory without touching
Neo4j — useful to sanity-check the generation logic on a machine with no DB
running.

## Stage 1 — Synthetic transaction generator

Reads the catalog back from Neo4j (`productId`, `categoryId`, `popularityWeight`,
and any `SIMILAR_TO` edges — empty before the GDS script has run, which is fine,
the sampler falls back to same-category siblings) and samples baskets correlated
with it, writing `data/transactions/part-0000.parquet`.

```bash
python spark/generate_transactions.py
```

Options: `--customers` (default 5000), `--orders` (default 50000), `--seed`
(default 7), `--output` (default `data/transactions` at the repo root).

Baskets are built by a decaying random walk over category siblings / `SIMILAR_TO`
neighbors, weighted by `popularityWeight` — not uniform random, which would make
every pair's co-purchase lift ≈ 1.0 and defeat Stage 2 entirely. A small fraction
(2%) are cross-category "bulk order" outliers spanning 22–45 items. Between those
and baskets that stop after one item, there's always a single-item and an
over-20-item tail for Stage 2's `2 <= size <= 20` cap to actually trim — the
generator prints the resulting percentages so that's visible.

`--dry-run` swaps the Neo4j read for an in-memory copy of Stage 0's default
catalog and skips the Parquet write — no DB, no disk, same sampling logic.

## Stage 2 — Spark co-purchase lift job

Reads `data/transactions`, computes co-purchase lift and confidence per product
pair, and writes `data/edges` as Parquet. `copurchase_lift.py` imports nothing but
`pyspark` — no custom image needed to run it.

**Run via Docker (recommended)** — sidesteps JVM/loopback-socket quirks some
Windows setups hit with a locally installed JDK, and needs nothing but Docker:

```bash
docker compose -f deploy/Docker/docker-compose.spark.yml run --rm spark
```

This is a one-shot job, not a long-running service — `run --rm` executes it and
cleans up the container afterward. It joins the same `devnet` network as
`docker-compose.services.yml`, so once Stage 3's Neo4j write lands, the container
reaches Neo4j at `neo4j://neo4j:7687` (container-name DNS), not `localhost`.

**Run locally instead**, if you have a working JDK (11/17/21) — `pyspark` isn't in
`spark/requirements.txt` (Stage 0/1 don't need it, and the Docker image already
bundles it), so install it separately:

```bash
pip install pyspark==4.2.0
python spark/copurchase_lift.py
```

Either way, options: `--input` / `--output` (default `data/transactions` /
`data/edges` at the repo root), `--min-support` (default 5, the minimum
co-purchase count before a pair is scored). Prints the edge count, top-10 pairs
by lift, and the physical plan (`explain()`) — worth reading once: the marginal
joins show as `BroadcastHashJoin` as expected (`marg` is one row per product,
trivially small). At demo data scale, **Adaptive Query Execution (AQE) also
converts the self-join itself into a `BroadcastHashJoin`** once it sees how small
`clean` actually is post-filter — a real runtime re-optimization, not something
the query plans for ahead of time. On a much larger dataset where `clean` exceeds
`spark.sql.autoBroadcastJoinThreshold` (default 10MB), the self-join would fall
back to a shuffled `SortMergeJoin` on `order_id` instead — both are correct, AQE
is just picking the cheaper physical join for the actual data size at runtime.

Confirmed against a real run (50k orders, 137,970 order-lines): 3,342 directed
edges at `min_support=5`, `count`/`lift` symmetric across both directions of a
pair (e.g. 245↔250: `count=9`, `lift=286.3` either way) and `confidence`
correctly asymmetric (0.321 one way, 0.220 the other) — exactly the shape
Stage 2's design calls for.

Stages 3–4 (Neo4j write, API blend) are not implemented yet.
