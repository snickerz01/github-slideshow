using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// The gate line: the one place the run actually begins.
///
/// The clock deliberately does not start on instance join. A clock that starts
/// when the world loads is already a third spent by the time the second party
/// arrives, and a late joiner would inherit someone else's deadline. Instead the
/// first guest to cross this trigger starts the twenty-five minutes for the whole
/// instance, and everyone who arrives after that reads the same synced clock.
///
/// The same crossing clears the previous run's surprise budget, so a group that
/// runs the carnival twice does not start the second run with six scares already
/// spent per player.
///
/// Attach to any trigger volume a guest must cross to reach the midway. The
/// wiring tool creates GATE_LINE for you.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarnivalShowStarter : UdonSharpBehaviour
{
    [Header("Wiring")]
    public CarnivalShowTimer timer;

    [Tooltip("Optional. Cleared when the run starts, so scares are available again.")]
    public SurpriseDirector surprises;

    [Tooltip("Optional. Cleared when the run starts, so a second party has to find the twelve teeth themselves.")]
    public CarnivalTeeth teeth;

    [Tooltip("Optional: enabled once the show has started (lobby sign off, gate lights on).")]
    public GameObject[] onShowStartedEnable;

    [Tooltip("Optional: disabled once the show has started (the 'waiting for guests' sign).")]
    public GameObject[] onShowStartedDisable;

    [Header("Debug")]
    public bool logStarts = true;

    private bool _fired;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;

        if (timer == null)
        {
            Debug.LogWarning("[Carnival] Gate line has no timer assigned.");
            return;
        }

        // Spent for this run. This flag is local to each guest, so it cannot be
        // cleared by whoever pulls the reset lever — a guest who crossed into the
        // first run would otherwise never start the second. The clock is the
        // shared fact: once it has been reset, the flag is nothing.
        if (_fired)
        {
            if (timer.running || timer.timeUp) return;

            _fired = false;
        }

        // Someone else already started this run.
        if (timer.running)
        {
            _fired = true;
            return;
        }

        // The run ended. Reopening the gate is a deliberate act, not a walk-through.
        if (timer.timeUp) return;

        _fired = true;

        if (surprises != null) surprises.ResetRun();
        if (teeth != null) teeth.ResetRun();

        timer.StartShow();
        ApplyStarted();

        if (logStarts)
        {
            Debug.Log("[Carnival] " + player.displayName + " crossed the gate line — show started.");
        }
    }

    /// <summary>
    /// Arms the gate line for the next party and puts the lobby props back to
    /// waiting. Safe only once the clock has been reset: while the clock still
    /// says time up, a crossing is deliberately ignored.
    /// </summary>
    public void ResetRun()
    {
        _fired = false;

        int i = 0;

        if (onShowStartedEnable != null)
        {
            for (i = 0; i < onShowStartedEnable.Length; i++)
            {
                if (onShowStartedEnable[i] != null) onShowStartedEnable[i].SetActive(false);
            }
        }

        if (onShowStartedDisable != null)
        {
            for (i = 0; i < onShowStartedDisable.Length; i++)
            {
                if (onShowStartedDisable[i] != null) onShowStartedDisable[i].SetActive(true);
            }
        }
    }

    private void ApplyStarted()
    {
        int i = 0;

        if (onShowStartedEnable != null)
        {
            for (i = 0; i < onShowStartedEnable.Length; i++)
            {
                if (onShowStartedEnable[i] != null) onShowStartedEnable[i].SetActive(true);
            }
        }

        if (onShowStartedDisable != null)
        {
            for (i = 0; i < onShowStartedDisable.Length; i++)
            {
                if (onShowStartedDisable[i] != null) onShowStartedDisable[i].SetActive(false);
            }
        }
    }
}
