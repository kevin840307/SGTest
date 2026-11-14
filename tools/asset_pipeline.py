#!/usr/bin/env python3
"""Single public runtime sprite asset pipeline.

Runtime classification is ownership/lifetime only:
- map: assets co-resident for one map generation;
- session: assets used for the whole character/session;
- on-demand: reserved for truly optional independently unloadable visuals.

Direction/action/part names remain semantic metadata inside a bundle and are
never public unload categories.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from asset_bundle_builder import build_bundle, compile_player_base, map_definitions
from build_enemy_map_palette import read_enemy_catalog, read_enemy_dat
from build_population import validate_stats

def resolve_server_dat(value: Path | None,
                       old_root: Path = Path(r"C:\SGO\Recovered\Old")) -> Path:
    if value is not None:
        resolved = value.resolve()
        if not resolved.is_dir():
            raise ValueError(f"server Dat missing: {resolved}")
        return resolved
    if not old_root.is_dir():
        raise ValueError("server Dat was not specified and Recovered/Old is unavailable")
    candidates = sorted(
        (child / "Dat").resolve()
        for child in old_root.iterdir()
        if child.is_dir() and (child / "Dat").is_dir()
    )
    if len(candidates) != 1:
        raise ValueError(
            f"cannot uniquely resolve server Dat under {old_root}; pass --server-dat"
        )
    return candidates[0]


def write_json(path: Path, value: dict, compact: bool = False) -> bytes:
    path.parent.mkdir(parents=True, exist_ok=True)
    text = (json.dumps(value, ensure_ascii=False, separators=(",", ":"))
            if compact else json.dumps(value, ensure_ascii=False, indent=2))
    raw = (text + "\n").encode("utf-8")
    temp = path.with_name(path.name + ".tmp")
    temp.write_bytes(raw)
    temp.replace(path)
    return raw


def original_population(server_dat: Path, map_id: int, bundle_relative: str) -> dict:
    source = server_dat / "Map" / f"Map{map_id}" / "Enemy.dat"
    catalog_path = server_dat / "Enemy" / "Enemy_Data.csv"
    spawns = read_enemy_dat(source)
    catalog = read_enemy_catalog(catalog_path)
    definitions = []
    for enemy_id in sorted({row["enemy_id"] for row in spawns}):
        if enemy_id not in catalog:
            raise ValueError(f"unresolved enemy definition {enemy_id}")
        source_definition = catalog[enemy_id]
        sprite = str(source_definition["sprite"]).upper()
        if not sprite.isascii() or not sprite.isalnum():
            raise ValueError(f"invalid sprite identity {sprite}")
        definitions.append({
            "id": enemy_id,
            "sprite": sprite,
            "atlas": bundle_relative,
            "stats": validate_stats(source_definition.get("stats")),
        })
    return {
        "version": 2,
        "map_id": map_id,
        "definitions": definitions,
        "spawns": [{
            "definition": row["enemy_id"],
            "p": [row["x"], row["y"]],
            "source_index": row["source_index"],
            "source_flags": row["flags"],
        } for row in spawns],
        "policy": "lifecycle bundle: map-co-resident sprites share one runtime atlas; "
                  "animation semantics remain sgtest.animation/v2",
        "sources": {
            source.relative_to(server_dat).as_posix(): hashlib.sha256(source.read_bytes()).hexdigest(),
            catalog_path.relative_to(server_dat).as_posix(): hashlib.sha256(catalog_path.read_bytes()).hexdigest(),
        },
    }


def build_map_bundle(source: Path, assets: Path, population: dict, map_id: int,
                     side: int | None = None) -> dict:
    relative = f"bundles/map/{map_id}/sprites.json"
    for row in population["definitions"]:
        row["atlas"] = relative
    definitions = map_definitions(source, population)
    result = build_bundle(
        definitions, source, assets / relative,
        f"map:{map_id}:sprites", "map", side=side,
    )
    return result


def build_session_bundle(source: Path, assets: Path,
                         side: int | None = None) -> dict:
    relative = "bundles/session/player-b-base.json"
    result = build_bundle(
        compile_player_base(source), source, assets / relative,
        "session:player:b:base", "session", side=side,
    )
    return result


def _remove_manifest_and_pages(path: Path) -> int:
    removed = 0
    if path.is_file():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            for page in data.get("pages", []):
                candidate = path.parent / str(page)
                if candidate.is_file():
                    candidate.unlink()
                    removed += 1
        except (OSError, ValueError, TypeError):
            pass
        path.unlink(missing_ok=True)
        removed += 1
    return removed


BASE_PLAYER_MANIFESTS = (
    "b-body.json", "b-leg.json", "b-head.json", "b-hair00.json",
    "b-l-arm.json", "b-r-arm.json", "b-death.json",
)


def prune_legacy_runtime_assets(assets: Path, map_sprites: list[str]) -> int:
    """Remove only generated manifests explicitly replaced by current bundles."""
    removed = 0
    enemy = assets / "enemy"
    for sprite in map_sprites:
        removed += _remove_manifest_and_pages(enemy / f"{sprite.lower()}.json")
    standard = assets / "player/b/standard"
    for name in BASE_PLAYER_MANIFESTS:
        removed += _remove_manifest_and_pages(standard / name)
    return removed


def build_all(source: Path, assets: Path, server_dat: Path, map_id: int,
              side: int | None = None, prune: bool = True) -> dict:
    if not source.is_dir():
        raise ValueError(f"private extracted source missing: {source}")
    if not server_dat.is_dir():
        raise ValueError(f"server Dat missing: {server_dat}")
    map_relative = f"bundles/map/{map_id}/sprites.json"
    population = original_population(server_dat, map_id, map_relative)
    map_result = build_map_bundle(source, assets, population, map_id, side)
    session_result = build_session_bundle(source, assets, side)

    population_raw = write_json(assets / f"maps/{map_id}/population.json", population, compact=True)
    index = {
        "version": 1,
        "public_grouping": "ownership-lifetime-only",
        "packing_policy": "normalized clip atomic; 2048 selected by lifecycle/render A/B",
        "bundles": [
            {
                "id": map_result["bundle_id"],
                "scope": "map",
                "manifest": f"bundles/map/{map_id}/sprites.json",
                "members": map_result["entities"],
                "side": map_result["side"],
                "pages": map_result["pages"],
                "rgba_bytes": map_result["rgba_bytes"],
            },
            {
                "id": session_result["bundle_id"],
                "scope": "session",
                "manifest": "bundles/session/player-b-base.json",
                "members": session_result["entities"],
                "side": session_result["side"],
                "pages": session_result["pages"],
                "rgba_bytes": session_result["rgba_bytes"],
            },
        ],
        "on_demand_rule": "create only when usage is infrequent AND asset can be independently unloaded",
    }
    write_json(assets / "bundles/index.json", index)
    removed = (prune_legacy_runtime_assets(
        assets, [str(row["sprite"]) for row in population["definitions"]]
    ) if prune else 0)
    report = {
        "version": 1,
        "map_id": map_id,
        "side_override": side,
        "population_sha256": hashlib.sha256(population_raw).hexdigest(),
        "definitions": len(population["definitions"]),
        "spawns": len(population["spawns"]),
        "map_bundle": map_result,
        "session_bundle": session_result,
        "legacy_generated_files_removed": removed,
        "server_protocol_changed": False,
        "normalized_animation_semantics_preserved": True,
    }
    Path("reports").mkdir(exist_ok=True)
    write_json(Path("reports/asset_pipeline.json"), report)
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path(".cache/client"))
    parser.add_argument("--assets", type=Path, default=Path("client/assets/generated"))
    parser.add_argument("--server-dat", type=Path,
                        help="override Server Dat; otherwise auto-discover under Recovered/Old")
    parser.add_argument("--map", type=int, default=13)
    parser.add_argument("--side", type=int, choices=(512, 1024, 2048, 4096),
                        help="research override; omit for measured automatic selection")
    parser.add_argument("--keep-legacy-generated", action="store_true")
    args = parser.parse_args()
    server_dat = resolve_server_dat(args.server_dat)
    result = build_all(
        args.source.resolve(), args.assets.resolve(), server_dat,
        args.map, args.side, prune=not args.keep_legacy_generated,
    )
    print(json.dumps({
        "map_bundle": {k: result["map_bundle"][k] for k in ("side", "pages", "rgba_bytes")},
        "session_bundle": {k: result["session_bundle"][k] for k in ("side", "pages", "rgba_bytes")},
        "definitions": result["definitions"],
        "spawns": result["spawns"],
        "legacy_generated_files_removed": result["legacy_generated_files_removed"],
    }, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
