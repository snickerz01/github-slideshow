using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Owns the whole run: which locks are solved, whether the show is complete,
/// and who performs the finale. The manager answers "is the show over";
/// CarnivalFinale answers "what happens next, and when".
///
/// One synced int (the solved bitmask) is the entire network cost of the
/// puzzle chain. Everything else is local presentation.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalLockManager : UdonSharpBehaviour
{
    [Header("Locks")]
    [Tooltip("Usually six: five puzzle locks plus the curtain.")]
    public CarnivalLock[] locks;

    [Tooltip("Bits that must be set for the finale. Default 0b111111 = all six.")]
    public int requiredMask = 63;

    [Header("Finale")]
    [Tooltip("Optional timed finale. When it is wired the sequence owns the last few seconds, and " +
             "the two arrays below are only the fallback for a scene that has no finale.")]
    public CarnivalFinale finale;

    [Tooltip("Enabled when every required lock is solved (exit gate, Grinny bow, lights).")]
    public GameObject[] onShowCompleteEnable;

    [Tooltip("Disabled when every required lock is solved (locked doors, storm sounds).")]
    public GameObject[] onShowCompleteDisable;

    [Header("Debug")]
    public bool logLockEvents = true;

    [UdonSynced] public int solvedMask;

    private bool _showComplete;

    void Start()
    {
        RefreshAllLocks();

        if (IsShowComplete())
        {
            _showComplete = true;

            // A run that is already finished is not performed again. The finale
            // syncs its own stage, so a late joiner reads the finished state
            // instead of watching the lights die for a second time.
            if (finale == null) ApplyShowComplete();
        }
    }

    public bool IsSolved(int lockId)
    {
        if (lockId < 1 || lockId > 31) return false;
        return (solvedMask & (1 << (lockId - 1))) != 0;
    }

    public bool IsShowComplete()
    {
        return (solvedMask & requiredMask) == requiredMask;
    }

    /// <summary>
    /// Called by a CarnivalLock after its puzzle verified the interaction.
    /// Takes ownership so exactly one client writes the state.
    /// </summary>
    public void CompleteLock(int lockId)
    {
        if (lockId < 1 || lockId > 31) return;
        if (IsSolved(lockId)) return;

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        solvedMask |= 1 << (lockId - 1);

        if (logLockEvents)
        {
            Debug.Log("[Carnival] Lock " + lockId + " solved. Mask = " + solvedMask);
        }

        RequestSerialization();
        RefreshAllLocks();

        if (!_showComplete && IsShowComplete())
        {
            _showComplete = true;
            BeginFinale();
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetworkOnShowComplete));
        }
    }

    /// <summary>Debug helper: use on a test instance only.</summary>
    public void ForceSolveAll()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        solvedMask = requiredMask;
        RequestSerialization();
        RefreshAllLocks();

        if (!_showComplete)
        {
            _showComplete = true;
            BeginFinale();
        }
    }

    public override void OnDeserialization()
    {
        RefreshAllLocks();
    }

    /// <summary>
    /// Clear the whole run: every bit, the completion flag, every lock's visuals.
    ///
    /// Called by CarnivalRunReset only after the six puzzles have been un-solved
    /// at the source. Clearing the mask while a puzzle still holds its answer
    /// would let that puzzle re-solve its lock a frame later, which is a reset
    /// that undoes itself.
    /// </summary>
    public void ResetRun()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        solvedMask = 0;
        _showComplete = false;

        RequestSerialization();
        RefreshAllLocks();

        if (logLockEvents) Debug.Log("[Carnival] Locks cleared for the next show.");
    }

    /// <summary>
    /// Everyone receives this, so the finale is not client-authoritative. With a
    /// CarnivalFinale wired it does nothing visual: one client drives the sequence
    /// and the synced stage brings the rest of the instance along.
    /// </summary>
    public void NetworkOnShowComplete()
    {
        _showComplete = true;

        if (finale == null) ApplyShowComplete();
    }

    /// <summary>
    /// Hand the last few seconds to the finale, or fall back to the instant
    /// toggle when there is none. Only ever called from the live solve path:
    /// loading into a finished run must not perform it again.
    /// </summary>
    private void BeginFinale()
    {
        if (finale != null)
        {
            finale.Begin();
            return;
        }

        ApplyShowComplete();
    }

    private void RefreshAllLocks()
    {
        if (locks == null) return;

        for (int i = 0; i < locks.Length; i++)
        {
            if (locks[i] != null) locks[i].Refresh();
        }
    }

    /// <summary>The fallback finale: one frame, no beats. Only used when no CarnivalFinale is wired.</summary>
    private void ApplyShowComplete()
    {
        int i = 0;

        if (onShowCompleteEnable != null)
        {
            for (i = 0; i < onShowCompleteEnable.Length; i++)
            {
                if (onShowCompleteEnable[i] != null) onShowCompleteEnable[i].SetActive(true);
            }
        }

        if (onShowCompleteDisable != null)
        {
            for (i = 0; i < onShowCompleteDisable.Length; i++)
            {
                if (onShowCompleteDisable[i] != null) onShowCompleteDisable[i].SetActive(false);
            }
        }
    }
}
