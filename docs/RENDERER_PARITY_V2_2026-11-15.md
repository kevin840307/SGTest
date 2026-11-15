# SGTest Renderer parity vs Recovered Model1 — 2026-11-15

Status: **isolated rendering and lifecycle validation complete; not yet a production Client renderer switch**.

## Goal

Choose the renderer users will feel is most responsive, while staying within acceptable runtime RAM/Texture memory. Retain logical asset boundaries: one Enemy per asset; no Map+Enemy or Enemy+Enemy merged atlas. Windows and macOS share the logical asset contract but macOS has not been measured.

### Runtime candidates

1. Recovered-native: actual Recovered `MonsterLoader.get_monster_frames(id,true)`, imported compressed Texture2D, shared `SpriteFrames`, `AnimatedSprite2D`, MonsterEntity-style `frame_changed` origin correction, world Y-sort.
2. SGTest Sprite: same **SGTest Height-P0 VRAM-compressed imported atlas** as SGTest Fast; shared `SpriteFrames` per Enemy type, independent `AnimatedSprite2D` per visible actor, `frame_changed` trim/origin correction, logical-view tick durations/looping, world Y-sort.
3. SGTest Fast Canvas: exactly the same SGTest imported atlas and logical-view timing, precompiled regions/offsets, one renderer node, explicit Y-sort and per-frame draw submission.

Scripts: `tools/asset_converter/benchmarks/packing/{parity_renderer.gd,recovered_parity_native.gd,visual_pixel_parity.gd,renderer_lifecycle.gd,run_renderer_parity.py}`.

Raw outputs: `.cache/asset_packing_benchmark/renderer_parity_v2/summary.json` and `.cache/pixel_parity_nonblank.json`, `.cache/renderer_lifecycle_result.json`. Results do not contain redistributed source images.

## Test conditions

- Windows; Godot 4.7.2; NVIDIA RTX 3050; OpenGL Compatibility.
- 1280×720; VSync off; 150 measured/warmup frames per case, first 35 as warmup.
- Three independent processes/case, median for each metric; second repetition reverses candidate order.
- Common action set for each Enemy includes IDLE, WALK, ATTACK, HIT, DEATH and multiple directional variants.
- Entity positions change every frame; all candidates perform world Y-sort and periodically change animation.
- Recovered uses its own ACT-aware loader; SGTest uses canonical logical views and timelines. Exact sampling of animation time between the two original clients is not guaranteed identical.
- Windows Peak RSS includes complete Godot launcher/renderer process tree. Engine Texture delta is a Godot monitor, **not a physical GPU VRAM probe**.
- Frame-time P95/P99 measure isolated Godot rendered frames, not end-to-end multiplayer input latency.
- No source and no production map/Server process loaded in the isolated test.

## Results

All RAM/Texture values are MiB; Load and P95/P99 are milliseconds. Lower is better.

| Scenario | Mode | Peak RSS | Texture delta | Load | P95 | P99 | Draw P95 |
|---|---|---:|---:|---:|---:|---:|---:|
| small Enemy ×1000 | Recovered | 209.1 | 0.5 | 1.6 | 1.474 | 1.594 | 2 |
| | **SGTest Sprite** | **150.9** | **0.3** | 2.2 | **0.935** | **1.326** | **1** |
| | SGTest Fast | 148.0 | 0.3 | 2.2 | 2.745 | 2.767 | 1 |
| huge Enemy ×1000 | **Recovered** | 222.5 | 15.3 | 41.7 | **1.988** | **2.114** | 2 |
| | SGTest Sprite | 161.2 | **14.5** | 34.7 | 2.732 | 2.858 | **1** |
| | SGTest Fast | **158.1** | 14.5 | **33.9** | 2.713 | 2.837 | **1** |
| 4 Enemy kinds ×1000 | Recovered | 214.9 | 21.2 | 48.1 | 2.777 | 2.990 | 840 |
| | **SGTest Sprite** | 167.9 | **19.5** | **28.5** | **2.582** | **2.715** | 839 |
| | SGTest Fast | **164.1** | 19.5 | 36.4 | 4.118 | 4.219 | 839 |
| 12 Enemy kinds ×200 | Recovered | 215.1 | 62.3 | **78.7** | 0.829 | **0.913** | 199 |
| | **SGTest Sprite** | 166.5 | **58.0** | 80.8 | **0.796** | 1.031 | 198 |
| | SGTest Fast | **165.9** | 58.0 | 79.1 | 1.017 | 1.151 | 198 |
| 12 Enemy kinds ×1000 | Recovered | 215.6 | 62.3 | **78.0** | 3.047 | 3.316 | 967 |
| | **SGTest Sprite** | 172.6 | **58.0** | 79.5 | **3.014** | **3.298** | **966** |
| | SGTest Fast | **168.2** | 58.0 | **77.8** | 4.399 | 4.548 | 967 |
| 12 Enemy kinds ×2000 | Recovered | 219.8 | 62.3 | **78.7** | 6.576 | 7.396 | **1901** |
| | **SGTest Sprite** | 178.7 | **58.0** | 84.5 | **5.602** | **6.142** | **1901** |
| | SGTest Fast | **168.7** | 58.0 | 79.0 | 8.997 | 9.265 | 1902 |

