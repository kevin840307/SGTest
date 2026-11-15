# Map13 Mixed Renderer Validation — 2026-11-15

## Scope

This is an **isolated visual-runtime benchmark**, not a full online/MMORPG gameplay certification. It compares Recovered's real MonsterLoader/SpriteFrames/AnimatedSprite2D with SGTest's independent Height-Dense per-Enemy atlas, imported VRAM-compressed textures, and a corrected shared SpriteFrames/AnimatedSprite2D render path.

The benchmark uses **real Map13 world.json placement and 41 independent scenery objects (16 unique asset identities)** around world coordinates (6616,4728). It adds **20 procedurally rendered player placeholders**, **500 MultiMesh drop markers**, **12 benchmark Enemy identities**, per-frame movement, and stable-count **5% AOI despawn/respawn every 20 frames**. Player paper-doll and ground drops are only workload approximations; network packets, map terrain tile drawing and actual server AI are NOT included.

Each asset remains independent. Never merge Map+Enemy, Enemy+Enemy, or Player/Equipment assets to improve these numbers.

## Benchmark conditions

- Godot 4.7.2 / OpenGL Compatibility, Windows RTX 3050
- 1280×720, VSync off, uncapped rendering
- 150 measured/warmup frames, first 35 warmup, three separate process launches per candidate and scenario
- Recovered animation uses its actual MonsterLoader; SGTest loads compressed imported atlases in a separate test Godot project
- Same logical 14 action/direction view set per Enemy type (including mirrored idle/walk/attack)
- Continuous independent movement, frame changes, **5% AOI churn every 20 frames**, shared world Y-sort
- Process tree RSS monitored (launcher + renderer); GPU texture metric is Godot Texture delta, **not physical VRAM**
- Figures are median of independent trials; reported frame p95/p99 include AOI churn and action transitions; order reversed in second repeat

## Comparison

Lower is better. RAM/Texture in MiB; times in ms.

| Scenario | Mode | Peak RSS | Texture Δ | Enemy palette load | Frame p95 | Frame p99 | Draw p95 | >16.7ms frame count |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| 12 Enemy types ×300 | Recovered | 214.1 | 66.7 | 59.4 | 1.498 | 1.663 | 342 | 0 |
| | **SGTest Sprite** | **164.3** | **62.4** | 62.3 | **1.402** | **1.535** | 341 | 0 |
| 12 Enemy types ×1000 | Recovered | 217.6 | 66.7 | **45.6** | 3.640 | 3.901 | 947 | 0 |
| | **SGTest Sprite** | **170.7** | **62.4** | 57.5 | **3.421** | **3.865** | 946 | 0 |
| 12 Enemy types ×2000 | Recovered | 219.8 | 66.7 | **50.2** | 7.470 | 10.361 | 1843 | 1 |
| | **SGTest Sprite** | **176.6** | **62.4** | 55.1 | **6.900** | **7.728** | 1843 | 0 |

The 2000-Enemy SGTest Sprite variant improves frame p95 by approximately 7.6%, p99 by 25.4%, and Peak RSS by 19.7% compared with the corresponding Recovered test. At 300/1000 the timing advantage is smaller and should be treated as workload-sensitive. Loading speed does **not** consistently beat Recovered.

AOI churn and action switch counts (same in both modes):

| Visible Enemy target | AOI removed | AOI created | Action switches |
|---:|---:|---:|---:|
| 300 | 105 | 105 | 215 |
| 1000 | 350 | 350 | 715 |
| 2000 | 700 | 700 | 1430 |

## Correctness and script improvements

- The old prototype applied Recovered `flip_h` using an incorrect numerical direction shortcut. It has been replaced with **the actual Recovered MonsterEntity direction mapping** (idle facing4 -> IDLE3 mirrored; walking facing6 -> WALK2 mirrored; attack facing6 -> ATTACK1 mirrored) and matching SGTest logical-view mirror metadata.
- The runtime now updates `flip_h` when the action/direction changes, rather than only when an entity is created.
- Monster-specific action table references remain aligned to the spawned sprite's actual Enemy ID after AOI churn.
- Godot test runner **fails closed** if the required action/view list is incomplete, an imported file is missing, AOI counts mismatch, scenery is absent, Godot logs an error, or expected output is not generated. It deletes a previous run's output before executing so stale JSON cannot be mistaken for a success.
- Python runner monitors the **complete process tree**, reports Windows RSS, CPU and frame metrics, accepts configurable counts/repeats/frames/output path and environment overrides for Godot/Recovered/SGTest paths.
- `ysort_layer_parity.gd`: **5/5 pixel-observed occlusion cases pass** for scenery, player, Enemy and removal/reveal order. It verifies that all categories must share the same Y-sort world.
- Earlier `visual_pixel_parity.gd`: **96/96** compressed-atlas visual comparisons, **36 flipped** cases, **0 differing pixels** on the chosen frames. Earlier `renderer_lifecycle.gd`: 5/5 unload cycles return texture and node counts to the isolated baseline.
- Regression tests in this session: **Python 128/128**, root tools **37 PASS / 1 skipped**, **Godot 15/15**, **C# Server 376/376**.

## How to rerun

Run from the source checkout in Windows PowerShell (Godot 4.7.2 CLI and psutil are required):

```powershell
python tools/asset_converter/benchmarks/packing/run_map13_mix.py
```

Target a quick smoke without overwriting the full report:

```powershell
python tools/asset_converter/benchmarks/packing/run_map13_mix.py --counts 300 --repeats 1 --frames 90 --output-dir .cache/map13_smoke
```

Default inputs are: `C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe`, `C:\SGO\Recovered\client`, `.cache/bptc_matrix` and `client/assets`. Override through environment variables `SGTEST_GODOT`, `SGTEST_RECOVERED_CLIENT`, `SGTEST_COMPRESSED_PROJECT`, `SGTEST_ASSETS_ROOT` if paths differ.

Pixel Y-sort verification:

```powershell
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --rendering-method gl_compatibility --path .cache/bptc_matrix --script tools/asset_converter/benchmarks/packing/ysort_layer_parity.gd -- .cache/ysort_parity.json
```

Raw results: `.cache/asset_packing_benchmark/map13_mix_v1/summary.json`. This stays ignored/local and is not added to Git.

## Production decision and remaining work

**Keep SGTest per-asset Height-Dense + imported compressed Texture2D. Trial the shared SpriteFrames/AnimatedSprite2D path for Enemy instances in the real world renderer**. The mixed benchmark now includes scenery and AOI churn and favors this Sprite variant, while its RAM footprint remains below the Recovered project in this environment.

The current production client **still draws world scenery, player and monsters through a single ordered CanvasItem**. Directly inserting Sprite children would break object interleaving unless the world renderer is restructured to put all visible sorted categories under a common Y-sort root or equivalent correct ordering. This is the **next integration blocker** and should be resolved before switching the client.

Not yet proven: actual server AOI and packets, source-perfect terrain rendering, player/weapon/equipment paper-doll, original animation interruption/attack-hit-death timing in live gameplay, action audio, camera zoom or high-DPI interpolation, Unfocus/AFK and 30 complete map/relogin cycles in the finished client, Windows Release EXE/PCK and real macOS GPU tests. Do not label these completed because this isolated benchmark passes.
