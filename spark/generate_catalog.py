"""Stage 0 of the Spark co-purchase pipeline (see doc/SPARK-PIPELINE.md).

Fills CatalogSegment / Category / Brand / Product / Parameter — nodes the repo's
deploy/scripts/seed-neo4j.cypher never creates (constraints/indexes only). Run
seed-neo4j.cypher first, then this script, before generate_transactions.py.

Plain Python + the neo4j driver — not a Spark job, this only manufactures input.
Segments/categories/brands/parameters live in catalog_seed.yaml, not in this file —
edit that to reshape the demo catalog.

Usage:
    pip install -r spark/requirements.txt
    python spark/generate_catalog.py
    python spark/generate_catalog.py --dry-run   # generate + validate, skip Neo4j
"""

import argparse
import os
import random
from pathlib import Path

import yaml
from dotenv import load_dotenv

DEFAULT_SEED_DATA_PATH = Path(__file__).parent / "catalog_seed.yaml"

MIN_PARAMS_PER_PRODUCT = 2
MAX_PARAMS_PER_PRODUCT = 4


def load_seed_data(path: Path = DEFAULT_SEED_DATA_PATH) -> dict:
    with open(path, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)

    assert len(data["parameters"]) >= MIN_PARAMS_PER_PRODUCT, \
        f"catalog_seed.yaml needs at least {MIN_PARAMS_PER_PRODUCT} parameters"
    # parameterId -> (parameterName, possible values). parameterId is the "kind" key;
    # the (parameterId, value) pair is the node identity, matching the composite
    # uniqueness constraint in seed-neo4j.cypher — many products share the same node.
    data["parameters"] = {p["id"]: (p["name"], p["values"]) for p in data["parameters"]}
    return data


def build_catalog(seed: int, products_per_leaf: int, seed_data: dict):
    """Deterministic given (seed, products_per_leaf, seed_data) — reruns MERGE onto the same nodes."""
    rng = random.Random(seed)

    segments = [{"segmentId": i + 1, "segmentName": name} for i, name in enumerate(seed_data["segments"])]
    segment_id_by_name = {s["segmentName"]: s["segmentId"] for s in segments}

    categories = []
    leaves = []  # (categoryId, categoryName)
    category_id = 0
    for segment_name, roots in seed_data["category_tree"].items():
        for root_name, leaf_names in roots.items():
            category_id += 1
            root_id = category_id
            categories.append({
                "categoryId": root_id,
                "categoryName": root_name,
                "parentCategoryId": None,
                "segmentId": segment_id_by_name[segment_name],
            })
            for leaf_name in leaf_names:
                category_id += 1
                categories.append({
                    "categoryId": category_id,
                    "categoryName": leaf_name,
                    "parentCategoryId": root_id,
                    "segmentId": None,
                })
                leaves.append((category_id, leaf_name))

    brands = [{"brandId": i + 1, "name": name} for i, name in enumerate(seed_data["brands"])]

    # Zipf-ish popularity: random rank assignment, weight = 1 / rank^s. Persisted
    # on Product so generate_transactions.py reuses the same skew instead of
    # resampling independently (uniform-random baskets make co-purchase lift ~= 1
    # everywhere, which defeats the point of the demo).
    total_products = len(leaves) * products_per_leaf
    ranks = list(range(1, total_products + 1))
    rng.shuffle(ranks)

    products = []
    parameters = {}  # (parameterId, value) -> node dict, dict dedups shared nodes
    has_parameter = []
    product_id = 0
    for leaf_id, leaf_name in leaves:
        for i in range(products_per_leaf):
            product_id += 1
            brand = rng.choice(brands)
            products.append({
                "productId": product_id,
                "productName": f"{brand['name']} {leaf_name} {rng.choice(seed_data['product_adjectives'])} {i + 1}",
                "productDescription": f"{leaf_name} by {brand['name']}, built for everyday use.",
                "brandId": brand["brandId"],
                "categoryId": leaf_id,
                "popularityWeight": 1.0 / (ranks[product_id - 1] ** seed_data["zipf_s"]),
            })

            param_defs = seed_data["parameters"]
            k = rng.randint(MIN_PARAMS_PER_PRODUCT, min(MAX_PARAMS_PER_PRODUCT, len(param_defs)))
            param_ids = rng.sample(list(param_defs.keys()), k=k)
            for param_id in param_ids:
                param_name, values = param_defs[param_id]
                value = rng.choice(values)
                parameters[(param_id, value)] = {
                    "parameterId": param_id,
                    "parameterName": param_name,
                    "value": value,
                }
                has_parameter.append({"productId": product_id, "parameterId": param_id, "value": value})

    return {
        "segments": segments,
        "categories": categories,
        "brands": brands,
        "products": products,
        "parameters": list(parameters.values()),
        "has_parameter": has_parameter,
    }


