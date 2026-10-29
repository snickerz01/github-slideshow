using UdonSharp;
using UnityEngine;

/// <summary>
/// One mirror on the wall. Grabbing it turns it a quarter turn and tells the
/// lock; the frame then draws whatever angle the synced state says, so two
/// guests turning the same mirror cannot disagree about where it points.
///
/// The beam is a child of the frame, so it turns with it. When the frame is on
/// its solution angle the beam lights — that light is the only hint this lock
/// needs, and it is visible to the whole group.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarnivalMirrorFrame : UdonSharpBehaviour
{
    [Header("Identity")]
    [Tooltip("0-3, left to right. Must match this frame's slot in CarnivalMirrorLock.frames.")]
    public int frameIndex;

    [Header("Wiring")]
    public CarnivalMirrorLock mirrorLock;

    [Tooltip("The transform that turns. Leave empty to turn this object.")]
    public Transform frame;

    [Tooltip("Beam shown while this frame sits on its solution angle.")]
    public GameObject[] beamWhenAligned;

    [Header("Presentation")]
    public float degreesPerStep = 90f;

    public override void Interact()
    {
        if (mirrorLock == null)
        {
            Debug.LogWarning("[Carnival] Mirror frame " + (frameIndex + 1) + " has no lock assigned.");
            return;
        }

        mirrorLock.TurnFrame(frameIndex);
    }

    /// <summary>Called by the lock whenever the synced angles change.</summary>
    public void ShowAngle(int angle, bool aligned)
    {
        Transform target = frame != null ? frame : transform;
        target.localRotation = Quaternion.Euler(0f, angle * degreesPerStep, 0f);

        if (beamWhenAligned != null)
        {
            for (int i = 0; i < beamWhenAligned.Length; i++)
            {
                if (beamWhenAligned[i] != null) beamWhenAligned[i].SetActive(aligned);
            }
        }
    }
}
