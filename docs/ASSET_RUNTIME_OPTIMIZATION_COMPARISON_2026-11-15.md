# SGTest Runtime Asset Optimization Comparison — 2026-11-15

Status: **measured isolated benchmark; renderer fidelity and macOS release still require verification**.

## Scope and fairness

- Windows, Godot 4.7.2, GL Compatibility, 1280×720, VSync disabled, 120 frames, 30 warmup, 3 isolated repeats per case (median).
- Windows process-tree Peak RSS includes the console launcher and actual Godot process.
- Texture memory is Godot's reported delta (not measured physical dedicated VRAM).
- Recovered Model1 uses its own imported textures, shared SpriteFrames, AtlasTexture and AnimatedSprite2D.
- Raw SGTest uses PNG → ImageTexture → EntityAtlas/direct Canvas.
- Imported SGTest uses Godot VRAM-compressed Texture2D/ResourceLoader and an optimized frame table/draw path.
- Logical assets remain independent. No Map+Enemy, Enemy+Enemy, equipment+player cross-asset atlas.
- File size and blank space are **not ranking criteria**.
- All timing rows below are isolated rendering benchmarks, **not full gameplay or multiplayer simulation**.

## A. Main controlled comparison: 12 kinds Enemy × 1000

Source: `.cache/asset_packing_benchmark/imported_compare/summary.json`.

| Candidate | Peak RSS MiB | Texture delta MiB | Load ms | P95 ms | P99 ms | Draw P95 |
|---|---:|---:|---:|---:|---:|---:|
| Recovered Model1 | 213.6 | 62.3 | 63.3 | 2.345 | 2.868 | 1001 |
| Raw Height-P0 + original direct | 190.2 | 231.2 | 594.2 | 5.136 | 5.294 | 1000 |
| Raw POT rectangle + direct | 204.4 | 372.0 | 705.7 | 4.998 | 5.337 | 1000 |
| Compressed P0 + original direct | 172.6 | 58.0 | **40.2** | 4.939 | 5.175 | 1000 |
| Compressed POT + original direct | 174.7 | 93.0 | 71.6 | 5.157 | 5.409 | 1000 |
| **Compressed P0 + fast direct** | **167.8** | **58.0** | 47.5 | 3.735 | 3.967 | 1000 |
| Compressed POT + fast direct | 171.9 | 93.0 | 73.3 | 3.525 | 3.650 | 1000 |
| Compressed P0 + sprite benchmark | 171.4 | 58.0 | 43.7 | **2.091** | **2.244** | 1000 |
| Compressed POT + sprite benchmark | 173.2 | 93.0 | 64.9 | 2.187 | 2.432 | 1000 |

**Controlled effects**:
- Changing only raw PNG/ImageTexture to compressed imported texture with the same direct renderer: texture 231.2 → 58.0 MiB, load 594.2 → 40.2 ms, P95 5.136 → 4.939 ms.
- Keeping compressed assets but replacing dictionary-heavy direct draw with precompiled fast draw: P95 4.939 → 3.735 ms.
- Sprite benchmark is the fastest measured but is **not yet functionally equivalent**: its simplified SpriteFrames reconstruction omits the source frame's trim/origin in the displayed sprite offset and does not implement the full ACT/logical-view/mirror semantics. Its world Y-sort/overlap integration has not been proven.

## B. Representative type/size workloads

Source: `.cache/asset_packing_benchmark/optimized_matrix/summary.json`, **a separate three-repeat run**. Avoid mixing timings across different runs as if paired.

| Scenario | Candidate | Peak RSS MiB | Texture delta MiB | Load ms | P95 ms |
|---|---|---:|---:|---:|---:|
| Enemy small ×1000 | Recovered | 209.1 | 0.5 | 1.5 | 1.111 |
|  | Raw Height-P0 | 147.8 | 1.2 | 4.2 | 2.426 |
|  | Compressed P0 fast | **146.8** | **0.3** | 5.9 | **0.760** |
| Enemy medium ×1000 | Recovered | 209.1 | 1.2 | 18.1 | 1.037 |
|  | Raw Height-P0 | 151.5 | 3.8 | 12.6 | 2.525 |
|  | Compressed P0 fast | **147.2** | **1.0** | **6.5** | **0.836** |
| Enemy large ×1000 | Recovered | 209.4 | 4.2 | 34.6 | **1.564** |
|  | Raw Height-P0 | 157.4 | 15.2 | 40.8 | 2.506 |
|  | Compressed P0 fast | **151.0** | **3.8** | **16.5** | 1.938 |
| Enemy huge ×1000 | Recovered | 220.9 | 15.3 | 39.4 | **2.047** |
|  | Raw Height-P0 | 183.5 | 57.8 | 127.0 | 2.489 |
|  | Compressed P0 fast | **157.5** | **14.5** | **35.8** | 2.730 |
| Enemy 4 kinds ×1000 | Recovered | 213.0 | 21.2 | **32.0** | 2.672 |
|  | Raw Height-P0 | 198.6 | 78.0 | 193.6 | 4.401 |
|  | Compressed P0 fast | **164.0** | **19.5** | 45.1 | **2.496** |
| Weapon 4 kinds ×300 | Recovered | 209.4 | 4.8 | 41.8 | 1.052 |
|  | Raw Height-P0 | 151.8 | 3.4 | **9.7** | 1.412 |
|  | Compressed P0 fast | **146.9** | **0.9** | 23.6 | **0.922** |
| Equipment 4 kinds ×300 | Recovered | 213.6 | 28.3 | 81.6 | 1.170 |
|  | Raw Height-P0 | 168.0 | 26.9 | 69.5 | 1.474 |
|  | Compressed P0 fast | **156.4** | **6.8** | **53.9** | **0.911** |
| Scenery 4 kinds ×600 | Raw Height-P0 | 159.6 | 16.2 | 47.4 | 2.669 |
|  | Compressed P0 fast | **152.3** | **4.1** | **31.9** | **2.366** |

