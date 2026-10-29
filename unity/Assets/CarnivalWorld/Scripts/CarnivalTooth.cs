using UdonSharp;
using UnityEngine;

/// <summary>
/// One of the twelve teeth. Interact takes it; the socket it came out of is
/// emptied on every client by the ledger, not by this object.
///
/// The component owns no state. If two guests reach for the same tooth in the
/// same frame, both calls land in CarnivalTeeth.Collect(), which refuses the
/// second because the bit is already set — the tooth is found once.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarnivalTooth : UdonSharpBehaviour
{
    [Header("Identity")]
    [Tooltip("0-11. Must be unique; the ledger sets this bit and the validator checks for duplicates.")]
    public int toothIndex;

    [Header("Wiring")]
    public CarnivalTeeth teeth;

    [Tooltip("Shown while the tooth is still in its socket. Usually the tooth prop itself.")]
    public GameObject[] onFoundDisable;

    [Tooltip("Shown once the group has it (a scratch on the socket, a tally light).")]
    public GameObject[] onFoundEnable;

    [Header("Debug")]
    public bool logPickups = true;

    public override void Interact()
    {
        if (teeth == null)
        {
            Debug.LogWarning("[Carnival] Tooth " + (toothIndex + 1) + " has no CarnivalTeeth assigned, so it cannot be taken.");
            return;
        }

        teeth.Collect(toothIndex);
    }

    /// <summary>Called by the ledger after every change of the synced mask, and on join.</summary>
    public void ShowFound(bool found)
    {
        int i = 0;

        if (onFoundEnable != null)
        {
            for (i = 0; i < onFoundEnable.Length; i++)
            {
                if (onFoundEnable[i] != null) onFoundEnable[i].SetActive(found);
            }
        }

        if (onFoundDisable != null)
        {
            for (i = 0; i < onFoundDisable.Length; i++)
            {
                if (onFoundDisable[i] != null) onFoundDisable[i].SetActive(!found);
            }
        }
    }
}