def _validate(catalog):
    """Runnable self-check for the generation logic — no framework needed."""
    product_ids = [p["productId"] for p in catalog["products"]]
    assert len(product_ids) == len(set(product_ids)), "duplicate productId"

    leaf_ids = {c["categoryId"] for c in catalog["categories"] if c["parentCategoryId"] is not None}
    root_ids = {c["categoryId"] for c in catalog["categories"] if c["parentCategoryId"] is None}
    assert leaf_ids.isdisjoint(root_ids)
    assert all(p["categoryId"] in leaf_ids for p in catalog["products"]), "product assigned to a root category"

    root_segment_ids = {c["categoryId"]: c["segmentId"] for c in catalog["categories"] if c["parentCategoryId"] is None}
    assert all(sid is not None for sid in root_segment_ids.values()), "root category missing IN_SEGMENT"
    assert all(c["segmentId"] is None for c in catalog["categories"] if c["parentCategoryId"] is not None), \
        "leaf category unexpectedly has a segment"

    brand_ids = {b["brandId"] for b in catalog["brands"]}
    assert all(p["brandId"] in brand_ids for p in catalog["products"])

    for p in catalog["products"]:
        assert 0.0 < p["popularityWeight"] <= 1.0

    hp_by_product = {}
    for hp in catalog["has_parameter"]:
        hp_by_product.setdefault(hp["productId"], set()).add(hp["parameterId"])
    assert all(2 <= len(v) <= 4 for v in hp_by_product.values()), "parameter count out of range"


def _run_batched(session, query, rows, batch_size=1000):
    for start in range(0, len(rows), batch_size):
        batch = rows[start:start + batch_size]
        session.execute_write(lambda tx, b=batch: tx.run(query, rows=b).consume())