Recovered loose-scenery renderer is not equivalent to SGTest's isolated scenery canvas and is omitted.

## C. POT rectangular buckets, e.g. 1024×256 or 2048×512

Source: `.cache/asset_packing_benchmark/optimized_matrix/summary.json`.

| Workload | Compressed P0 Texture MiB | Compressed POT Texture MiB | Compressed P0 load ms | Compressed POT load ms | P0 P95 ms | POT P95 ms |
|---|---:|---:|---:|---:|---:|---:|
| Enemy huge ×1000 | 14.5 | 21.3 | 35.8 | 43.4 | 2.730 | 2.715 |
| Enemy 4 kinds ×1000 | 19.5 | 28.3 | 45.1 | 76.4 | 2.496 | 2.727 |
| Enemy 12 kinds ×1000 | 58.0 | 93.0 | 109.2 | 123.1 | 2.765 | 2.592 |
| Weapon 4 kinds ×300 | 0.9 | 1.2 | 23.6 | 24.0 | 0.922 | 0.921 |
| Equipment 4 kinds ×300 | 6.8 | 12.4 | 53.9 | 55.6 | 0.911 | 0.913 |
| Scenery 4 kinds ×600 | 4.1 | 5.8 | 31.9 | 49.0 | 2.366 | 2.378 |

**Decision**: POT rectangles are a valid candidate but **not the default**. Their small P95 wins in some scenarios are inconsistent between independent runs, while texture usage and loading are consistently more expensive. Keep POT as a constrained/hardware-specific fallback when an actual measured advantage exists, not a required bucket for every asset.

## D. Compression format test

Source: `.cache/asset_packing_benchmark/compression_matrix/summary.json`.

| Enemy 12 kinds, compressed fast | Texture MiB | Load ms | P95 ms |
|---|---:|---:|---:|
| Candidate labeled BPTC | 58.0 | 104.8 | 2.495 |
| Candidate labeled S3TC | 58.0 | 106.5 | 2.507 |
| Basis Universal | 57.9 | 494.5 | 2.508 |

**Important technical correction:** The measurements use the GL Compatibility renderer. In this renderer, Godot ignores the high-quality BPTC option and normally selects S3TC for desktop textures. The demonstrated benefit is **VRAM-compressed imported textures**, not a verified BC7/BPTC-versus-S3TC win. See the Godot Importing Images documentation.

## E. Recommended production approach

1. Keep `Enemy/<id>/` and every other logical asset independent.
2. Use Height-descending, variable-sized, single-page atlas (`<=4096`) as primary packing. Do not force power-of-two rectangles or cross-asset bucket merging.
3. Use imported VRAM-compressed Texture2D via ResourceLoader in an **external PCK** rather than bundling all art inside the executable. Pixel-sensitive UI/icons remain lossless.
4. Keep Client/Server and renderer FPS independent.
5. Apply the precompiled frame table/low-allocation direct draw route **only when original ACT timeline, facing/mirror, trim/pivot, action speed and world Y-order remain correct**.
6. Do not switch the entire game to AnimatedSprite2D on the basis of the isolated 2.091 ms result alone: its benchmark version has incomplete source/paper-doll semantics and does not prove gameplay Y-order parity.
7. No real macOS export/runtime results yet. Windows/macOS can share the same logical asset contract, but the compressed texture variants and exported pack must be validated on actual target hardware.
8. P0 with zero padding requires rendering/compression edge-artifact checks at different scaling/filter modes; UV clipping alone does not prove block-compressed sprite edges visually match the original.

## F. Validation and implementation state

- Per-asset Height-Dense production builder and external compressed PCK pipeline have **working-tree modifications**; do not claim these are committed/pushed or fully released until separately verified.
- Python converter/tests: **128 PASS**.
- Python root contracts: **37 PASS, 1 skipped**.
- Godot Client scripts: **15/15 PASS**.
- C# Server tests: **376 PASS**.
- Windows compressed external PCK smoke artifact exists; final Windows EXE export and macOS runtime validation remain distinct gates.
- Full source archive conversion, deterministic 7,890-asset pixel/pivot/hash validation and production multiplayer/AFK long-run tests have not been re-certified for the new pipeline.

This document records an isolated performance decision, not an assertion that all game and export tasks are complete.
