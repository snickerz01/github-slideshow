using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// One balloon animal. Hold it and squeeze to fill; release and it snaps to the
/// nearest of five colours. The fill is the only synced value, and the colour is
/// derived from it, so everybody reads the same balloon.
///
/// Why a float and not a colour index: the fill has to be visible while it is
/// happening — a balloon that teleports between five states is not a balloon.
/// Serialization is throttled to about three writes a second during the fill
/// and always happens on release, which is the only moment the colour matters.
///
/// Comfort: nothing flashes. The pop is a hidden skin plus a burst object, no
/// full-screen flash, per the deck's comfort rule.
///
/// A reset has to beat a squeeze that is already in progress. The fill loop is a
/// queue of delayed events, so zeroing fill on the reset lever would only hold
/// until the next one arrived — and the guest squeezing never got a use-up, so
/// their hand is still on the button. <c>fillEpoch</c> is the fence: every reset
/// moves it, a squeeze remembers the epoch it started in, and any tick or
/// release from an older one is refused instead of written.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalBalloon : UdonSharpBehaviour
{
    [Header("Identity")]
    [Tooltip("0-2. Must match this balloon's slot in CarnivalBalloonLock.balloons.")]
    public int balloonIndex;

    [Header("Wiring")]
    public CarnivalBalloonLock balloonLock;

    [Tooltip("The inflated mesh. Scaled between the two sizes below.")]
    public Transform skin;

    [Tooltip("Five markers in colour order; only the current colour is shown.")]
    public GameObject[] colourSwatches;

    [Tooltip("Enabled when the balloon pops (burst, confetti puff).")]
    public GameObject[] onPopEnable;

    [Header("Fill")]
    [Tooltip("Seconds of squeezing to go from empty to full.")]
    public float fillDurationSeconds = 2.5f;

    [Tooltip("Used when the balloon is clicked instead of held. 0.25 lands on every colour exactly.")]
    public float pumpPerInteract = 0.25f;

    public Vector3 minimumScale = new Vector3(0.25f, 0.25f, 0.25f);
    public Vector3 maximumScale = new Vector3(0.62f, 0.62f, 0.62f);

    [Header("Debug")]
    public bool logFills = true;

    // 0 = empty, 1 = full. Colour index = round(fill * 4).
    [UdonSynced] public float fill;

    /// <summary>
    /// MOVED BY A RESET ONLY. A squeeze records the value it started on; a tick
    /// or a release that finds a different one belongs to a run that is over and
    /// is refused. Synced because the reset usually happens on someone else's
    /// client: that is the only way the guest still holding the balloon learns
    /// that their fill no longer counts.
    /// </summary>
    [UdonSynced] public int fillEpoch;

    private const float TickSeconds = 0.1f;
    private const int TicksPerSync = 3;

    private bool _filling;
    private bool _popped;
    private int _ticks;
    private int _sessionEpoch;
    private int _seenEpoch;

    // ------------------------------------------------------------- filling

    public override void OnPickupUseDown()
    {
        BeginFill();
    }

    public override void OnPickupUseUp()
    {
        Commit();
    }

    public override void OnDrop()
    {
        Commit();
    }

    /// <summary>Click-to-pump fallback, for desktop tests and missing pickups.</summary>
    public override void Interact()
    {
        if (_popped) return;

        TakeOwnership();

        // A click is a fresh, deliberate squeeze, so it starts a new session
        // rather than inheriting whatever the last one was.
        _sessionEpoch = fillEpoch;

        fill = Mathf.Clamp01(fill + pumpPerInteract);
        RequestSerialization();
        ApplyFill();

        Commit();
    }

    private void BeginFill()
    {
        if (_popped) return;

        TakeOwnership();

        _sessionEpoch = fillEpoch;
        _filling = true;
        _ticks = 0;
        SendCustomEventDelayedSeconds(nameof(FillTick), TickSeconds);
    }

    public void FillTick()
    {
        if (!_filling) return;

        if (!Networking.IsOwner(gameObject))
        {
            // Someone else took the balloon mid-squeeze; stop rather than fight.
            _filling = false;
            return;
        }

        // The balloon was reset after this squeeze began. Refuse the tick: no
        // fill is written, nothing further is scheduled, and the guest has to
        // squeeze again on purpose to start filling from empty.
        if (StaleSession()) return;

        fill = Mathf.Clamp01(fill + TickSeconds / Mathf.Max(0.2f, fillDurationSeconds));

        bool full = fill >= 1f;

        if (full) _filling = false;

        _ticks++;

        if (_ticks % TicksPerSync == 0 || full) RequestSerialization();

        ApplyFill();

        if (_filling) SendCustomEventDelayedSeconds(nameof(FillTick), TickSeconds);
    }

    /// <summary>Snap to the nearest colour and tell the lock.</summary>
    private void Commit()
    {
        _filling = false;

        if (_popped) return;

        // A release that lands after a reset must not stamp the old colour back
        // on, and must not tell the lock the balloon is still where the last run
        // left it — that would re-solve the puzzle a moment after it was cleared.
        if (StaleSession()) return;

        TakeOwnership();

        int colour = ColourOf();
        fill = colour * 0.25f;

        RequestSerialization();
        ApplyFill();

        if (logFills)
        {
            Debug.Log("[Carnival] Balloon " + (balloonIndex + 1) + " released on colour " + colour + ".");
        }

        if (balloonLock != null) balloonLock.BalloonCommitted(balloonIndex);
    }

    /// <summary>
    /// True when a reset has happened since this squeeze started. Drops the
    /// session so the delayed chain ends here; the epoch itself is only ever
    /// moved by ResetBalloon.
    /// </summary>
    private bool StaleSession()
    {
        if (_sessionEpoch == fillEpoch) return false;

        _filling = false;
        return true;
    }

    // --------------------------------------------------------------- state

    public int ColourOf()
    {
        return Mathf.RoundToInt(Mathf.Clamp01(fill) * 4f);
    }

    /// <summary>Debug helper: set a colour without squeezing.</summary>
    public void SetColour(int colour)
    {
        TakeOwnership();

        _sessionEpoch = fillEpoch;

        fill = Mathf.Clamp(colour, 0, 4) * 0.25f;
        RequestSerialization();
        ApplyFill();
    }

    public void Pop()
    {
        if (_popped) return;

        _popped = true;
        _filling = false;

        if (skin != null) skin.gameObject.SetActive(false);

        if (colourSwatches != null)
        {
            for (int i = 0; i < colourSwatches.Length; i++)
            {
                if (colourSwatches[i] != null) colourSwatches[i].SetActive(false);
            }
        }

        if (onPopEnable != null)
        {
            for (int i = 0; i < onPopEnable.Length; i++)
            {
                if (onPopEnable[i] != null) onPopEnable[i].SetActive(true);
            }
        }

        // Nobody should be able to refill a popped balloon.
        Collider reach = GetComponent<Collider>();
        if (reach != null) reach.enabled = false;
    }

    public override void OnDeserialization()
    {
        // A reset on another client has to stop the squeeze here immediately.
        // The guest is still gripping the pickup, so without this their queued
        // ticks would carry on inflating a balloon that was just set to empty.
        if (fillEpoch != _seenEpoch)
        {
            _seenEpoch = fillEpoch;
            _filling = false;
        }

        ApplyFill();
    }

    private void ApplyFill()
    {
        float clamped = Mathf.Clamp01(fill);

        if (skin != null && !_popped)
        {
            skin.localScale = Vector3.Lerp(minimumScale, maximumScale, clamped);
        }

        if (colourSwatches == null || _popped) return;

        int colour = ColourOf();

        for (int i = 0; i < colourSwatches.Length; i++)
        {
            if (colourSwatches[i] != null) colourSwatches[i].SetActive(i == colour);
        }
    }

    /// <summary>
    /// Back to empty and unpopped, with the skin and the collider restored —
    /// Pop() takes both away, and a second run needs a balloon to squeeze.
    ///
    /// Moving the epoch is the part that makes it stick: a guest mid-squeeze may
    /// still own this pickup and still hold the use button, so clearing the float
    /// alone would be overwritten by the next delayed tick. After the bump, every
    /// tick and every release from before the reset is refused, on every client.
    /// </summary>
    public void ResetBalloon()
    {
        TakeOwnership();

        _popped = false;
        _filling = false;
        fill = 0f;

        fillEpoch++;
        _seenEpoch = fillEpoch;
        _sessionEpoch = fillEpoch;

        Collider reach = GetComponent<Collider>();
        if (reach != null) reach.enabled = true;

        if (skin != null) skin.gameObject.SetActive(true);

        RequestSerialization();
        ApplyFill();
    }

    private void TakeOwnership()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }
}
