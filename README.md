# Carnival World — Midnight Midway

A design dossier deck **and** a Unity greybox kit for a VRChat escape-room hideout world built to run
on **Meta Quest 3 and PC**. The concept: a travelling carnival that broke down in the wrong field and
never left. Four players buy tickets, the gate locks, and they get twenty-five minutes to break the
show before the show breaks them. Tone is Joker showmanship, Terrifier grime and Killer Klowns candy
phosphor — used as flavour, with an original host character and original props throughout.

## The deck

33 slides on one reveal.js page, no build step.

```sh
python3 -m http.server 3000   # then open http://localhost:3000
```

`index.html` is a standalone document, so opening the file directly also works. reveal.js is vendored
in `node_modules/reveal.js`, so there is nothing to install.

| Key | Action |
| --- | --- |
| `→` `←` / space | next / previous slide |
| `Esc` | slide overview |
| `S` | speaker notes |
| `F` | fullscreen |
| `B` | blackout |
| `?` | all shortcuts |

Deck structure: opening (1–3) · **The Look** (4–8: references, palette, art rules, Grinny) ·
**The Place** (9–16: floor plan, five zones, hideout, budget roll-up) · **The Game** (17–23: run
structure, five locks, surprise economy, roles, endings) · **The Build** (24–32: Quest limits,
platform tiers, systems, audio, comfort, roadmap, risks, checklist) · finale (33).

Two effects demonstrate the design: slide 21 fires one reactive scare, slide 33 drops streamers.
Both are skipped for visitors whose system reports `prefers-reduced-motion: reduce`.

## The Unity package

`dist/CarnivalWorld-0.20.0.unitypackage` — greybox and systems kit, built from source in
[unity/Assets](unity/Assets) by [script/build-unitypackage.py](script/build-unitypackage.py).

```sh
python3 script/build-unitypackage.py   # rebuilds the .unitypackage from unity/Assets
```

Contents:

| Asset | What it does |
| --- | --- |
| `CarnivalLockManager` | one synced bitmask for the whole run, hands the last seconds to the finale |
| `CarnivalLock` | per-lock identity, solved visual, relay to the manager |
| `CarnivalShowTimer` | 25-minute clock, one sync per second, final-minute hooks |
| `SurpriseDirector` | 90 s cooldown, six scares per player, intensity 0–2 |
| `CarnivalRoleBoard` | four claimable co-op roles, released on player leave |
| `CarnivalShowStarter` | gate line: starts the clock on first crossing, clears the previous run |
| `CarnivalCurtainLock` | lock 06's puzzle: four marks held at once, 0.4 s debounce, Contortionist's 8 s solo hold |
| `CarnivalCurtainMark` | one ring mark: reports guests in and out to the curtain |
| `CarnivalBoilerLock` | lock 05's puzzle: four positions in one synced int, wrong notes hiss and ask the director for a scare |
| `CarnivalValve` | one boiler dial: Interact turns it, the knob draws the synced note |
| `CarnivalTicketLock` | lock 01's puzzle: four distinct stubs in one synced mask, reveals the 1986 photo |
| `CarnivalTicketStub` | one numbered stub: judges its own drop against the booth mouth, or is handed over; a reset returns it to its home mark |
| `CarnivalMirrorLock` | lock 03's puzzle: four angles in one synced int, per-frame beam when aligned |
| `CarnivalMirrorFrame` | one mirror: Interact turns a quarter, the frame draws its synced angle |
| `CarnivalBalloonLock` | lock 04's puzzle: three balloon colours against the chalkboard answer |
| `CarnivalBalloon` | one balloon: hold-to-fill with a synced float, five colour bands, pops on solve |
| `CarnivalScaleLock` | lock 02's puzzle: three barrels against a sliding counterweight, tolerance plus a 1 s settle |
| `CarnivalBarrel` | one barrel: two-handed carry, seats on a pan socket, the Bearded Lady lifts alone, and it goes back on its home mark when the run resets |
| `CarnivalFinale` | the last three beats — lights out, Grinny bows, gate opens — on one synced stage, so late joiners land on the finished state; the clean break, the understudy or the hidden ending is chosen at the gate beat |
| `CarnivalTeeth` | the twelve teeth as one synced mask — the hidden ending's whole condition, cleared at the gate line |
| `CarnivalTooth` | one tooth: Interact calls `Collect()`, and the ledger empties the socket for everyone |
| `CarnivalRunReset` | the lever in the hideout: clears the six puzzles at the source, sends the stubs, the three barrels and the counterweight back to their home markers, then clears the mask, the finale stage and ending, the teeth, the clock and the scare budget, so a second party can play the same instance |
| `Editor/CarnivalGreyboxBuilder` | builds the eight zones from the deck's floor plan, in metres |
| `Editor/CarnivalRigWiring` | attaches and wires the entire rig onto the anchors in one click, then reads the reset lever back and warns when one of its systems is not the instance the rig uses |
| `Editor/CarnivalRigValidator` | read-only pre-upload check: empty references, mask mismatches, unreachable triggers, a reset lever that misses a system |
| `Editor/CarnivalBudgetCheck` | reports triangles, materials, lights, mirrors, audio, physics |
| `Docs/` | setup guide, Quest budget, puzzle sheet, surprise economy |

Import into a Unity 2022.3 LTS project that already has the VRChat SDK3 Worlds and UdonSharp, then
run **Carnival World → 0. New Playable Greybox** (one click: build, lighting, wiring). See
[unity/Assets/CarnivalWorld/Docs/00-Start-Here.md](unity/Assets/CarnivalWorld/Docs/00-Start-Here.md)
for the wiring order and upload checklist.

### Honest status

The archive is structurally verified — every entry carries `pathname`, `asset` and `asset.meta`,
with GUIDs derived from the asset path so rebuilds are byte-identical. It has **not been
imported into Unity here** (there is no Unity installation in this environment), so the UdonSharp
scripts are uncompiled and may need small API fixes on first import, and the greybox does not yet
include a `VRCWorld` descriptor or spawn component — you add those in your scene. The editor tools
reference stock Unity APIs only.

## Budget sources

- [Android content limitations](https://creators.vrchat.com/platforms/android/quest-content-limitations/) —
  100 MB world cap, post-processing and avatar lights disabled, restricted shaders and audio
- [Android content optimization](https://creators.vrchat.com/platforms/android/quest-content-optimization/) —
  ~250k triangle world budget, 1024² texture ceiling, baked lighting, occlusion culling

Zone figures are design targets, not measurements; the roll-up leaves ~38k triangles and ~36 MB of
headroom for avatars and future props.

## Files

| Path | What it is |
| --- | --- |
| `index.html` | the whole deck, 33 sections |
| `css/carnival.css` | theme: type, palette, cards, tables, map, ticket, scare overlay |
| `js/carnival.js` | reveal.js init plus the two in-deck effects |
| `unity/Assets/CarnivalWorld/` | package source: scripts, editor tools, docs |
| `dist/` | built `.unitypackage` |
| `script/build-unitypackage.py` | reproducible packer |

The Jekyll files (`_layouts`, `_includes`, `_posts`, `_config.yml`) are leftovers from the original
Learning Lab slideshow template and no longer drive the deck; `index.html` is a plain document.
