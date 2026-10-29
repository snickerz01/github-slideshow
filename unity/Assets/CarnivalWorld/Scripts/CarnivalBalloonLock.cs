using UdonSharp;
using UnityEngine;

/// <summary>
/// Lock 04, balloon logic. Three animals, five colours, one chalkboard joke:
/// balloon i has to be targets[i]. There is no wrong-answer penalty — a balloon
/// simply is not the colour the joke asks for, and the group keeps squeezing.
///
/// This component syncs nothing. Each balloon already replicates its own fill,
/// so the colour on screen is the same for everyone; this only holds the answer
/// and decides when the three agree.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarnivalBalloonLock : UdonSharpBehaviour
{
    [Header("Wiring")]
    [Tooltip("The CarnivalLock on LOCK_04_BalloonLogic. The third matching balloon ends in its Solve().")]
    public CarnivalLock lockRef;

    [Tooltip("The three balloons, index matching CarnivalBalloon.balloonIndex.")]
    public CarnivalBalloon[] balloons;

    [Tooltip("Revealed when all three match: confetti, the warmer card.")]
    public GameObject[] onRevealEnable;

    [Header("Answer")]
    [Tooltip("Chalkboard colours, 0-4 per balloon. The joke's punchline, not a code.")]
    public int[] targets = new int[] { 1, 3, 0 };

    [Header("Debug")]
    public bool logColours = true;

    private bool _solved;

    void Start()
    {
        if (lockRef != null && lockRef.manager != null && lockRef.manager.IsSolved(lockRef.lockId))
        {
            _solved = true;
            PopAll();
        }
    }

    /// <summary>Called by a balloon whenever it is released on a colour.</summary>
    public void BalloonCommitted(int index)
    {
        if (_solved) return;

        if (!AllCorrect()) return;

        Solved();
    }

    private bool AllCorrect()
    {
        if (balloons == null || balloons.Length != 3) return false;
        if (targets == null || targets.Length != 3) return false;

        for (int i = 0; i < 3; i++)
        {
            if (balloons[i] == null) return false;
            if (balloons[i].ColourOf() != (targets[i] & 7)) return false;
        }

        return true;
    }

    private void Solved()
    {
        _solved = true;

        if (logColours) Debug.Log("[Carnival] Three balloons match the chalkboard.");

        if (onRevealEnable != null)
        {
            for (int i = 0; i < onRevealEnable.Length; i++)
            {
                if (onRevealEnable[i] != null) onRevealEnable[i].SetActive(true);
            }
        }

        PopAll();

        if (lockRef != null) lockRef.Solve();
    }

    /// <summary>
    /// An empty rig: every balloon unpopped and empty, the reveal gone. Each
    /// balloon owns its own synced fill, so the lock has to send them home one
    /// by one — it has no state of its own to clear.
    /// </summary>
    public void ResetPuzzle()
    {
        _solved = false;

        if (onRevealEnable != null)
        {
            for (int i = 0; i < onRevealEnable.Length; i++)
            {
                if (onRevealEnable[i] != null) onRevealEnable[i].SetActive(false);
            }
        }

        if (balloons == null) return;

        for (int i = 0; i < balloons.Length; i++)
        {
            if (balloons[i] != null) balloons[i].ResetBalloon();
        }
    }

    private void PopAll()
    {
        if (balloons == null) return;

        for (int i = 0; i < balloons.Length; i++)
        {
            if (balloons[i] != null) balloons[i].Pop();
        }
    }

    /// <summary>Debug helper: set every balloon to the answer and take the reveal.</summary>
    public void ForceSolve()
    {
        if (_solved) return;

        if (balloons != null && targets != null)
        {
            for (int i = 0; i < balloons.Length && i < targets.Length; i++)
            {
                if (balloons[i] != null) balloons[i].SetColour(targets[i]);
            }
        }

        Solved();
    }
}
