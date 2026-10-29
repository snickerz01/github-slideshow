using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// The last five seconds of the show, as a sequence instead of a switch.
///
/// Lights out, Grinny bows, the gate opens: three beats on one synced stage int,
/// so every client sees the same thing at the same moment, and a guest who joins
/// late walks into the finished state instead of watching it replay.
///
/// The stage is the state. A client never applies "the finale" — it applies
/// every beat up to and including its stage, and puts each beat back to its
/// before-state when the stage is below it. That makes ApplyStage idempotent:
/// a duplicate serialization cannot double-fire anything, and stage 0 is the
/// closed gate, which is what an instance needs at load.
///
/// The manager hands off here once, from the one client that solved the last
/// lock. Everyone else reads the stage.
///
/// Three endings, chosen at the gate beat from facts the run already holds: the
/// clean break (nobody stayed), the understudy (one guest is still standing in
/// the ring), and the hidden one (all twelve teeth found). The ending is synced
/// with the stage, so the gate opens the same way for everyone.
///
/// A beat is scheduled by name, so it cannot carry which run it belongs to. The
/// fence is therefore the run's own clock: each beat holds the absolute time it
/// is due, and a beat that finds itself early waits for the deadline rather than
/// firing. A reset clears those deadlines on every client — stage 0 is already
/// synced and already means "no run" — and the next run's Begin replaces them
/// with its own, so a beat left over from the previous show can only ever wait
/// for the new one instead of opening the gate five seconds into it.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalFinale : UdonSharpBehaviour
{
    public const int StageIdle = 0;
    public const int StageBlackout = 1;
    public const int StageBow = 2;
    public const int StageOpen = 3;

    public const int EndingUndecided = 0;
    public const int EndingClean = 1;
    public const int EndingUnderstudy = 2;
    public const int EndingOwner = 3;

    [Header("Beats (seconds after the lights die)")]
    [Tooltip("When the figure appears on the perch and bows.")]
    public float bowDelay = 2.2f;

    [Tooltip("When the barrier comes down, the open sign lights, and the ending is chosen. Keep this longer than bowDelay.")]
    public float gateDelay = 5f;

    [Header("1 - Lights out")]
    public GameObject[] onBlackoutEnable;
    public GameObject[] onBlackoutDisable;

    [Header("2 - Grinny bows")]
    public GameObject[] onBowEnable;
    public GameObject[] onBowDisable;

    [Header("3 - The gate opens")]
    public GameObject[] onGateOpenEnable;
    public GameObject[] onGateOpenDisable;

    [Header("Ending — chosen at the gate beat")]
    [Tooltip("The twelve teeth. All found outranks everything else: that is the hidden ending.")]
    public CarnivalTeeth teeth;

    [Tooltip("The curtain's centre pad. Exactly one guest still standing in the ring at the gate beat is the understudy.")]
    public CarnivalCurtainLock curtain;

    [Header("Ending overlays — exactly one set is shown")]
    [Tooltip("Clean break: nobody stayed. Dawn light past the gate, the show is over.")]
    public GameObject[] onCleanEnable;

    [Tooltip("Understudy: one guest held the ring. The spot stays on them and the ring stays dark.")]
    public GameObject[] onUnderstudyEnable;

    [Tooltip("Owner: all twelve teeth. The deed plate and the hideout's owner wing.")]
    public GameObject[] onOwnerEnable;

    [Header("Wiring")]
    public CarnivalLockManager manager;

    [Header("Debug")]
    public bool logBeats = true;

    [UdonSynced] public int stage;

    [UdonSynced] public int ending;

    private bool _running;

    // Absolute times the two beats are due on this client, 0 for "not
    // scheduled". Not synced: each client that drives a sequence owns its own
    // clock, and the reset reaches them through the stage.
    private float _bowAt;
    private float _gateAt;

    void Start()
    {
        // Normalise the scene. At stage 0 this puts the barrier back up, so a
        // world that was saved mid-finale does not start with the exit open.
        ApplyStage(stage);
    }

    public override void OnDeserialization()
    {
        ApplyStage(stage);
    }

    /// <summary>
    /// Start the sequence. Called once by the client that solved the last lock.
    /// Refuses a second call, so a duplicated network event cannot re-run it.
    /// </summary>
    public void Begin()
    {
        if (stage != StageIdle) return;
        if (_running) return;

        _running = true;

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        AdvanceTo(StageBlackout);

        float bow = bowDelay > 0f ? bowDelay : 0f;
        float gate = gateDelay > 0f ? gateDelay : 0f;

        _bowAt = Time.time + bow;
        _gateAt = Time.time + gate;

        SendCustomEventDelayedSeconds(nameof(BeatBow), bow);
        SendCustomEventDelayedSeconds(nameof(BeatGate), gate);

        if (logBeats)
        {
            Debug.Log("[Carnival] Finale begun: lights out, bow at " + bow + "s, gate at " + gate + "s.");
        }
    }

    /// <summary>Beat two. Public because SendCustomEventDelayedSeconds needs a name.</summary>
    public void BeatBow()
    {
        // An idle finale is not a run. This is a beat whose clock outlived the
        // sequence it was scheduled for: the lever was pulled between the
        // blackout and the bow, and stage went back to 0 underneath it.
        if (stage == StageIdle) return;

        // Cleared by the reset, or by a duplicate chain that already fired it.
        if (_bowAt <= 0f) return;

        float wait = _bowAt - Time.time;

        if (wait > 0.01f)
        {
            // A leftover from the show before this one: the deadline it is
            // holding was replaced when the new run began, so it waits for that
            // one instead of bowing the instant a fresh run starts.
            SendCustomEventDelayedSeconds(nameof(BeatBow), wait);
            return;
        }

        _bowAt = 0f;

        AdvanceTo(StageBow);
    }

    /// <summary>
    /// Beat three, and the only moment the ending can be decided: by now the ring
    /// has been dark for five seconds, so whoever is still standing in it chose to
    /// stay. The ending is written before the stage, so one serialization carries
    /// both and no client can see an open gate without knowing which ending it is.
    /// </summary>
    public void BeatGate()
    {
        if (stage == StageIdle) return;

        if (_gateAt <= 0f) return;

        float wait = _gateAt - Time.time;

        if (wait > 0.01f)
        {
            SendCustomEventDelayedSeconds(nameof(BeatGate), wait);
            return;
        }

        _gateAt = 0f;

        // The bow is a precondition, not an assumption: a gateDelay set shorter
        // than bowDelay would otherwise shut the gate for good. A leftover gate
        // from an earlier run cannot take this path, because that run's deadline
        // was already replaced when this one began.
        if (stage == StageBlackout)
        {
            _bowAt = 0f;
            AdvanceTo(StageBow);
        }

        if (stage != StageBow) return;

        ChooseEnding();
        AdvanceTo(StageOpen);
    }

    /// <summary>Debug: jump straight to the open gate. Does not re-run the beats.</summary>
    public void SkipToEnd()
    {
        _running = true;
        _bowAt = 0f;
        _gateAt = 0f;

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        ChooseEnding();
        AdvanceTo(StageOpen);
    }

    /// <summary>
    /// The guest driving the sequence left between beats. VRChat hands the object
    /// to someone else; that guest finishes the run from where it stopped. The
    /// failure this avoids is a solved show with a shut gate and no owner left to
    /// open it.
    /// </summary>
    public override void OnOwnershipTransferred(VRCPlayerApi newOwner)
    {
        if (newOwner == null || !newOwner.isLocal) return;
        if (stage == StageIdle || stage >= StageOpen) return;
        if (_running) return;

        _running = true;

        if (logBeats)
        {
            Debug.LogWarning("[Carnival] Finale resumed at stage " + stage + " by " + newOwner.displayName + ".");
        }

        float gate = gateDelay > 0f ? gateDelay : 0f;

        if (stage == StageBlackout)
        {
            float bow = bowDelay > 0f ? bowDelay : 0f;

            _bowAt = Time.time + bow;
            SendCustomEventDelayedSeconds(nameof(BeatBow), bow);
        }

        _gateAt = Time.time + gate;
        SendCustomEventDelayedSeconds(nameof(BeatGate), gate);
    }

    /// <summary>
    /// Back to a shut gate, no ending, nothing showing. The stage goes to idle,
    /// which is also what makes any beat still in flight return without acting.
    /// </summary>
    public void ResetRun()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        _running = false;
        _bowAt = 0f;
        _gateAt = 0f;
        stage = StageIdle;
        ending = EndingUndecided;

        RequestSerialization();
        ApplyStage(StageIdle);

        if (logBeats) Debug.Log("[Carnival] Finale reset — gate shut, ending cleared.");
    }

    /// <summary>
    /// Which ending this run earned. Decided once, by the owner, at the gate beat;
    /// the value is synced, so a client that arrives later reads the ending the
    /// group actually got instead of guessing from the props.
    /// </summary>
    private void ChooseEnding()
    {
        if (ending != EndingUndecided) return;

        ending = PickEnding();

        if (logBeats)
        {
            Debug.Log("[Carnival] Ending " + EndingName(ending) + ".");
        }
    }

    private int PickEnding()
    {
        // Hidden ending first: it is the hardest thing in the run, so it wins any
        // tie with a guest who happened to be standing still.
        if (teeth != null && teeth.AllFound()) return EndingOwner;

        if (curtain != null && curtain.CentreMarkSolo()) return EndingUnderstudy;

        return EndingClean;
    }

    /// <summary>Readable name for the log line. Instance, not static: UdonSharp is happiest with instance methods.</summary>
    private string EndingName(int value)
    {
        if (value == EndingUnderstudy) return "understudy";
        if (value == EndingOwner) return "buy the carnival";
        if (value == EndingClean) return "clean break";

        return "undecided";
    }

    private void AdvanceTo(int target)
    {
        if (target <= stage) return;

        stage = target;
        RequestSerialization();
        ApplyStage(stage);

        if (logBeats)
        {
            Debug.Log("[Carnival] Finale stage " + stage + ".");
        }
    }

    /// <summary>Draw the whole sequence up to a stage. Safe to call with any value, any number of times.</summary>
    private void ApplyStage(int value)
    {
        // Stage 0 means no run has begun, so no client is driving one. Clearing
        // the flag here is what lets the guest who drove the last finale drive
        // the next: a local flag left true would make Begin() refuse for them.
        //
        // The deadlines go with it. This is the only place a reset reaches a
        // beat already in flight on somebody else's client, and a cleared
        // deadline is what makes that beat return without acting.
        if (value == StageIdle)
        {
            _running = false;
            _bowAt = 0f;
            _gateAt = 0f;
        }

        Apply(value >= StageBlackout, onBlackoutEnable, onBlackoutDisable);
        Apply(value >= StageBow, onBowEnable, onBowDisable);
        Apply(value >= StageOpen, onGateOpenEnable, onGateOpenDisable);

        // Below the gate beat there is nothing to distinguish, and showing an
        // overlay early would tell the group how the run ends before it does.
        ApplyEnding(value >= StageOpen ? ending : EndingUndecided);
    }

    /// <summary>
    /// Exactly one overlay is live. Everything else is forced off, so a guest who
    /// joins after the gate sees the ending the group earned and no second one.
    /// </summary>
    private void ApplyEnding(int value)
    {
        Apply(value == EndingClean, onCleanEnable, null);
        Apply(value == EndingUnderstudy, onUnderstudyEnable, null);
        Apply(value == EndingOwner, onOwnerEnable, null);
    }

    /// <summary>Exactly one of each pair is active: the beat's after-state, or its before.</summary>
    private void Apply(bool fired, GameObject[] enable, GameObject[] disable)
    {
        int i = 0;

        if (enable != null)
        {
            for (i = 0; i < enable.Length; i++)
            {
                if (enable[i] != null) enable[i].SetActive(fired);
            }
        }

        if (disable != null)
        {
            for (i = 0; i < disable.Length; i++)
            {
                if (disable[i] != null) disable[i].SetActive(!fired);
            }
        }
    }
}