def write_catalog(uri, user, password, database, catalog):
    from neo4j import GraphDatabase

    driver = GraphDatabase.driver(uri, auth=(user, password))
    try:
        with driver.session(database=database) as session:
            session.execute_write(lambda tx: tx.run(
                "MATCH (n) WHERE n:Product OR n:Category OR n:Brand OR n:Parameter OR n:CatalogSegment "
                "DETACH DELETE n"
            ).consume())

            # nodes before relationships (MATCH in relationship writes would miss them otherwise)
            _run_batched(session, """
                UNWIND $rows AS row
                MERGE (s:CatalogSegment {segmentId: row.segmentId})
                SET s.segmentName = row.segmentName
            """, catalog["segments"])

            _run_batched(session, """
                UNWIND $rows AS row
                MERGE (c:Category {categoryId: row.categoryId})
                SET c.categoryName = row.categoryName,
                    c.parentCategoryId = row.parentCategoryId,
                    c.created_at = datetime()
            """, catalog["categories"])

            _run_batched(session, """
                UNWIND $rows AS row
                MERGE (b:Brand {brandId: row.brandId})
                SET b.name = row.name,
                    b.created_at = datetime()
            """, catalog["brands"])

            _run_batched(session, """
                UNWIND $rows AS row
                MERGE (p:Product {productId: row.productId})
                SET p.productName = row.productName,
                    p.productDescription = row.productDescription,
                    p.brandId = row.brandId,
                    p.categoryId = row.categoryId,
                    p.popularityWeight = row.popularityWeight,
                    p.created_at = datetime()
            """, catalog["products"])

            _run_batched(session, """
                UNWIND $rows AS row
                MERGE (param:Parameter {parameterId: row.parameterId, value: row.value})
                SET param.parameterName = row.parameterName,
                    param.created_at = datetime(),
                    param.updated_at = datetime()
            """, catalog["parameters"])

            # relationships
            leaf_rows = [c for c in catalog["categories"] if c["parentCategoryId"] is not None]
            _run_batched(session, """
                UNWIND $rows AS row
                MATCH (child:Category {categoryId: row.categoryId})
                MATCH (parent:Category {categoryId: row.parentCategoryId})
                MERGE (child)-[:CHILD_OF]->(parent)
            """, leaf_rows)

            root_rows = [c for c in catalog["categories"] if c["parentCategoryId"] is None]
            _run_batched(session, """
                UNWIND $rows AS row
                MATCH (c:Category {categoryId: row.categoryId})
                MATCH (s:CatalogSegment {segmentId: row.segmentId})
                MERGE (c)-[:IN_SEGMENT]->(s)
            """, root_rows)

            _run_batched(session, """
                UNWIND $rows AS row
                MATCH (p:Product {productId: row.productId})
                MATCH (c:Category {categoryId: row.categoryId})
                MERGE (p)-[:BELONGS_TO]->(c)
            """, catalog["products"])

            _run_batched(session, """
                UNWIND $rows AS row
                MATCH (p:Product {productId: row.productId})
                MATCH (b:Brand {brandId: row.brandId})
                MERGE (p)-[:MADE_BY]->(b)
            """, catalog["products"])

            _run_batched(session, """
                UNWIND $rows AS row
                MATCH (p:Product {productId: row.productId})
                MATCH (param:Parameter {parameterId: row.parameterId, value: row.value})
                MERGE (p)-[:HAS_PARAMETER]->(param)
            """, catalog["has_parameter"])

            counts = session.execute_read(lambda tx: tx.run("""
                MATCH (s:CatalogSegment) WITH count(s) AS segments
                MATCH (c:Category) WITH segments, count(c) AS categories
                MATCH (b:Brand) WITH segments, categories, count(b) AS brands
                MATCH (p:Product) WITH segments, categories, brands, count(p) AS products
                MATCH (param:Parameter) WITH segments, categories, brands, products, count(param) AS parameters
                RETURN segments, categories, brands, products, parameters
            """).single())
            print(f"Wrote: {dict(counts)}")
    finally:
        driver.close()


def main():
    load_dotenv(Path(__file__).parent / ".env")  # before argparse so its defaults see the values

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--products-per-leaf", type=int, default=12)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--seed-data", type=Path, default=DEFAULT_SEED_DATA_PATH,
                         help="path to the catalog_seed.yaml-shaped data file")
    parser.add_argument("--dry-run", action="store_true", help="generate + validate only, skip Neo4j")
    args = parser.parse_args()

    seed_data = load_seed_data(args.seed_data)
    catalog = build_catalog(args.seed, args.products_per_leaf, seed_data)
    _validate(catalog)
    print(f"Generated {len(catalog['products'])} products across "
          f"{len(catalog['categories'])} categories, {len(catalog['brands'])} brands, "
          f"{len(catalog['parameters'])} distinct parameter values.")

    if args.dry_run:
        return

    write_catalog(
        os.environ.get("NEO4J_URI", "neo4j://localhost:7687"),
        os.environ.get("NEO4J_USER", "neo4j"),
        os.environ.get("NEO4J_PASSWORD", "12345678"),
        os.environ.get("NEO4J_DATABASE", "recommendation"),
        catalog,
    )


if __name__ == "__main__":
    main()
