# Puzzle sheet — six locks, implementation notes

Every lock is a prop interaction that ends in one call: `carnivalLock.Solve()`.
State, replication and visuals belong to `CarnivalLockManager`, not the prop.

## 01 · Ticket Tear — ticket booth

- **Players do:** collect a numbered stub each from the midway games, then feed all four into
  the booth's mouth in any order.
- **Implementation:** four pickups (`VRC_Pickup`), each with a unique `stubId`. A trigger at the
  booth mouth counts distinct ids; at four, call `Solve()`.
- **Built:** `CarnivalTicketLock` on `LOCK_01_TicketTear`, four `CarnivalTicketStub` cards in
  `TICKET_STUBS`, a `TICKET_MOUTH` marker and four `STUB_LAMP_*` blocks. The wiring tool adds
  `VRC_Pickup` by name and reports it if the SDK does not have the type — in that case guests
  hand the stub over with Interact instead of carrying it.
- **Why not a trigger on the mouth:** a held object fires no trigger events, so feeding cannot be
  detected by a volume. The stub judges its own `OnDrop` position against the mouth marker's box
  (`mouthHalfExtents`, 0.45 × 0.35 × 0.5 m) and reports itself. An unwired `mouth` refuses every
  drop rather than counting from anywhere in the world.
- **State:** one synced int, four bits, one per stub number. Order does not matter — the booth
  counts distinct numbers — and a deposited stub hides itself for everyone. The four lamps show
  the count without a UI panel.
- **Home:** each stub has a `TICKET_STUB_HOME_*` marker under `TICKET_STUBS`, and a reset sends
  all four back to theirs. Each stub takes ownership of itself first: a `VRC_Pickup` only
  replicates its transform from its owner, so moving one without owning it would look reset on
  the client that pulled the lever and unchanged to everyone else.
- **Reset mid-grip:** a held stub is the case ownership cannot fix, because the pickup drives
  that transform every frame on the client holding it. So the stub carries a synced `homeEpoch`
  that every reset moves. On the holder's client the new epoch ends the grip by turning the stub
  off for the two frames a pickup needs to let go; the lock — always active — brings it back on
  its mark, since a disabled behaviour cannot reschedule itself. A release from the old grip is
  refused rather than counted, so somebody still holding a stub when the lever is pulled cannot
  bank it after the booth has been cleared. If a platform declines to drop the prop on being
  disabled, the stub simply stays in that hand: the stale release is still refused, and the next
  reset returns it.
- **The photograph:** `PHOTO_1986` is a placeholder block; the 1986 belongs in the texture, not in
  code, so nothing here generates a date.
- **Reveal:** enable the photo prop — a party photo dated 1986. The date is baked into the
  texture, not generated.
- **Failure:** feeding a wrong object gives a Grinny line, no penalty.

## 02 · Weight of the Show — sawdust ring

- **Players do:** move three barrels onto a beam scale until it balances.
- **Implementation:** three barrels with a `mass` value; a counterweight steps along the short arm
  and the beam checks the difference against a small tolerance. No rigidbodies — snap barrels to
  sockets, weigh in code.
- **Built:** `CarnivalScaleLock` on `WEIGHT_SCALE` (a solid stand a guest clicks to move the
  counterweight) and three `CarnivalBarrel` props in `WEIGHT_BARRELS`, seated on three
  `SCALE_SOCKET_*` markers that are children of `SCALE_BEAM`, so a seated barrel tilts with the
  pan. Masses are `18.4, 22.0, 19.3` kg — 59.7 kg on the pan — and the counterweight steps
  `56.5` to `60.5` kg in five 1 kg steps, so exactly one step lands inside the 0.5 kg tolerance.
- **The check:** levelness is a tolerance, not an equation. The beam counts as level while the two
  sides differ by less than `tolerance`, and it has to stay level for `settleSeconds` (1 s) before
  the lock opens. A barrel knocked past the balance point, or clicking Interact straight through
  the good step, cancels the attempt instead of solving it. `ForceSolve()` on the scale is the
  debug path.
- **Two hands:** a guest takes one end of a barrel, a second guest takes the other, and the barrel
  follows the midpoint between them. One end alone does not move it. No `VRC_Pickup` and no
  rigidbody: a physics barrel dropped on a tilting beam rolls off the pan. The Bearded Lady can
  carry one alone — claim the role at the hideout board — which keeps the lock solvable by one.
- **Sync:** each barrel replicates holder ids and its socket index; the carry position is derived
  from those ids, so no transform is streamed. The scale replicates one int: the counterweight
  step.
