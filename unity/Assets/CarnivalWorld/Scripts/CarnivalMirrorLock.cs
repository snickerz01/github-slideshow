using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Lock 03, the mirror's aside. Four frames, four angles, one beam that has to
/// reach the rune. The answer is a constant: frame i must sit on solution[i].
///
/// A wrong angle costs nothing — the beam simply does not light — so this is
/// the gentlest lock in the show. What it does pay for is feedback: a frame
/// that is right lights its beam for everyone, which is how four strangers
/// converge without a hint.
///
/// Angles live in one synced int, two bits per frame. Nothing else is synced.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalMirrorLock : UdonSharpBehaviour
{
    [Header("Wiring")]
    [Tooltip("The CarnivalLock on LOCK_03_MirrorsAside. The last aligned frame ends in its Solve().")]
    public CarnivalLock lockRef;

    [Tooltip("The four frames, index matching CarnivalMirrorFrame.frameIndex.")]
    public CarnivalMirrorFrame[] frames;

    [Tooltip("Revealed when the beam reaches the rune: lagging reflection, lit rune.")]
    public GameObject[] onRevealEnable;

    [Header("Solution")]
    [Tooltip("Correct angle per frame, 0-3 (x90°). The deck's combination.")]
    public int[] solution = new int[] { 1, 3, 0, 2 };

    [Header("Debug")]
    public bool logTurns = true;

    // Two bits per frame.
    [UdonSynced] public int angles;

    private int _frameCount;
    private bool _solved;

    void Start()
    {
        _frameCount = frames == null ? 0 : frames.Length;

        if (lockRef != null && lockRef.manager != null && lockRef.manager.IsSolved(lockRef.lockId))
        {
            _solved = true;
        }

        ApplyAll();

        if (logTurns && (_frameCount != 4 || (solution != null && solution.Length != 4)))
        {
            Debug.LogWarning("[Carnival] Mirrors have " + _frameCount + " frame(s) and " +
                             (solution == null ? 0 : solution.Length) + " angle(s); the lock needs four of each.");
        }
    }

    // ------------------------------------------------------------- turning

    /// <summary>Called by a frame when a guest grabs and turns it.</summary>
    public void TurnFrame(int index)
    {
        if (_solved) return;
        if (index < 0 || index >= _frameCount) return;

        TakeOwnership();

        int next = (AngleOf(index) + 1) & 3;
        SetAngle(index, next);
        RequestSerialization();
        ApplyAll();

        if (logTurns)
        {
            Debug.Log("[Carnival] Mirror frame " + (index + 1) + " turned to " + (next * 90) + "°" +
                      (IsAligned(index) ? " — beam on." : "."));
        }

        if (IsComplete()) Solved();
    }

    // --------------------------------------------------------------- state

    public int AngleOf(int index)
    {
        return (angles >> (index * 2)) & 3;
    }

    public bool IsAligned(int index)
    {
        return AngleOf(index) == TargetAngle(index);
    }

    private int TargetAngle(int index)
    {
        if (solution == null || index < 0 || index >= solution.Length) return 0;
        return solution[index] & 3;
    }

    private void SetAngle(int index, int value)
    {
        int shift = index * 2;
        int cleared = angles & ~(3 << shift);

        angles = cleared | ((value & 3) << shift);
    }

    private bool IsComplete()
    {
        if (_frameCount != 4) return false;

        for (int i = 0; i < 4; i++)
        {
            if (!IsAligned(i)) return false;
        }

        return true;
    }

    private void ApplyAll()
    {
        if (frames == null) return;

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null) frames[i].ShowAngle(AngleOf(i), IsAligned(i));
        }
    }

    public override void OnDeserialization()
    {
        ApplyAll();

        // Anyone loading a mask that is already correct shows the reveal; only
        // the client that turned the last frame calls Solve().
        if (!_solved && IsComplete()) RevealLocally();
    }

    /// <summary>
    /// Every frame back to angle 0, the rune dark. The solution is a constant, so
    /// the validator errors on an all-zero answer: a reset scene that is already
    /// aligned is a lock the second party never gets to solve.
    /// </summary>
    public void ResetPuzzle()
    {
        TakeOwnership();

        angles = 0;
        _solved = false;

        RequestSerialization();
        ApplyAll();

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

    private void Solved()
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

        if (logTurns) Debug.Log("[Carnival] The beam reaches the rune.");
    }

    /// <summary>Debug helper: align every frame without walking the hall.</summary>
    public void ForceSolve()
    {
        if (_solved) return;

        TakeOwnership();

        for (int i = 0; i < 4; i++) SetAngle(i, TargetAngle(i));

        RequestSerialization();
        ApplyAll();
        Solved();
    }
}
