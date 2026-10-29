using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// The twelve teeth, and the only thing standing between a group and the hidden
/// ending. One synced int holds the whole set: bit N is tooth N.
///
/// The finale asks this component one question at the gate beat — AllFound() —
/// so the hidden ending cannot be faked by a client that merely walked past the
/// props: the mask is owner-written, and every client redraws from it.
///
/// Teeth are found, not held: picking one up is a bit in this mask plus a local
/// render. Nothing is picked up, dropped, thrown or stolen, which is the only
/// way a collectible survives a room full of people grabbing at each other.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalTeeth : UdonSharpBehaviour
{
    [Header("Rules")]
    [Tooltip("How many teeth the hidden ending needs. The deck's number is twelve.")]
    public int requiredTeeth = 12;

    [Header("Wiring")]
    [Tooltip("Every CarnivalTooth in the world. Each client redraws them from the synced mask, " +
             "so a guest who joins late sees the sockets the group already emptied.")]
    public CarnivalTooth[] teeth;

    [Header("Presentation")]
    [Tooltip("Enabled once every tooth is found (a light, a sound, Grinny noticing).")]
    public GameObject[] onAllFoundEnable;

    [Tooltip("Disabled once every tooth is found (the 'twelve' tally card).")]
    public GameObject[] onAllFoundDisable;

    [Header("Debug")]
    public bool logTeeth = true;

    [UdonSynced] public int foundMask;

    // Applied once per run so a serialization storm cannot stack the celebration.
    private bool _celebrated;

    void Start()
    {
        RefreshTeeth();

        if (AllFound()) ApplyAllFound();
    }

    public override void OnDeserialization()
    {
        RefreshTeeth();

        if (AllFound()) ApplyAllFound();
    }

    public bool IsFound(int index)
    {
        if (index < 0 || index > 30) return false;
        return (foundMask & (1 << index)) != 0;
    }

    /// <summary>How many are in the group's pockets. Counted, not stored, so the mask stays the only state.</summary>
    public int FoundCount()
    {
        int count = 0;

        for (int i = 0; i < 31; i++)
        {
            if ((foundMask & (1 << i)) != 0) count++;
        }

        return count;
    }

    /// <summary>True only when the first requiredTeeth teeth are all in. The hidden ending's whole condition.</summary>
    public bool AllFound()
    {
        if (requiredTeeth <= 0 || requiredTeeth > 31) return false;

        int need = 0;

        for (int i = 0; i < requiredTeeth; i++)
        {
            need |= 1 << i;
        }

        return (foundMask & need) == need;
    }

    /// <summary>Called by a CarnivalTooth from a verified Interact.</summary>
    public void Collect(int index)
    {
        if (index < 0 || index > 30) return;
        if (IsFound(index)) return;

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        foundMask |= 1 << index;

        RequestSerialization();
        RefreshTeeth();

        if (logTeeth)
        {
            Debug.Log("[Carnival] Tooth " + (index + 1) + " found — " + FoundCount() + " of " + requiredTeeth + ".");
        }

        if (AllFound())
        {
            if (logTeeth) Debug.Log("[Carnival] All twelve teeth. The hidden ending is live.");

            ApplyAllFound();
        }
    }

    /// <summary>
    /// Clears the ledger at the gate line, so a second party in the same instance
    /// has to find the teeth themselves. This is the one place persistence is
    /// dropped on purpose: the ending is per run, not per instance.
    /// </summary>
    public void ResetRun()
    {
        if (foundMask == 0 && !_celebrated) return;

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        foundMask = 0;
        _celebrated = false;

        RequestSerialization();
        RefreshTeeth();

        // And the celebration comes back down. Without this the next run starts
        // with the last one's tally lights on, which reads as already solved.
        int i = 0;

        if (onAllFoundEnable != null)
        {
            for (i = 0; i < onAllFoundEnable.Length; i++)
            {
                if (onAllFoundEnable[i] != null) onAllFoundEnable[i].SetActive(false);
            }
        }

        if (onAllFoundDisable != null)
        {
            for (i = 0; i < onAllFoundDisable.Length; i++)
            {
                if (onAllFoundDisable[i] != null) onAllFoundDisable[i].SetActive(true);
            }
        }

        if (logTeeth) Debug.Log("[Carnival] Teeth cleared for a new run.");
    }

    /// <summary>Debug helper: use on a test instance only.</summary>
    public void ForceFindAll()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        int need = 0;

        for (int i = 0; i < requiredTeeth && i < 31; i++)
        {
            need |= 1 << i;
        }

        foundMask |= need;

        RequestSerialization();
        RefreshTeeth();
        ApplyAllFound();

        if (logTeeth) Debug.Log("[Carnival] Teeth forced: mask = " + foundMask);
    }

    private void RefreshTeeth()
    {
        if (teeth == null) return;

        for (int i = 0; i < teeth.Length; i++)
        {
            if (teeth[i] != null) teeth[i].ShowFound(IsFound(teeth[i].toothIndex));
        }
    }

    private void ApplyAllFound()
    {
        if (_celebrated) return;

        _celebrated = true;

        int i = 0;

        if (onAllFoundEnable != null)
        {
            for (i = 0; i < onAllFoundEnable.Length; i++)
            {
                if (onAllFoundEnable[i] != null) onAllFoundEnable[i].SetActive(true);
            }
        }

        if (onAllFoundDisable != null)
        {
            for (i = 0; i < onAllFoundDisable.Length; i++)
            {
                if (onAllFoundDisable[i] != null) onAllFoundDisable[i].SetActive(false);
            }
        }
    }
}