- **Home:** the three barrels have `BARREL_HOME_*` marks in the ring and the counterweight has
  `SCALE_CW_HOME` on the beam. A reset frees both hands, clears the socket index and writes the
  home spot into the barrel's synced `restPosition`, so it lands back on its mark for every
  client without a new piece of state. The counterweight reads its step-0 position from the
  marker through the beam's own transform, so the marker still means the same spot when the arm
  is tilted.
- **Reset mid-carry:** a barrel carries a synced `resetEpoch` that every reset moves. To the
  guests still carrying it, the reset is invisible until its state arrives — no use-up was ever
  delivered, so their `Update()` is still pulling the barrel towards their hands. On arrival the
  new epoch is treated as the reset: both hands are freed, the socket index is cleared and the
  barrel is put back on its marker. A packet tagged with an older epoch was written in that stale
  frame and is refused rather than applied, so a late release cannot seat the barrel back on the
  pan or drag it out of home for everyone else. The scale's 1 s settle covers the same window on
  the lock itself: a pan that reads complete for a frame re-weighs as incomplete before it counts.
- **No wrong answer:** the beam tilts, the group reads which way they are heavy, and a barrel can
  be lifted off its socket and re-seated. Nothing here costs more than time.
- **Reveal:** enable `RING_MANNEQUINS`; the sheet's ring floor dropping 0.5 m and the 1.2 s light
  cut are Animator work for the art pass, not the rig.
- **Note:** the Bearded Lady can carry a barrel alone; everyone else needs two players.

## 03 · The Mirror's Aside — hall of mirrors

- **Players do:** rotate four mirror frames so a beam hits the rune.
- **Implementation:** four networked dials, each with `0–3` rotation state. Correct combination
  is a constant array; check on each change.
- **Built:** `CarnivalMirrorLock` on `LOCK_03_MirrorsAside`, four `CarnivalMirrorFrame` panes in
  `MIRROR_FRAMES`, a `RUNE_LIT` and a `LAGGING_REFLECTION` pane. Solution defaults to
  `1, 3, 0, 2`; the four angles are packed two bits each into one synced int.
- **Feedback instead of punishment:** a wrong angle costs nothing — this is the gentlest lock in
  the show. A frame on its solution angle lights its own `FRAME_BEAM`, which is a child of the
  frame so it swings with it; that light is how four strangers converge without a hint.
- **Interaction:** each pane is solid and turns a quarter turn per Interact. It draws whatever
  angle the synced state says, so two guests turning the same mirror cannot disagree.
- **Budget note:** only the entrance mirror is a real reflection. `LAGGING_REFLECTION` stands in
  for the baked capture pass; the other eight belong to the art pass, not the rig.
- **Reveal:** enable the "lagging reflection" prop (a baked duplicate of the mirror view with a
  half-second delay), and start the scripted beat.
- **Budget:** only the entrance mirror is real. The other eight are baked captures with a
  parallax offset; the eighth shows the room *before* the party entered.

## 04 · Balloon Logic — funhouse

- **Players do:** inflate three balloon animals to the colours written on the chalkboard joke.
- **Implementation:** hold-to-fill with a synced float per balloon, snapped to the nearest of
  five colours at release. Three correct = solve.
- **Built:** `CarnivalBalloonLock` on `LOCK_04_BalloonLogic` and three `CarnivalBalloon` props in
  `BALLOON_RIG`, each with a skin that scales with fill, five swatches and a pop burst. Answer
  defaults to colours `1, 3, 0`.
- **Fill, not colour:** the synced value is the fill (0–1), and the colour is `round(fill × 4)`. A
  balloon that jumped between five states would not read as inflating; the float is written
  roughly three times a second while squeezing and always on release. Hold-and-squeeze uses the
  pickup's use events; clicking pumps a quarter — the exact size of one colour band — so a
  desktop test can still reach all five.
- **Nothing to fail:** a wrong colour is simply wrong, and the group keeps squeezing. No scare, no
  reset, no penalty — the balloon lock is the comedy peak, not a punishment.
- **Reset mid-squeeze:** a balloon carries a synced fill epoch that the reset lever moves. A squeeze
  already in progress remembers the epoch it started in, and every tick or release from an older one
  is refused instead of written, so leftover ticks cannot inflate a balloon that was just set to
  empty — even though the guest is still holding the pickup with the button down. Squeezing again
  starts a fresh fill, which is the only way back up.
- **Reveal:** pop all three, spawn confetti, enable the "warmer" card.
- **Comfort:** no flashing; the pop is audio plus a short particle burst.

## 05 · The Boiler's Lullaby — backstage

