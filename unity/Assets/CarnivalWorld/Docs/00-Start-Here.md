# Start here — building Midnight Midway

## 1. Project setup

- Unity **2022.3 LTS** (check VRChat's current supported version before you commit).
- VRChat Creator Companion → create a **Worlds** project → add **UdonSharp**.
- Switch the build target to **Android** at least once early: it finds texture and shader
  problems while they are still cheap to fix.

## 2. Build the greybox

`Carnival World → 1. Build Greybox` creates:

| Object | Size | Purpose |
| --- | --- | --- |
| `00_TicketBooth` | 6 × 5 m | spawn, first lock, the photograph |
| `01_Midway` | 40 × 8 m | tutorial games, four clues, the tell |
| `02_BigTop` | 26 m round | ring, audience, trapeze, curtain lock |
| `03_HallOfMirrors` | 12 × 14 m | beam lock, one real mirror |
| `04_Funhouse` | 14 × 14 m | balloon lock, comedy peak |
| `05_BackstageBoiler` | 10 × 8 m | calliope lock, the source |
| `06_Hideout` | 9 × 6 m | safe room, roles, persistence |
| `07_ExitGate` | 6 × 5 m | opens once, at the end |

Anchors live under `_Anchors` and `_ScriptedBeats`. Move them, do not rename them — the
names are the wiring contract between the builder and your components.

## 3. Wire the run — one click

`Carnival World → 0. New Playable Greybox` builds the greybox, applies the Quest lighting
preset and wires the run in one step. If you already have a greybox, use
`Carnival World → 4. Wire Show Rig (playable)` instead.

| Created | Where | Wiring |
| --- | --- | --- |
| `_Systems` | greybox root | `CarnivalLockManager`, `CarnivalShowTimer`, `CarnivalRoleBoard`, `SurpriseDirector`, `CarnivalFinale` |
| `CarnivalLock` | each `LOCK_0*` anchor | `lockId` 1–6, title, manager, solved visual |
| `SOLVED_PIN` | under each lock anchor | small gold block, hidden until that lock is solved |
| `GATE_BARRIER` | exit gate doorway | blocks the exit; removed by the finale's third beat |
| `GATE_OPEN_SIGN` | exit gate | lights 5 s after the blackout, when the barrier comes down |
| `HOUSE_LIGHT_*` | sawdust ring | four practicals standing in for the big-top string lights; cut by beat one |
| `GRINNY_BOW`, `BOW_SPOT` | under `GRINNY_PERCH` | the figure and its plate, hidden until beat two |
| `SIGN_FINAL_MINUTE`, `SIGN_TIME_UP` | exit gate | swapped in by the clock |
| four `RoleCard_*` | `ROLE_BOARD` anchor | shown while a guest holds that role |
| `GATE_LINE` + `CarnivalShowStarter` | midway entrance, z = −5 | starts the 25 minutes on first crossing |
| `RUN_RESET_LEVER` + `CarnivalRunReset` | hideout wall | the next show: one Interact clears the whole run |
| `CURTAIN_MARKS` + four `CURTAIN_MARK_*` | `LOCK_06_Curtain` anchor | 1.6 × 1.8 × 1.6 m trigger columns on a 2.6 m radius; the curtain lock reads them |
| `MARK_PAD` | inside each curtain mark | lights while that mark is held or latched |
| `BOILER_VALVES` + four `VALVE_*` | `LOCK_05_BoilerLullaby` anchor | 0.46 m interactable plates at 1.4 m, each with a turning knob |
| `BOILER_STEAM` | boiler anchor | hiss on a wrong note, hidden otherwise |
| `TICKET_STUBS` + four `TICKET_STUB_*` | `LOCK_01_TicketTear` anchor | numbered stubs with `VRC_Pickup` and a collider |
| `TICKET_MOUTH` | ticket booth | the box a dropped stub has to land in |
| `STUB_LAMP_*`, `PHOTO_1986` | ticket booth | count lamps, and the photo revealed at four |
| `MIRROR_FRAMES` + four `MIRROR_FRAME_*` | `LOCK_03_MirrorsAside` anchor | solid panes that turn a quarter per Interact, each with a beam child |
| `RUNE_LIT`, `LAGGING_REFLECTION` | hall of mirrors | lit when the beam arrives |
| `BALLOON_RIG` + three `BALLOON_*` | `LOCK_04_BalloonLogic` anchor | grabbable balloons with a fill skin, five swatches and a pop burst |
| `BALLOON_CONFETTI`, `WARMER_CARD` | funhouse | revealed by the pop |
| `WEIGHT_SCALE` + three `BARREL_*` | `LOCK_02_WeightOfTheShow` anchor | a beam on a solid stand, three sockets on the pan, a sliding counterweight |
| `RING_MANNEQUINS` | sawdust ring | revealed once the beam settles level |

The tool is **idempotent**: re-running it after you move anchors re-wires the objects it finds
instead of duplicating them, and it reuses any component you already configured. It reports
anything it could not do in a single Console line — read it, it names the missing anchor or
the missing field.

`requiredMask = 63` means all six locks. Per lock: `1` booth, `2` weight, `4` mirrors, `8` balloons,
`16` boiler, `32` curtain. Use `31` while testing the five puzzle locks without the curtain.
`showLengthSeconds = 1500` is the 25-minute run.

All six puzzles are implemented: the curtain (stand on all four ring marks, or let the
Contortionist hold one for eight seconds), the boiler (turn four valves so they spell `2, 0, 3,
1`), the ticket booth (feed it four numbered stubs in any order), the mirrors (turn four frames so
each beam lands), the balloons (inflate three animals to colours `1, 3, 0`) and the weight (carry
three barrels onto the beam and slide the counterweight until it is level). Every one of them can
be finished by a single guest: the Contortionist latches a mark, the Bearded Lady carries a barrel
alone, and the other three locks need only one pair of hands. The validator reports a lock that is
wired but has no puzzle behind it, so a half-built run does not look finished.

The finale runs as a sequence, not a switch: at 0 s the ring goes dark, at 2.2 s Grinny appears on
the perch and bows, at 5 s the barrier drops and the open sign lights. Tune `bowDelay` and
`gateDelay` on `_Systems` — the validator errors on a gate delay shorter than the bow delay,
because the one beat that sells the ending would then happen after the group had turned to leave.
The sequence syncs a single `stage` int, so a guest who joins late arrives with the gate already
open rather than watching it happen again, and if the guest driving it leaves mid-sequence the
next owner finishes the remaining beats.

The gate opens the same physical way every time, but three endings are decided at the gate beat
and synced with the stage. A **clean break** is the default. **The understudy** is earned by one
guest still standing on `CURTAIN_CENTRE_MARK` five seconds after the lights die — exactly one, so
a group milling about in the ring does not trigger it by accident. **Buy the carnival**, the
hidden ending, needs all twelve teeth; `CarnivalTeeth` keeps one mask, `CarnivalTooth` is the
pickup, and the ledger is cleared at the gate line so a second party earns it themselves. Only
one ending's overlay is ever visible, including for a guest who joins after the gate is open.

The clock does **not** auto-start on join: `GATE_LINE` starts it when the first guest walks
into the midway, so a second party does not inherit a half-spent run. The same crossing calls
`SurpriseDirector.ResetRun()`, so a group running the carnival twice starts with a full
six-scare budget again.

A finished show leaves a solved world behind, so without help the second party would inherit all
six locks, the open gate and the twelve teeth. `RUN_RESET_LEVER` in the hideout is the way back:
one Interact clears the six puzzles at the source (the booth's stubs, the boiler's dials, the
mirrors' angles, the balloons, the scale's counterweight, the curtain's marks), then the solved
mask, then the finale's stage and ending, the teeth, the clock and the scare budget. Stage 0 also
cancels any finale beat still in flight, on every client that reads it. The order is
the point — clearing the mask first would leave each puzzle's own synced answer in place and the
lock would be solved again a frame later. It is a lever rather than a trigger because a finished
show and four guests standing around talking look identical from a trigger's point of view, and
wiping a party's progress by accident is worse than asking one of them to pull something.
The physical props come home too. Every stub has a `TICKET_STUB_HOME_*` mark under
`TICKET_STUBS`, every barrel a `BARREL_HOME_*` under `WEIGHT_BARRELS`, and the counterweight a
`SCALE_CW_HOME` on the beam: the reset puts each one back, so the second party finds a set-up ring
instead of four stubs on the floor and a barrel still sitting on the pan. Move a marker and the
reset follows it — nothing about where a prop belongs is baked into code. Pulling the lever
mid-run is allowed, for a group that wants to start over — including a stub someone is still
gripping. The pickup owns that transform, so the reset cannot move it from the outside; it sends
the stub a new epoch instead, and the holder's own client ends the grip (the prop is off for the
two frames a pickup needs to let go) and the booth stands it back on its mark. A release from
that old grip is refused rather than counted, so a stub cannot be banked after the booth was
cleared. Pulling the lever with a stub in your own hand does the same thing.

Re-running **Carnival World → 4. Wire Show Rig** reads the lever back once it has written it, and
warns if any system on it is not the instance the rest of the rig points at. A second director,
clock, finale or teeth ledger dropped into the scene by hand is the usual way that happens, and it
is the failure that looks like a working reset: the lever clears a copy nothing reads, so the run
comes back with one system still carrying the last party's state.

Manual wiring is still fine if you would rather place your own props. Keep the anchor names —
they are the contract the tool relies on.

## 4. Check the rig before you upload

`Carnival World → 5. Validate Rig (pre-upload)` reads the wired scene and reports the three
failures that only surface after a 25-minute run has already been lost:

- **Empty references** — a lock with no manager, a slot in `manager.locks` with nothing in it,
  a starter with no clock, a role card that was never created, a beat that is not wired.
- **Mask mismatches** — `requiredMask` asking for a lock that does not exist, which means the
  finale can never fire; or a wired lock outside the mask, which means solving it does nothing.
- **Unreachable triggers** — a trigger that is disabled, buried inside a wall or floor slab
  (tested with an overlap query, not by eye), or too thin for a guest to cross. If nothing can
  start the clock, it says that too.
- **The curtain** — four marks wired, no duplicates, each reporting to the right curtain, each
  with a trigger, `lockRef` pointing at lock 6, and the solo hold not silently disabled.
- **The boiler** — four valves wired, unique indices, each reporting to the right boiler, each
  with a *solid* collider (a trigger-mounted valve cannot be interacted with at all), `lockRef`
  pointing at lock 5, a four-note melody inside 0–3, and the surprise director assigned.
- **The ticket booth** — four stubs with distinct ids, each reporting to the right booth and
  carrying a mouth marker, plus the pickup component (warning: without it, Interact is the only
  way to hand a stub over) and a reveal that is not empty.
- **The mirrors** — four frames wired, unique indices, each reporting to the right lock, each with
  a *solid* collider, a solution angle inside 0–3, and a reveal that is not empty.
- **The balloons** — three balloons wired, unique indices, each with a collider, five colour
  swatches and a skin, `lockRef` pointing at lock 4, and an answer whose colours all exist.
- **The finale** — a `CarnivalFinale` in the scene and pointed at by the manager, a bow delay the
  gate delay does not undercut, blackout and bow beats that actually turn something on or off, a
  `GATE_BARRIER` the finale opens, `GATE_OPEN_SIGN` in the enable list rather than the disable
  one, and a `stage` left at 0 so guests do not arrive at a finished show.
- **The endings** — the finale pointed at the teeth ledger and the ring, a `requiredTeeth` that
  matches the number of wired teeth (otherwise twelve can never add up and the hidden ending is
  unreachable), unique tooth indices, a solid collider on every tooth, a non-empty overlay for
  each ending, no prop shared between two endings, a `CURTAIN_CENTRE_MARK` flagged `centreMark`
  with a trigger, and an `ending` left at 0.
- **The weight** — three barrels wired, unique indices, each with a *solid* collider, a positive
  mass and the right scale backlink, three sockets that really are children of the beam, and a
  counterweight whose range contains at least one step inside the tolerance. That last one is
  this lock's unreachable-trigger check: a beam no setting can level is unwinnable, and it looks
  finished in the hierarchy.
- **Replay** — a reset lever that exists, is active, has a collider and a cooldown, all six
  puzzles wired into it (a missing one leaves its lock solved), every lock in `manager.locks`
  covered by one of those puzzles, the same finale the manager uses, plus the teeth, the clock,
  the gate line and the surprise director. That last one is the quiet one: the scare budget lives
  in a per-guest array on each client's copy of the director, so a lever that never reaches it
  resets everything except the one thing the second party notices within a minute — their six
  scares, already spent. It errors when the lever's director is empty, and when it is a different
  director than the one on `_Systems` (the budget gets cleared on a director no prop asks). It
  also errors on an all-zero mirror solution, because a reset puts every frame back on angle 0 and
  that answer would start the run already aligned.
- **Homes** — a home marker on all four stubs, all three barrels and the counterweight, since a
  missing one leaves that prop wherever the last run dropped it. It also warns about two stubs or
  two barrels sharing a mark, a barrel home inside a socket's snap radius (a reset that seats
  itself), a stub home inside the booth mouth (a stub that arrives already handed in) and a
  counterweight home on the wrong side of the fulcrum, which makes the lightest step read as the
  heaviest.

