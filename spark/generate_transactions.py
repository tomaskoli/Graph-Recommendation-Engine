"""Stage 1 of the Spark co-purchase pipeline (see doc/SPARK-PIPELINE.md).

Samples synthetic transaction baskets correlated with the catalog Stage 0 wrote to
Neo4j (category siblings + SIMILAR_TO neighbors, weighted by popularityWeight) and
writes them to data/transactions/ as Parquet for copurchase_lift.py to aggregate.
Uniform-random baskets would make every pair's co-purchase lift ~= 1.0 — the whole
point of Stage 2 is showing lift separating real signal from bestseller noise, so
the baskets have to be graph-correlated, not random.

Plain Python — not a Spark job, this only manufactures input.

Usage:
    python spark/generate_transactions.py
    python spark/generate_transactions.py --dry-run   # offline catalog, no Neo4j/disk
"""

import argparse
import itertools
import os
import random
from datetime import datetime, timedelta
from pathlib import Path

from dotenv import load_dotenv

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT_DIR = REPO_ROOT / "data" / "transactions"

# Fixed reference date, not "today" — keeps output reproducible given the same seed.
BASE_DATE = datetime(2026, 1, 1)
SPAN_DAYS = 180

# Rare cross-category bulk orders: the >20-item tail Stage 2's basket-size cap trims.
BULK_ORDER_PROB = 0.02
BULK_ORDER_SIZE_RANGE = (22, 45)

# Basket walk: keep extending with decaying probability — most baskets stay small,
# a shrinking few keep going. This is also where 1-item baskets come from (the
# continue check can fail on the very first draw), the other half of what Stage 2's
# cap trims.
CONTINUE_PROB_START = 0.75
CONTINUE_PROB_DECAY = 0.55
MAX_BASKET_SIZE = 20


def read_catalog_from_neo4j(uri, user, password, database):
    from neo4j import GraphDatabase

    driver = GraphDatabase.driver(uri, auth=(user, password))
    try:
        with driver.session(database=database) as session:
            products = session.execute_read(lambda tx: [dict(r) for r in tx.run(
                "MATCH (p:Product) RETURN p.productId AS productId, "
                "p.categoryId AS categoryId, p.popularityWeight AS popularityWeight"
            )])
            similar = session.execute_read(lambda tx: {r["productId"]: r["neighbors"] for r in tx.run(
                "MATCH (p:Product)-[:SIMILAR_TO]->(s:Product) "
                "RETURN p.productId AS productId, collect(s.productId) AS neighbors"
            )})
    finally:
        driver.close()

    if not products:
        raise RuntimeError("No products found in Neo4j — run generate_catalog.py first.")

    return {"products": products, "similar_by_product": similar}


def _offline_catalog():
    """Regenerates Stage 0's default catalog in-memory — for --dry-run only, no DB."""
    import generate_catalog as stage0

    seed_data = stage0.load_seed_data()
    catalog = stage0.build_catalog(seed=42, products_per_leaf=12, seed_data=seed_data)
    products = [
        {"productId": p["productId"], "categoryId": p["categoryId"], "popularityWeight": p["popularityWeight"]}
        for p in catalog["products"]
    ]
    return {"products": products, "similar_by_product": {}}  # SIMILAR_TO doesn't exist pre-GDS either way


def _build_indexes(catalog):
    products = catalog["products"]
    popularity_by_product = {p["productId"]: p["popularityWeight"] for p in products}
    category_by_product = {p["productId"]: p["categoryId"] for p in products}
    siblings_by_category = {}
    for p in products:
        siblings_by_category.setdefault(p["categoryId"], []).append(p["productId"])
    return popularity_by_product, siblings_by_category, category_by_product, catalog["similar_by_product"]


def _weighted_sample_no_replace(rng, items, weights, k):
    # Efraimidis-Spirakis weighted reservoir sampling — fine at catalog scale (~500 items).
    k = min(k, len(items))
    keyed = sorted(zip(items, weights), key=lambda iw: rng.random() ** (1.0 / iw[1]), reverse=True)
    return [item for item, _ in keyed[:k]]