- **Players do:** play the four-note calliope melody written in the graffiti on four valves.
- **Implementation:** four valve dials (0–3), sequence check on each turn. Wrong note resets the
  input and triggers a reactive scare if the director allows it.
- **Built:** `CarnivalBoilerLock` on `LOCK_05_BoilerLullaby` and four `CarnivalValve` dials in
  `BOILER_VALVES`, each with a solid collider (Interact needs one on the behaviour's own object)
  and a `VALVE_KNOB` that turns 90° per note. Default phrase `2, 0, 3, 1` — change `melody` on the
  boiler to re-key it, and repaint the graffiti hint to match.
- **The check:** valve i must sit on `melody[i]`. A turn that lands on the wrong note springs that
  valve back to zero, so a dial never sits on a lie, hisses `BOILER_STEAM` for 1.2 s and asks
  `SurpriseDirector.TryConsumeScare` for a scare. The director refuses at intensity 0, for a guest
  who has spent their six scares, or inside its 90-second cooldown; `wrongNoteCooldown` adds a 2 s
  local floor so mashing a valve does not queue one scare per click.
- **Networking:** all four positions are packed into one synced int, two bits per valve, so a turn
  costs one serialization and `OnDeserialization` redraws every knob.
- **Testing it alone:** `requiredMask = 16` on `_Systems`, or call `ForceSolve()` on the boiler.
- **Still missing:** the second-hands step the design constraints ask for. The natural place is a
  pressure lever one guest holds while another turns the last valve; it is not built yet, so the
  boiler is currently soloable.
- **Reveal:** the pipes speak a previous guest's line, then the boiler dies and the midway loses
  power — this is the transition into the finale.

## 06 · Curtain — centre ring

- **Players do:** stand on four marks at the same time. The Contortionist can hold a mark alone
  for eight seconds, so a three-player group is not stuck.
- **Implementation:** four trigger volumes, `OnPlayerTriggerEnter/Exit`, set a bitmask per mark;
  a debounce of 0.4 s prevents flicker.
- **Built:** `CarnivalCurtainLock` on `LOCK_06_Curtain`, with four `CarnivalCurtainMark` triggers in
  `CURTAIN_MARKS` on a 2.6 m radius around the centre pole. Each mark is a 1.6 × 1.8 × 1.6 m
  trigger column with a `MARK_PAD` child that lights while held. Nothing on this component is
  synced — occupancy is where guests are standing, and the manager is still the only writer of
  solved state.
- **The solo hold, as implemented:** when the Contortionist steps off a lit mark it stays lit for
  eight seconds, which is what lets three players finish a four-mark puzzle: one guest crosses
  between two pads instead of a fourth body appearing. Switch `contortionistSoloHold` off if you
  would rather the lock be strictly four-handed. The same hold lets a lone Contortionist chain all
  four pads inside the eight-second window — slower than four people, but the lock stays solvable
  by one, which the upload checklist requires.
- **The debounce:** all four must be lit for 0.4 s *and* still be lit when the timer fires. Walking
  across a pad during that window cancels the attempt, so crossing the ring never solves it.
- **Reveal:** lights out, Grinny bows, exit gate unlocks — see the finale below.
- **The centre pad:** `CURTAIN_CENTRE_MARK` sits under the ring on the same component, flagged
  `centreMark` and outside the four-mark array. It is not a fifth mark — it keeps counting after
  the curtain falls, because the finale asks it who stayed (see the endings).
- **Testing it alone:** set `requiredMask` to `32` on `_Systems` and leave the other five locks
  wired. Stand on all four marks — or call `ForceSolve()` on the curtain from a debug button — and
  the gate should open.

## 07 · The finale — exit gate

- **Players see:** the ring practicals die, one figure appears on the perch and bows, then
  `GATE_OPEN_SIGN` lights and `GATE_BARRIER` comes down.
- **Built:** `CarnivalFinale` on `_Systems`, with `HOUSE_LIGHT_*` cut by the blackout, `GRINNY_BOW`
  and `BOW_SPOT` revealed by the bow, and the sign and barrier swapped by the gate beat. Defaults
  are `bowDelay = 2.2` s and `gateDelay = 5` s, both measured from the blackout.
- **One stage, cumulative:** the finale syncs one int, `stage` (0 idle, 1 blackout, 2 bow, 3 gate).
  A client never applies "the finale" — it applies every beat up to and including its stage, and
  puts each beat back to its before-state below that. So `ApplyStage` is idempotent, a duplicate
  serialization cannot double-fire a beat, and stage 0 is a shut gate.
- **Who drives it:** the guest who solved the last lock, once, through
  `CarnivalLockManager.BeginFinale()`. Everyone else reads the stage, which is also why a late
  joiner arrives with the gate already open instead of watching the lights die a second time.
  If the driver leaves between beats, VRChat hands the object to someone else and
  `OnOwnershipTransferred` finishes the run from wherever it stopped — the failure that avoids is
  a solved show with a shut gate and nobody left to open it.
- **Reset mid-sequence:** a beat is scheduled by name, so it cannot carry which run it belongs
  to. Each beat therefore holds the absolute time it is due, and a beat that finds itself early
  waits for that deadline instead of firing. A reset clears the deadlines, and it does so through
  `stage`: 0 is already synced and already means "no run", so every client that sees it drops any
  beat still in flight. The next run's `Begin()` replaces the deadlines with its own, so a beat
  left over from the previous show can only wait for the new one — it cannot open the gate five
  seconds into the next party's run. `BeatGate` also promotes through the bow when `gateDelay` was
  authored shorter, rather than leaving the gate shut for good.
- **Why a sequence:** the three beats are the deck's own ending (slide 19, step at 00:23). Fired
  in one frame they read as a door unlock; in a headset the blackout has to land before the sign
  changes or nobody sees either.
- **The manager's fallback:** `onShowCompleteEnable` / `onShowCompleteDisable` still point at the
  sign and the barrier, but they are used only when no `CarnivalFinale` is wired. That keeps a
  scene without the finale winnable — just instantly.
- **Validator:** a stage saved above 0, a gate delay shorter than the bow delay, an empty
  blackout or bow array, a barrier the finale never opens, the open sign in the wrong array, and
  a manager that was never pointed at the finale.
- **Testing it alone:** `requiredMask = 63` on `_Systems`, then `ForceSolveAll()`; the finale's
  `SkipToEnd()` is the fast path when you only want the gate open.

## 08 · The three endings

The barrier comes down the same way every time — a finale that traps people is not a finale.
What differs is what happens at the moment it opens, and that is chosen once, at the gate beat,
from facts the run already holds.

| Ending | Condition | What the gate looks like |
| --- | --- | --- |
| **Clean break** | nobody stayed in the ring | `GATE_OPEN_SIGN` lights, `ENDING_DAWN` comes up past the doorway |
| **The understudy** | exactly one guest on `CURTAIN_CENTRE_MARK` at the gate beat | the sign lights, `ENDING_UNDERSTUDY_SPOT` stays on the one who stayed, the ring stays dark |
| **Buy the carnival** | all twelve teeth found | `ENDING_DEED_PLATE` at the booth, `ENDING_OWNER_WING` in the hideout |

- **Built:** the conditions live on `CarnivalFinale` (`teeth`, `curtain`) and are read in
  `BeatGate()`, five seconds after the lights die. The ending is written to the synced `ending`
  int *before* `stage` advances, so one serialization carries both and no client can see an open
  gate without knowing which ending it is.
- **Why the gate beat and not the solve:** the understudy is a decision made during the dark, not
  a statistic from earlier in the run. Anyone can step into the ring in those five seconds, and
  the deck's argument is that groups argue about it — which only works if the choice is still
  open when the lights are already out.
- **Teeth:** `CarnivalTeeth` holds one synced mask, twelve bits. `CarnivalTooth` takes an Interact
  and calls `Collect()`, which refuses a bit that is already set — two guests reaching for the
  same tooth in the same frame is one tooth found, not two. The ledger is cleared at the gate
  line, so a second party has to find them again.
- **One overlay at a time:** `ApplyEnding()` writes `onCleanEnable`, `onUnderstudyEnable` and
  `onOwnerEnable` on every stage change, so a guest joining after the gate arrives on the earned
  ending, never on two at once.
- **Validator:** an empty overlay array, the same prop in two endings, a `requiredTeeth` that does
  not match the number of wired teeth, a duplicate tooth index, a tooth with no solid collider, a
  finale not pointed at the ledger or the ring, a missing or unlabelled `CURTAIN_CENTRE_MARK`, an
  overlay that starts visible, and an `ending` saved above 0.
- **Testing them:** `CarnivalTeeth.ForceFindAll()` for the hidden ending; stand alone on
  `CURTAIN_CENTRE_MARK` and let the gate beat arrive for the understudy; step out of the ring for
  the clean break. `CarnivalFinale.SkipToEnd()` decides an ending immediately.

## Design constraints that must survive implementation

- **Two hands, always.** Every lock needs at least one step that requires a second player.
- **No dead ends.** A wrong answer costs time and a scare, never the run.
- **No hint economy.** Hints are free and delivered by Grinny's intercom, because a stuck group
  in VR leaves rather than searches.
- **No gore.** Failures are theatrical, not graphic.
