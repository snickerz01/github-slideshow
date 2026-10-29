using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Lock 01, the ticket tear. Four numbered stubs, one booth mouth, any order:
/// the booth counts distinct ticket numbers, not a sequence, so a group can
/// feed it however they like.
///
/// State is a four-bit mask in one synced int. Each stub hides itself once its
/// bit is set, so nothing about a single stub needs replicating.
///
/// No wrong answer exists here — the booth ignores what it does not want — so
/// there is nothing for this lock to punish.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalTicketLock : UdonSharpBehaviour
{
    [Header("Wiring")]
    [Tooltip("The CarnivalLock on LOCK_01_TicketTear. The fourth stub ends in its Solve().")]
    public CarnivalLock lockRef;

    [Tooltip("The four stubs, in stubId order.")]
    public CarnivalTicketStub[] stubs;

    [Tooltip("Four lamps on the booth, one lit per stub received. Optional.")]
    public GameObject[] stubLights;

    [Tooltip("Revealed when all four are in: the party photograph dated 1986.")]
    public GameObject[] onRevealEnable;

    [Header("Debug")]
    public bool logDeposits = true;

    // Bit 0 is stub 1.
    [UdonSynced] public int depositedMask;

    private bool _solved;

    void Start()
    {
        // A guest joining after the photo appeared still sees it.
        if (lockRef != null && lockRef.manager != null && lockRef.manager.IsSolved(lockRef.lockId))
        {
            _solved = true;
        }

        ApplyDeposits();
    }

    public bool IsDeposited(int stubId)
    {
        if (stubId < 1 || stubId > 4) return false;
        return (depositedMask & (1 << (stubId - 1))) != 0;
    }

    /// <summary>Called by a stub that was dropped into the mouth, or handed over.</summary>
    public void StubDeposited(int stubId)
    {
        if (stubId < 1 || stubId > 4) return;
        if (IsDeposited(stubId)) return;

        TakeOwnership();

        depositedMask |= 1 << (stubId - 1);
        RequestSerialization();
        ApplyDeposits();

        int count = CountDeposited();

        if (logDeposits)
        {
            Debug.Log("[Carnival] Booth took stub " + stubId + ". " + count + " of 4.");
        }

        if (!_solved && count == 4) Reveal();
    }

    public override void OnDeserialization()
    {
        ApplyDeposits();

        // A fresh instance that loads an already-complete mask shows the photo
        // but does not call Solve() again: the client that fed the fourth stub
        // already did.
        if (!_solved && CountDeposited() == 4) RevealLocally();
    }

    private int CountDeposited()
    {
        int count = 0;

        for (int bit = 0; bit < 4; bit++)
        {
            if ((depositedMask & (1 << bit)) != 0) count++;
        }

        return count;
    }

    private void ApplyDeposits()
    {
        if (stubs != null)
        {
            for (int i = 0; i < stubs.Length; i++)
            {
                if (stubs[i] != null) stubs[i].SetDeposited(IsDeposited(stubs[i].stubId));
            }
        }

        if (stubLights != null)
        {
            int count = CountDeposited();

            for (int i = 0; i < stubLights.Length; i++)
            {
                if (stubLights[i] != null) stubLights[i].SetActive(i < count);
            }
        }
    }

    private void Reveal()
    {
        RevealLocally();

        if (lockRef != null) lockRef.Solve();
    }

    private void RevealLocally()
    {
        if (_solved) return;

        _solved = true;

        if (onRevealEnable != null)
        {
            for (int i = 0; i < onRevealEnable.Length; i++)
            {
                if (onRevealEnable[i] != null) onRevealEnable[i].SetActive(true);
            }
        }

        if (logDeposits) Debug.Log("[Carnival] Booth revealed the 1986 photograph.");
    }

    /// <summary>
    /// Two frames after a stub ended a grip that outlived the reset. The stub
    /// cannot schedule this itself — it is inactive for exactly those frames so
    /// that the pickup lets go — and the lock is always active.
    /// </summary>
    public void ScheduleRestore()
    {
        SendCustomEventDelayedFrames(nameof(RestoreStubs), 2);
    }

    /// <summary>
    /// Puts every stub back where the reset says it belongs. Idempotent, so it
    /// is harmless when several stubs were released at once.
    /// </summary>
    public void RestoreStubs()
    {
        if (stubs == null) return;

        for (int i = 0; i < stubs.Length; i++)
        {
            if (stubs[i] != null) stubs[i].RestoreAfterReset();
        }
    }

    /// <summary>
    /// Back to an untouched booth: nothing counted, all four stubs present on
    /// their home marks, the photograph gone.
    ///
    /// The stubs are sent home one by one, each taking ownership of itself, so
    /// the move replicates: a VRC_Pickup syncs its transform from its owner, and
    /// the guest who pulled the reset lever is not necessarily that owner. A stub
    /// still in someone's grip cannot be moved from here at all, so it is sent an
    /// epoch instead and drops itself.
    /// </summary>
    public void ResetPuzzle()
    {
        TakeOwnership();

        depositedMask = 0;
        _solved = false;

        RequestSerialization();
        ApplyDeposits();

        if (stubs != null)
        {
            for (int i = 0; i < stubs.Length; i++)
            {
                if (stubs[i] != null) stubs[i].ReturnHome();
            }
        }

        if (onRevealEnable != null)
        {
            for (int i = 0; i < onRevealEnable.Length; i++)
            {
                if (onRevealEnable[i] != null) onRevealEnable[i].SetActive(false);
            }
        }
    }

    private void TakeOwnership()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }
}