It is read-only — nothing in the scene is touched — so it is safe to run at any point. Errors
log as errors with the offending object attached, so clicking the Console line selects it;
warnings log as warnings; a clean scene logs one line saying so. Zero errors means the scene is
uploadable to a private test world. The two known manual steps it flags are the missing
`VRCWorld` descriptor and its spawn points — it does not create those for you.

## 5. Interactables

Use VRChat **Interact** events on the props, not per-frame checks. Every puzzle ends in the
same call:

```csharp
// from your puzzle script, once the interaction is verified
lockBehaviour.Solve();
```

Nothing else needs to know about networking — the manager owns the state and re-syncs the
visuals for everyone.

## 6. Test order

1. Desktop client, 4 players in a private instance: all six locks, both endings.
2. Two players, then one: the second-pair-of-hands steps must still be reachable.
3. Quest 3 over Link first, then standalone on device — standalone is the only measurement
   that counts for frame rate.
4. Android build with the same texture and lightmap settings you intend to ship.

## 7. Upload checklist

Matches the deck's final slide. Do not upload until all ten are true:

- [ ] World build under 100 MB compressed (hard cap on Android)
- [ ] 72 fps on Quest 3 in the big top with four avatars in view
- [ ] All locks solvable by four strangers without a hint
- [ ] All locks still solvable by two, and by one
- [ ] One mirror render pass on Quest, measured
- [ ] Surprise budget verified from logs
- [ ] Comfort card seen by a first-time tester
- [ ] Persistence survives rejoin
- [ ] Thumbnail and trailer sell the comedy
- [ ] Content warnings match the in-world card

Upload PC first, then Android, from the same scene with the same content.
