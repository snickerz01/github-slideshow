using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// One of the four marks in the centre ring. This component exists because
/// UdonSharp delivers OnPlayerTriggerEnter to the behaviour on the trigger
/// object itself, so each mark needs its own small script and the state lives
/// on the single CarnivalCurtainLock that owns them all.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarnivalCurtainMark : UdonSharpBehaviour
{
    [Header("Identity")]
    [Tooltip("0-3. Must match this mark's slot in CarnivalCurtainLock.marks.")]
    public int markIndex;

    [Header("Wiring")]
    public CarnivalCurtainLock curtain;

    [Tooltip("Pad or beam shown while the mark is held. Starts disabled.")]
    public GameObject litVisual;

    [Header("Centre pad")]
    [Tooltip("Tick for the ring's centre pad. It is not one of the four marks: the finale reads it at " +
             "the gate beat to decide the understudy ending, and it keeps counting after the curtain has fallen.")]
    public bool centreMark;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (curtain == null || player == null) return;

        if (centreMark)
        {
            curtain.CentreEntered();
            return;
        }

        curtain.MarkEntered(markIndex, player);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (curtain == null || player == null) return;

        if (centreMark)
        {
            curtain.CentreExited();
            return;
        }

        curtain.MarkExited(markIndex, player);
    }

    public void SetLit(bool lit)
    {
        if (litVisual != null) litVisual.SetActive(lit);
    }
}
