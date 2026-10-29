# Carnival World — Unity package (v0.20.0)

A greybox and systems kit for the **Midnight Midway** escape-room world described in the
design deck. This is the build skeleton, not the art: eight blocked-out zones, the six-lock
run logic, the surprise economy, and a Quest budget reporter that measures your scene.

## Requirements

| Item | Version |
| --- | --- |
| Unity | 2022.3 LTS (the version VRChat's SDK is on — confirm the current pin before upgrading) |
| VRChat SDK | SDK3 · Worlds |
| UdonSharp | installed via VRChat Creator Companion |
| Platform | Windows (PC) + Android (Quest 3) build targets |

## Quickstart

1. Import this package into a project that already has the VRChat SDK and UdonSharp.
2. Run **Carnival World → 0. New Playable Greybox** on an empty scene. One click builds the
   eight zones, applies the Quest lighting preset and wires the run: lock manager, six locks
   with solved pins, the four curtain marks, four boiler valves, four ticket stubs, four mirror
   frames, three balloons, three barrels on a beam scale, the 25-minute clock, the role board, the
   surprise budget, the three-beat finale (lights out, Grinny bows, gate opens) with its three
   endings, twelve teeth for the hidden one, the gate line that starts the clock, and the reset
   lever that clears the whole run for the next party.
3. Bake lighting (Progressive CPU), then press Play, or upload to a private test world.
4. Re-run **Carnival World → 4. Wire Show Rig (playable)** after moving anchors — it reuses
   what exists rather than duplicating it, and prints what it could not wire.
5. Run **Carnival World → 5. Validate Rig (pre-upload)** before uploading. It is read-only and
   reports empty references, `requiredMask` mismatches, trigger volumes nobody can cross, and a
   reset lever that misses a system — including the surprise director, whose budget is per client
   and so cannot be cleared by the mask.
6. Run **Carnival World → 3. Quest Budget Report** before every upload.

## What each script does

| Script | Role |
| --- | --- |
| `CarnivalLock` | One lock: identity, solved visual, relay to the manager |
| `CarnivalLockManager` | Single synced bitmask for the whole run; fires the finale |
| `CarnivalShowTimer` | 25-minute clock, one sync per second, final-minute and time-up hooks |
| `SurpriseDirector` | Per-player cooldown, six-scare budget, intensity 0–2 |
| `CarnivalRoleBoard` | Four claimable co-op roles, released on player leave |
| `CarnivalShowStarter` | Gate line: starts the clock on first crossing, clears the last run |
| `CarnivalCurtainLock` | Lock 06's puzzle: four marks held at once, 0.4 s debounce, Contortionist's 8 s solo hold |
| `CarnivalCurtainMark` | One ring mark: reports guests in and out to the curtain |
| `CarnivalBoilerLock` | Lock 05's puzzle: four valve positions in one synced int, wrong notes hiss and ask the director for a scare |
| `CarnivalValve` | One boiler dial: Interact turns it, the knob draws the synced note |
| `CarnivalTicketLock` | Lock 01's puzzle: four distinct stubs in one synced mask, reveals the 1986 photo |
| `CarnivalTicketStub` | One numbered stub: judges its own drop against the booth mouth, or is handed over; a reset puts it back on its home mark, and ends a grip that outlived it |
| `CarnivalMirrorLock` | Lock 03's puzzle: four angles in one synced int, per-frame beam when aligned |
| `CarnivalMirrorFrame` | One mirror: Interact turns a quarter, the frame draws its synced angle |
| `CarnivalBalloonLock` | Lock 04's puzzle: three balloon colours against the chalkboard answer |
| `CarnivalBalloon` | One balloon: hold-to-fill with a synced float, five colour bands, pops on solve |
| `CarnivalScaleLock` | Lock 02's puzzle: three barrels against a sliding counterweight, tolerance plus a 1 s settle |
| `CarnivalBarrel` | One barrel: two-handed carry, seats on a pan socket (the Bearded Lady lifts alone), returns to its home mark on reset and refuses a carry written before that reset |
| `CarnivalFinale` | The last three beats: lights out, Grinny bows, the gate opens — one synced stage, late joiners land on the finished state; picks the clean break, the understudy or the hidden ending at the gate beat |
| `CarnivalTeeth` | The twelve teeth as one synced mask; the hidden ending's whole condition, cleared at the gate line |
| `CarnivalTooth` | One tooth: Interact calls `Collect()`, the ledger empties the socket for everyone |
| `CarnivalRunReset` | The next show: one lever that un-solves six puzzles at the source, sends the stubs, barrels and counterweight home, then clears the mask, the finale, the endings, the teeth, the clock and the scare budget |
| `Editor/CarnivalGreyboxBuilder` | Builds the deck's floor plan from code |
| `Editor/CarnivalRigWiring` | Attaches and wires the whole rig onto the anchors, one click; then reads the reset lever back and warns when one of its systems is not the instance the rig uses |
| `Editor/CarnivalRigValidator` | Read-only pre-upload check: empty references, mask mismatches, unreachable triggers, a reset lever that misses a system |
| `Editor/CarnivalBudgetCheck` | Measures triangles, materials, lights, mirrors, audio, physics |

## Honest status

The package is structurally valid and reproducible (`python3 script/build-unitypackage.py`),
and the editor tools reference only stock Unity APIs. The UdonSharp scripts have **not been
compiled against a VRChat SDK in this repo** — there is no Unity install here. Expect to fix
small API details on first import (a `using`, a field type, an interface namespace), and test
on PC before Android.

Everything is original work for this world. The clown, props and signage are not derived from
any licensed character.
