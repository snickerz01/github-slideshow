using UdonSharp;
using UnityEngine;

/// <summary>
/// The next show. One lever, pulled once, that puts the whole run back to the
/// state a new party should find it in: no locks solved, the finale at stage 0,
/// no ending showing, twelve teeth back in their sockets, the clock at zero and
/// the surprise budget unspent.
///
/// Why a lever and not automatic detection: a finished show and a party standing
/// around talking look identical from the world's point of view. Wiping their
/// progress because someone wandered near the gate is the kind of bug that ruins
/// a session, so the reset is an Interact — aimed at, and pulled on purpose.
///
/// The order is the important part. Every puzzle owns a synced value the lock
/// manager knows nothing about: the booth's stubs, the boiler's dials, the
/// mirrors' angles. Clearing the solved mask first would leave those answers in
/// place, and each puzzle would re-solve its lock a frame later — a reset that
/// undoes itself. So the puzzles are cleared at the source, then the mask, then
/// everything that reads the mask.
///
/// This component syncs nothing. Each step writes its own object's state as that
/// object's owner, which is what the rest of the show already does.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarnivalRunReset : UdonSharpBehaviour
{
    [Header("Wiring")]
    [Tooltip("The solved bitmask. Cleared once every puzzle below has been un-solved.")]
    public CarnivalLockManager manager;

    [Tooltip("Stage and ending back to 0, so the gate shuts and no overlay is left showing.")]
    public CarnivalFinale finale;

    [Tooltip("Elapsed back to zero and the run stopped, so the gate line can start the next one.")]
    public CarnivalShowTimer timer;

    [Tooltip("Per-player scare budget reset, so the second party gets six scares each.")]
    public SurpriseDirector surprises;

    [Tooltip("The twelve teeth back in their sockets.")]
    public CarnivalTeeth teeth;

    [Tooltip("The gate line. Without this the next crossing does nothing, because the starter stays fired.")]
    public CarnivalShowStarter starter;

    [Header("The six puzzles — each one un-solved at the source")]
    public CarnivalTicketLock ticketLock;
    public CarnivalScaleLock scaleLock;
    public CarnivalMirrorLock mirrorLock;
    public CarnivalBalloonLock balloonLock;
    public CarnivalBoilerLock boilerLock;
    public CarnivalCurtainLock curtainLock;

    [Header("Rules")]
    [Tooltip("Seconds before the lever will accept another pull. Stops a double Interact from resetting twice.")]
    public float cooldownSeconds = 3f;

    [Header("Presentation")]
    [Tooltip("Enabled after a reset: the 'next show' sign, a lit booth.")]
    public GameObject[] onResetEnable;

    [Tooltip("Disabled after a reset: the sold-out sign, the aftermath lighting.")]
    public GameObject[] onResetDisable;

    [Header("Debug")]
    public bool logResets = true;

    private float _nextAllowed;

    /// <summary>The lever. Deliberate, aimed, one per reset.</summary>
    public override void Interact()
    {
        ResetRun();
    }

    /// <summary>
    /// Put the run back to the start. Safe to call twice: every step below is a
    /// write of a known value, not a toggle.
    /// </summary>
    public void ResetRun()
    {
        if (Time.time < _nextAllowed) return;

        _nextAllowed = Time.time + (cooldownSeconds > 0f ? cooldownSeconds : 0f);

        // 1. The puzzles, before the mask. Each of these owns a synced value the
        //    manager cannot clear for it.
        if (ticketLock != null) ticketLock.ResetPuzzle();
        if (scaleLock != null) scaleLock.ResetPuzzle();
        if (mirrorLock != null) mirrorLock.ResetPuzzle();
        if (balloonLock != null) balloonLock.ResetPuzzle();
        if (boilerLock != null) boilerLock.ResetPuzzle();
        if (curtainLock != null) curtainLock.ResetPuzzle();

        // 2. The mask, and the locks that draw from it.
        if (manager != null) manager.ResetRun();

        // 3. Everything that reads the mask or belongs to the run.
        if (finale != null) finale.ResetRun();
        if (teeth != null) teeth.ResetRun();
        if (surprises != null) surprises.ResetRun();
        if (timer != null) timer.ResetRun();
        if (starter != null) starter.ResetRun();

        ApplyReset();

        if (logResets)
        {
            Debug.Log("[Carnival] Run reset — the next party starts from a clean show.");
        }
    }

    private void ApplyReset()
    {
        int i = 0;

        if (onResetEnable != null)
        {
            for (i = 0; i < onResetEnable.Length; i++)
            {
                if (onResetEnable[i] != null) onResetEnable[i].SetActive(true);
            }
        }

        if (onResetDisable != null)
        {
            for (i = 0; i < onResetDisable.Length; i++)
            {
                if (onResetDisable[i] != null) onResetDisable[i].SetActive(false);
            }
        }
    }

    /// <summary>Debug helper: reset without walking to the lever.</summary>
    public void ForceReset()
    {
        _nextAllowed = 0f;
        ResetRun();
    }
}