def _sample_basket(rng, seed_product, popularity_by_product, siblings_by_category,
                    category_by_product, similar_by_product):
    if rng.random() < BULK_ORDER_PROB:
        all_products = list(popularity_by_product.keys())
        size = rng.randint(*BULK_ORDER_SIZE_RANGE)
        weights = [popularity_by_product[pid] for pid in all_products]
        return _weighted_sample_no_replace(rng, all_products, weights, size)

    basket = [seed_product]
    continue_prob = CONTINUE_PROB_START
    while rng.random() < continue_prob and len(basket) < MAX_BASKET_SIZE:
        last = basket[-1]
        candidates = similar_by_product.get(last) or siblings_by_category.get(category_by_product[last], [])
        candidates = [c for c in candidates if c not in basket]
        if not candidates:
            break
        weights = [popularity_by_product[c] for c in candidates]
        basket.append(rng.choices(candidates, weights=weights, k=1)[0])
        continue_prob *= CONTINUE_PROB_DECAY

    return basket


def generate_transactions(catalog, num_customers, num_orders, seed):
    rng = random.Random(seed)
    popularity_by_product, siblings_by_category, category_by_product, similar_by_product = _build_indexes(catalog)

    product_ids = list(popularity_by_product.keys())
    cum_weights = list(itertools.accumulate(popularity_by_product[pid] for pid in product_ids))

    rows = []
    for order_id in range(1, num_orders + 1):
        seed_product = rng.choices(product_ids, cum_weights=cum_weights, k=1)[0]
        basket = _sample_basket(rng, seed_product, popularity_by_product, siblings_by_category,
                                 category_by_product, similar_by_product)

        customer_id = rng.randint(1, num_customers)
        ts = BASE_DATE + timedelta(days=rng.uniform(0, SPAN_DAYS), seconds=rng.randint(0, 86399))
        for product_id in basket:
            rows.append({"order_id": order_id, "customer_id": customer_id, "product_id": product_id, "ts": ts})

    return rows


def _validate(rows, catalog):
    """Runnable self-check for the generation logic — no framework needed."""
    assert rows, "no transaction rows generated"

    product_ids = {p["productId"] for p in catalog["products"]}
    assert all(r["product_id"] in product_ids for r in rows), "unknown product_id in output"

    baskets = {}
    for r in rows:
        baskets.setdefault(r["order_id"], set()).add(r["product_id"])
    sizes = [len(items) for items in baskets.values()]
    assert min(sizes) >= 1

    single_item_pct = sum(1 for s in sizes if s == 1) / len(sizes)
    large_pct = sum(1 for s in sizes if s > 20) / len(sizes)
    print(f"{len(baskets)} orders, {len(rows)} order-lines, "
          f"avg basket size {sum(sizes) / len(sizes):.2f}, "
          f"{single_item_pct:.1%} single-item, {large_pct:.1%} over 20 items "
          f"(both are exactly what Stage 2's basket-size cap trims).")


def write_parquet(rows, output_dir: Path):
    import pyarrow as pa
    import pyarrow.parquet as pq

    output_dir.mkdir(parents=True, exist_ok=True)
    pq.write_table(pa.Table.from_pylist(rows), output_dir / "part-0000.parquet")
    print(f"Wrote {len(rows)} rows to {output_dir}")


def main():
    load_dotenv(Path(__file__).parent / ".env")

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--customers", type=int, default=5000)
    parser.add_argument("--orders", type=int, default=50000)
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT_DIR)
    parser.add_argument("--dry-run", action="store_true",
                         help="use an in-memory copy of the default catalog, skip Neo4j and disk")
    args = parser.parse_args()

    if args.dry_run:
        catalog = _offline_catalog()
    else:
        catalog = read_catalog_from_neo4j(
            os.environ.get("NEO4J_URI", "neo4j://localhost:7687"),
            os.environ.get("NEO4J_USER", "neo4j"),
            os.environ.get("NEO4J_PASSWORD", "12345678"),
            os.environ.get("NEO4J_DATABASE", "recommendation"),
        )

    rows = generate_transactions(catalog, args.customers, args.orders, args.seed)
    _validate(rows, catalog)

    if args.dry_run:
        return

    write_parquet(rows, args.output)


if __name__ == "__main__":
    main()