Key relative outcomes:
- 12 kinds ×2000: SGTest Sprite P95 **14.8% lower** than Recovered, peak RSS **18.7% lower**, Texture delta **6.9% lower**.
- 12 kinds ×1000: P95 difference ~1%, practically a tie at this repeat count; SGTest Sprite RSS ~20% lower.
- 4 kinds ×1000: SGTest Sprite P95 ~7% lower, RSS ~22% lower.
- Huge *single* kind ×1000: Recovered **faster** on P95 (1.988 vs 2.732). Do **not** claim universal superiority.
- SGTest Sprite typically uses ~1001 Nodes where Fast Canvas uses two nodes. However, it retains a modest RSS advantage versus the actual Recovered client in this isolated setup.
- 12-kind interleaved Y-order has ~967 draws per 1000 entities in both; no texture-binding/draw-call miracle was observed.

## Visual and timeline correctness gates

### Draw parity

`visual_pixel_parity.gd` compared the *same imported compressed atlas* via the direct region renderer and `AtlasTexture + Sprite2D`. It tested small/medium/huge Enemy and EN114A; idle/walk/attack/hit/death frames and mirrored directions. Both target images were verified nonblank before comparison.

Result:

- **96 / 96 test cases**.
- **36 mirrored cases**.
- **0 differing RGBA pixels** across all tested viewport captures.

This proves sampled sprite region placement and flip consistency **between the two renderer implementations on the compressed atlas**, not that VRAM compression is pixel-perfect compared with the original uncompressed SPR.

### Frame metadata/timeline

`parity_renderer.gd` statically verifies every generated `SpriteFrames` frame maps to the exact same imported page/region, expected trim-origin offset, flip offset, loop value and frame duration as the logical-view data.

On a 12-Enemy sample, **1,179 frame assertions passed**. This proves the chosen frames' metadata representation, not a full online action/audio/packet simulation.

### Map/AFK lifecycle

`renderer_lifecycle.gd`: 12 Enemy types / 1000 nodes, **five load/unload cycles**.

- Loaded Texture delta: **60,768,112 bytes** each cycle (about 58.0 MiB).
- After each unload: **0 bytes Texture delta** relative to test baseline.
- After each unload: **0 Node delta** relative to test baseline.
- No increasing retained GPU Texture resource in this isolated repeated-creation test.

This does not certify full Game UI/AFK, in-flight worker cancellations, or macOS process RSS.

## Decision for SGTest

**Recommended next implementation candidate**:

```text
Individual Enemy/<id> Height-Dense P0 atlas
  -> Godot imported VRAM-compressed Texture2D in external PCK
  -> one shared SpriteFrames per logical Enemy type
  -> one AnimatedSprite2D per visible moving Enemy
  -> frame_changed offset correction + canonical logical-view timing
  -> correct World Y-sort
  -> shared AssetStore ownership / AFK / map unload
```

Why: This meets the user's preference to prioritize **smoother visible motion** when RAM is acceptable, and is faster *and* lower RAM in the tested 4-kind and 12-kind high-load scenarios. Node count alone should not override the observed frame-time results.

**Do not** switch Player/Equipment/Pet/Mecha automatically: each has distinct draw-order, layering and actions and needs its own rendering fidelity/profiling. Keep low-node Canvas for static scenery/drops as an independent measured decision.

**Remaining acceptance gates before production switch:**

1. Integrate with actual `RuntimeAssetStore`, original server packet updates and actual Map13 Y-sort.
2. Confirm walking/attack/HIT/DEATH, directional mirror, action interrupt/resume and audio event timing under real network jitter.
3. Full player paper-doll, weapon layering, pet/mecha and drops simultaneously: no wrong occlusion / missing animation.
4. Test viewport sizes and P0 compressed edge quality with nearest/linear filtering. A zero-difference comparison between two compressed render paths is **not** equivalent to comparing with original uncompressed pixels.
5. Test AFK/unfocus, multi-map switching, re-login, texture release while workers may still be loading, and memory stability.
6. Measure on Windows Release EXE/PCK **and actual macOS hardware**; separate input-to-render latency from frame P95.
7. Investigate the single huge-Enemy regression and multi-texture draw-call bottleneck only if representative gameplay workloads justify complexity.

Nothing in these test-only scripts modifies production client logic or the original Recovered project.
