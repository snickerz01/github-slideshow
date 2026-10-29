# Surprise economy — how to spend scares

A scare that repeats is a texture. The director exists so the world cannot spend them faster
than they are worth.

## The budget

| Rule | Value | Where enforced |
| --- | --- | --- |
| Cooldown per player | 90 s | `SurpriseDirector.CanScare()` |
| Scares per player per run | 6 | `SurpriseDirector.scareCount` |
| Intensity levels | 0 off / 1 ambient / 2 full | `SurpriseDirector.intensity` (synced) |
| Scripted beats | 3 per run | `scriptedBeats` array |
| Reset fence | synced `runEpoch` | `ScheduleScare()` books, `ScareStillScheduled()` refuses after a lever pull |
| Flashes | never above 3 Hz, never over 60 ms of white | animation, not code |
| Safe room | hideout is never a scare site | trigger exclusion |

## The four tiers

1. **Ambient** — bulb flicker, seat creak, a calliope note missed. Free, constant, no director
   check needed. This is what makes the world feel alive.
2. **Reactive** — fired by a mistake (wrong valve, dropped ring). Ask the director first.
3. **Scripted** — the three beats: audience turns, reflection lags, Grinny bows. They fire on
   progression, not on the player's behaviour.
4. **Social** — the strength bell, the drop-flap, the mirror that shows the last person who lied.
   Players spend these on each other, which is free atmosphere.

## Wiring a scare

```csharp
// on your scare prop, with a reference to the director
public SurpriseDirector director;
public GameObject scareVisual;

private int _ticket = -1;

public void TriggerScare(VRCPlayerApi player)
{
    _ticket = director.ScheduleScare(player);   // budget + cooldown checked, booked
    if (_ticket < 0) return;

    SendCustomEventDelayedSeconds(nameof(FireScare), 1.2f);   // the face lands late
}

public void FireScare()
{
    // The lever may have been pulled during those 1.2 s. A scare booked for the
    // show before it returns without firing into the next party's run.
    if (!director.ScareStillScheduled(_ticket)) return;

    scareVisual.SetActive(true);
    SendCustomEventDelayedSeconds(nameof(HideScare), 1.5f);
}

public void HideScare()
{
    scareVisual.SetActive(false);
}
```

## Rules for building the scare itself

- **One beat, then back to the room.** No chase, no grab, nothing touching the avatar.
- **Sound before sight.** The calliope cutting out is the warning; the face is the payoff.
- **Never interrupt a two-hand interaction.** A player mid-valve cannot drop what they are doing.
- **A scheduled scare carries its run.** `ScheduleScare()` spends the budget now and hands back the run's
  ticket; the prop asks `ScareStillScheduled(ticket)` at the moment it fires. The reset lever moves
  the epoch, so a scare booked before it is refused — it does not land on the next party's first
  minute. `TryConsumeScare()` stays for scares that fire in the same frame as the mistake.
- **Cooldown survives respawn.** Do not reset the director when a player respawns or rejoins; the
  lever is the only thing that clears budgets.
- **Log it.** `logLockEvents` and the director's counts are what you check before shipping.

## What not to build

- A looping scream.
- A scare on the spawn point — the first thirty seconds are for orientation.
- Anything that requires the player to look somewhere specific. In VR you cannot make them.
- Gore. The whole tone collapses the moment it becomes a wound instead of a joke.
