using UdonSharp;
using UnityEngine;

/// <summary>
/// One of the four boiler valves. The valve is only a dial: it reports a turn
/// upward and draws whichever note the boiler currently holds it on, so every
/// client sees the same four positions without a synced object per valve.
///
/// UdonSharp delivers Interact() to the behaviour on the object that has the
/// collider, which is why each valve is its own component rather than a list
/// on the boiler.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarnivalValve : UdonSharpBehaviour
{
    [Header("Identity")]
    [Tooltip("0-3, left to right. Must match this valve's slot in CarnivalBoilerLock.valves.")]
    public int valveIndex;

    [Header("Wiring")]
    public CarnivalBoilerLock boiler;

    [Tooltip("Knob that turns to show the note. Optional.")]
    public Transform knob;

    public override void Interact()
    {
        if (boiler == null)
        {
            Debug.LogWarning("[Carnival] Valve " + (valveIndex + 1) + " has no boiler assigned.");
            return;
        }

        boiler.TurnValve(valveIndex);
    }

    /// <summary>Called by the boiler whenever the synced positions change.</summary>
    public void ShowPosition(int position)
    {
        if (knob == null) return;

        knob.localRotation = Quaternion.Euler(0f, 0f, position * 90f);
    }
}
