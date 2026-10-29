# Quest 3 budget — what actually costs frame time

The short version: **bake everything, atlas everything, and count render passes, not polygons.**

## Hard limits and targets

| Item | Limit | Midnight Midway target |
| --- | --- | --- |
| World download | 100 MB compressed (hard; cannot upload or load above it) | 64 MB |
| Triangles | ~250k recommended for worlds | 212k |
| Texture size | 1024² practical ceiling on mobile | 512² atlases |
| Lighting | Baked lightmaps + probes; realtime is expensive | Baked + one lamp |
| Post-processing | Disabled entirely on Android / Quest | Baked into albedo + emission |
| Transparency | Alpha fill rate is a major mobile sink | Six alpha props |
| Audio sources | Restricted in worlds | Nine total, three concurrent |
| Mirrors | Each real mirror is an extra scene render | One real, eight fakes |
| Avatars | Lights, audio sources, physics and post FX disabled on Quest | Design as if absent |
| Physics | Allowed in worlds, but avoid stacks | Zero rigidbodies |

Sources: VRChat's *Android Content Limitations* and *Android Content Optimization* pages.
Re-check them before a major version bump — VRChat does adjust these.

## Where the triangles go

| Zone | Triangles | Materials | Download |
| --- | --- | --- | --- |
| Ticket booth + midway | 42k | 11 | 12 MB |
| Big top | 55k | 9 | 14 MB |
| Hall of mirrors | 26k | 7 | 11 MB |
| Funhouse | 38k | 10 | 10 MB |
| Backstage boiler | 24k | 6 | 8 MB |
| Hideout | 21k | 8 | 9 MB |
| Grinny + props + shared atlas | 6k | 2 | — |
| **Total** | **212k** | **53** | **64 MB** |

## Optimization order

Do these in order. Each one buys more than the one after it.

1. **Bake occlusion per zone.** The big top tent should stop the midway from rendering entirely.
2. **Atlas by object group**, not by material name. A stall's planks, cloth and tin are one
   material and one texture if you author them that way.
3. **Bake lighting** at low resolution. 1024² lightmaps at 20–40 texels per unit look good
   under carnival strip lighting, which is mostly direct and hard.
4. **Replace realtime lights** with emissive bulbs plus a baked pool of light. Keep one
   animated practical — the swinging lamp — as an animation, not a light.
5. **Fake the mirrors.** One `VRCMirrorReflection` at 512 with a player-only layer mask.
6. **Merge mannequins** into three meshes with one atlas and one idle loop.
7. **Cut transparency** — netting, gauze and balloon glow are the usual offenders.
8. **Proxy the hero props** for Android: swap to a decimated mesh on platform detection.

## Measuring

- `Carnival World → 3. Quest Budget Report` gives triangles, materials, lights, mirrors,
  audio sources, particles and rigidbodies for the open scene.
- Unity Profiler for draw calls; the number you care about is *SetPass calls* per frame.
- The build statistics in the SDK Build panel give the compressed download size.
- Standalone Quest 3 is the only frame rate that counts. Link and the editor lie.

## Red flags in this world specifically

- A second mirror in the dressing room: PC only, disabled on Android.
- Balloon props with transparency: use opaque meshes with emissive trim.
- Crowd audio: one source, never one per mannequin.
- Particle confetti: four systems maximum, and only in the finale.
