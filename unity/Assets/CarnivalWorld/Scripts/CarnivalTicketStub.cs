using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// One numbered ticket stub. It knows its number and where the booth's mouth
/// is, so it can decide for itself whether it was dropped inside it.
///
/// A trigger volume on the mouth cannot work here: held objects do not fire
/// trigger events, so "fed the stub in" has to be judged on release, by
/// position, against the mouth's box.
///
/// Interact() is the fallback for anyone who cannot grip the stub — desktop
/// testing, or a pickup the SDK refused to add.
///
/// A reset has to reach a stub that is still in someone's hand, and that is the
/// awkward case: the pickup owns the transform, so moving the stub from another
/// client changes nothing the holder can see, and no drop event is coming to
/// tidy up. <c>homeEpoch</c> carries the reset to them. The grip remembers the
/// epoch it started in, so a release from an older one is refused instead of
/// counting for the booth, and the reset ends the grip by turning the prop off
/// for the frame that a pickup needs to let go.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalTicketStub : UdonSharpBehaviour
{
    [Header("Identity")]
    [Tooltip("1-4. The booth counts distinct numbers, so two stubs must never share one.")]
    public int stubId = 1;

    [Header("Wiring")]
    public CarnivalTicketLock ticketLock;

    [Tooltip("Marker at the booth mouth. A drop counts when the stub is inside its box.")]
    public Transform mouth;

    [Tooltip("Half-size of the mouth in the marker's local space, in metres.")]
    public Vector3 mouthHalfExtents = new Vector3(0.45f, 0.35f, 0.5f);

    [Header("Home")]
    [Tooltip("Where this stub waits for the next party. A reset puts it back here.")]
    public Transform home;

    /// <summary>
    /// MOVED BY A RESET ONLY. Synced, because the stub that needs to hear about
    /// the reset is in someone else's hand, on someone else's client, and a
    /// VRC_Pickup's transform only ever replicates from its owner.
    /// </summary>
    [UdonSynced] public int homeEpoch;

    [Header("Debug")]
    public bool logDrops = true;

    // The last epoch this client accepted, and the epoch the current grip
    // started in. Neither is synced: both are this client's own memory.
    private int _seenEpoch;
    private int _gripEpoch;
    private bool _held;

    public override void OnPickup()
    {
        _held = true;
        _gripEpoch = homeEpoch;
    }

    public override void OnDrop()
    {
        _held = false;

        // The grip began before a reset this client has since seen. It cannot
        // count for the booth, and the stub belongs on its mark rather than
        // wherever the hand happened to open.
        if (_gripEpoch != homeEpoch)
        {
            if (logDrops)
            {
                Debug.Log("[Carnival] Stub " + stubId + " was released from a grip from before the reset.");
            }

            MoveHome();
            return;
        }

        if (ticketLock == null || ticketLock.IsDeposited(stubId)) return;

        if (!InsideMouth())
        {
            if (logDrops)
            {
                Debug.Log("[Carnival] Stub " + stubId + " was dropped outside the booth mouth.");
            }

            return;
        }

        ticketLock.StubDeposited(stubId);
    }

    /// <summary>Hand the stub over instead of dropping it. Same result.</summary>
    public override void Interact()
    {
        if (ticketLock == null || ticketLock.IsDeposited(stubId)) return;

        ticketLock.StubDeposited(stubId);
    }

    public void SetDeposited(bool deposited)
    {
        if (gameObject.activeSelf == deposited) gameObject.SetActive(!deposited);
    }

    public override void OnDeserialization()
    {
        if (homeEpoch == _seenEpoch) return;

        _seenEpoch = homeEpoch;

        // The stub in this client's hand is the one that cannot be moved from
        // the outside: end the grip instead, and let the restore put it home.
        if (_held)
        {
            ForceOutOfHand();
            return;
        }

        if (ticketLock != null && ticketLock.IsDeposited(stubId)) return;

        MoveHome();
    }

    /// <summary>
    /// Back on its mark for the next party: visible again, and standing where it
    /// started rather than wherever the last group left it.
    ///
    /// Taking ownership is the load-bearing part. A VRC_Pickup's transform is
    /// only replicated from its owner, so a stub moved by a client that does not
    /// own it would look reset here and unchanged to everyone else. Claiming the
    /// object first is what makes one guest's reset land for the whole instance.
    ///
    /// The epoch moves before anything else, and it is the only part that always
    /// runs: a stub held by another guest needs to learn that the run is over
    /// even when this client cannot put it down for them.
    /// </summary>
    public void ReturnHome()
    {
        SetDeposited(false);

        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        homeEpoch++;
        _seenEpoch = homeEpoch;

        RequestSerialization();

        if (home == null)
        {
            if (logDrops)
            {
                Debug.LogWarning("[Carnival] Stub " + stubId + " has no home marker, so a reset cannot return it.");
            }

            return;
        }

        if (_held)
        {
            // Reset from inside a grip — desktop Interact, or a lever pulled
            // while holding. Same cure as a remote holder.
            ForceOutOfHand();
            return;
        }

        MoveHome();
    }

    /// <summary>
    /// Puts the stub away for the two frames it takes a pickup to let go, and
    /// asks the lock to bring it back: a disabled behaviour cannot schedule the
    /// event that would restore itself, and the lock is always active.
    /// </summary>
    private void ForceOutOfHand()
    {
        _held = false;

        // The grip epoch is deliberately left behind. Turning the prop off can
        // itself fire OnDrop, and that drop belongs to the old grip: it must be
        // refused, not counted for a booth that was just cleared.
        gameObject.SetActive(false);

        if (ticketLock != null) ticketLock.ScheduleRestore();
    }

    /// <summary>
    /// Called by the lock two frames after a forced release. A stub that reached
    /// the booth in the meantime stays hidden; every other one stands on its
    /// mark, which is where a reset says it belongs.
    /// </summary>
    public void RestoreAfterReset()
    {
        if (ticketLock != null && ticketLock.IsDeposited(stubId))
        {
            SetDeposited(true);
            return;
        }

        SetDeposited(false);
        MoveHome();
    }

    /// <summary>Stand on the home marker. Does nothing without one.</summary>
    public void MoveHome()
    {
        if (home == null) return;

        transform.position = home.position;
        transform.rotation = home.rotation;
    }

    private bool InsideMouth()
    {
        if (mouth == null)
        {
            // Never accept a silent pass: an unwired stub would otherwise count
            // from anywhere in the world, and the puzzle would solve itself.
            if (logDrops) Debug.LogWarning("[Carnival] Stub " + stubId + " has no mouth assigned.");
            return false;
        }

        Vector3 local = mouth.InverseTransformPoint(transform.position);

        return Mathf.Abs(local.x) <= mouthHalfExtents.x &&
               Mathf.Abs(local.y) <= mouthHalfExtents.y &&
               Mathf.Abs(local.z) <= mouthHalfExtents.z;
    }
}
