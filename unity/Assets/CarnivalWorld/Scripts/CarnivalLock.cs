using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// A single carnival lock. The lock does not own its own state — the
/// CarnivalLockManager is the single source of truth, so a run only ever
/// pays for one synced object instead of six.
///
/// Wire the puzzle interaction (valve, scale, balloon, beam) to call Solve().
/// Validate the puzzle in the puzzle script before calling; this component
/// trusts the caller.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarnivalLock : UdonSharpBehaviour
{
    [Header("Identity")]
    [Tooltip("1-6. Matches the lock order on the puzzle sheet.")]
    public int lockId = 1;

    public string lockName = "Ticket Tear";

    [Header("Wiring")]
    public CarnivalLockManager manager;

    [Tooltip("Enabled when the lock is solved. Usually the opened door, lit sign or revealed prop.")]
    public GameObject solvedVisual;

    [Tooltip("Optional: enabled once when this lock is solved (Grinny line, light change, sound).")]
    public GameObject[] onSolvedEnable;

    [Tooltip("Optional: disabled when this lock is solved (the locked prop, the 'no entry' sign).")]
    public GameObject[] onSolvedDisable;

    private bool _localSolved;

    void Start()
    {
        if (manager == null)
        {
            Debug.LogWarning("[Carnival] Lock '" + lockName + "' has no manager assigned.");
        }

        Refresh();
    }

    /// <summary>Called by the puzzle script from a verified interaction.</summary>
    public void Solve()
    {
        if (manager == null) return;
        manager.CompleteLock(lockId);
    }

    /// <summary>Called by the manager whenever the solved mask changes.</summary>
    public void Refresh()
    {
        bool solved = manager != null && manager.IsSolved(lockId);

        if (solved == _localSolved) return;
        _localSolved = solved;

        if (solvedVisual != null) solvedVisual.SetActive(solved);

        int i = 0;

        // Both directions, not only the solve. A reset run has to put the "no
        // entry" sign back and take the solved flourish away, or the second
        // party walks in to a scene that still says the lock is done.
        if (onSolvedEnable != null)
        {
            for (i = 0; i < onSolvedEnable.Length; i++)
            {
                if (onSolvedEnable[i] != null) onSolvedEnable[i].SetActive(solved);
            }
        }

        if (onSolvedDisable != null)
        {
            for (i = 0; i < onSolvedDisable.Length; i++)
            {
                if (onSolvedDisable[i] != null) onSolvedDisable[i].SetActive(!solved);
            }
        }
    }
}
