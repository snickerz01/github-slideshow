using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Lock 06, the curtain: the only lock in the show with no prop. Four marks in
/// the centre ring have to be held at the same time, and the Contortionist can
/// keep one lit after stepping off it — otherwise a three-player group is stuck
/// on a puzzle that wants four bodies.
///
/// All of it is local. Every client sees the same trigger events, each one
/// evaluates the marks itself, and the first to finish calls Solve(). The
/// CarnivalLockManager already refuses to set a bit twice, so four clients
/// racing here costs one extra RPC, not a broken run.
///
/// Nothing is synced on this object: occupancy is a fact about where guests are
/// standing, not state worth replicating.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarnivalCurtainLock : UdonSharpBehaviour
{
    [Header("Wiring")]
    [Tooltip("The CarnivalLock on LOCK_06_Curtain. Everything ends in its Solve().")]
    public CarnivalLock lockRef;

    [Tooltip("Needed for the solo hold. Without it all four marks must be held at once.")]
    public CarnivalRoleBoard roleBoard;

    [Tooltip("The four marks, index matching CarnivalCurtainMark.markIndex.")]
    public CarnivalCurtainMark[] marks;

    [Header("Rules")]
    [Tooltip("Seconds the four marks must stay lit before the lock counts. Stops a guest walking across a pad from solving it.")]
    public float debounceSeconds = 0.4f;

    [Tooltip("Seconds a mark stays lit after the Contortionist steps off (deck calls for 8).")]
    public float soloHoldSeconds = 8f;

    [Tooltip("Give a three-player group a way through. Turn off to require four bodies at once.")]
    public bool contortionistSoloHold = true;

    [Header("Presentation")]
    [Tooltip("Enabled once all four marks are held (house lights down, ring goes quiet).")]
    public GameObject[] onAllMarksEnable;

    [Header("Debug")]
    public bool logMarkEvents = true;

    private const int MaxMarks = 8;

    // Guests can share a mark, so count bodies rather than storing one id.
    private int[] _occupancy = new int[MaxMarks];

    // Time.time at which a latched mark goes dark. 0 = not latched.
    private float[] _latchUntil = new float[MaxMarks];

    private int _markCount;
    private bool _armed;
    private float _armedAt;
    private bool _solved;

    // Bodies on the centre pad. Deliberately not one of the four marks: it keeps
    // counting after the curtain falls, because the finale asks it a question
    // five seconds later.
    private int _centreCount;

    void Start()
    {
        _markCount = marks == null ? 0 : marks.Length;
        _centreCount = 0;

        if (_markCount > MaxMarks) _markCount = MaxMarks;

        // A guest who joins after the curtain fell still needs to see it solved.
        if (lockRef != null && lockRef.manager != null && lockRef.manager.IsSolved(lockRef.lockId))
        {
            _solved = true;
            LightAllMarks();
        }
        else
        {
            Refresh();
        }

        if (logMarkEvents && _markCount < 4)
        {
            Debug.LogWarning("[Carnival] Curtain has " + _markCount + " mark(s) wired; it needs four.");
        }
    }

    // ------------------------------------------------------------ marks

    public void MarkEntered(int index, VRCPlayerApi player)
    {
        if (_solved) return;
        if (index < 0 || index >= _markCount) return;

        _occupancy[index] = _occupancy[index] + 1;

        // Someone is standing here: the mark is held for real, not by the latch.
        _latchUntil[index] = 0f;

        if (logMarkEvents)
        {
            Debug.Log("[Carnival] Curtain mark " + (index + 1) + " held by " + player.displayName);
        }

        Refresh();
    }

    public void MarkExited(int index, VRCPlayerApi player)
    {
        if (_solved) return;
        if (index < 0 || index >= _markCount) return;

        _occupancy[index] = _occupancy[index] > 0 ? _occupancy[index] - 1 : 0;

        if (_occupancy[index] == 0 && CanSoloHold(player))
        {
            _latchUntil[index] = Time.time + soloHoldSeconds;

            // One wake-up per latch. Stale timers just re-check and find nothing.
            SendCustomEventDelayedSeconds(nameof(LatchExpired), soloHoldSeconds);

            if (logMarkEvents)
            {
                Debug.Log("[Carnival] Contortionist holds mark " + (index + 1) + " for " +
                          soloHoldSeconds + "s.");
            }
        }

        Refresh();
    }

    private bool CanSoloHold(VRCPlayerApi player)
    {
        if (!contortionistSoloHold) return false;
        if (soloHoldSeconds <= 0f) return false;
        if (roleBoard == null || player == null) return false;

        return roleBoard.HasRole(player, CarnivalRoleBoard.RoleContortionist);
    }

    /// <summary>Drops latches whose eight seconds are up.</summary>
    public void LatchExpired()
    {
        if (_solved) return;

        float now = Time.time;
        bool changed = false;

        for (int i = 0; i < _markCount; i++)
        {
            if (_latchUntil[i] > 0f && now >= _latchUntil[i])
            {
                _latchUntil[i] = 0f;
                changed = true;
            }
        }

        if (changed) Refresh();
    }

    // ------------------------------------------------------- centre pad

    /// <summary>
    /// The centre of the ring, counted rather than latched. This is the understudy
    /// ending's only input: a guest who is still standing here when the gate beat
    /// fires is the one who stayed behind.
    /// </summary>
    public void CentreEntered()
    {
        _centreCount++;

        if (logMarkEvents)
        {
            Debug.Log("[Carnival] Centre pad held by " + _centreCount + " guest(s).");
        }
    }

    public void CentreExited()
    {
        _centreCount = _centreCount > 0 ? _centreCount - 1 : 0;
    }

    public int CentreCount()
    {
        return _centreCount;
    }

    /// <summary>
    /// Exactly one guest in the middle of the ring. One is the ending; two is a
    /// group standing in a circle waiting to be told what to do, and treating that
    /// as an ending would fire it by accident in every session.
    /// </summary>
    public bool CentreMarkSolo()
    {
        return _centreCount == 1;
    }

    // ---------------------------------------------------------- evaluate

    private bool IsLit(int index)
    {
        if (_occupancy[index] > 0) return true;
        return _latchUntil[index] > 0f && Time.time < _latchUntil[index];
    }

    /// <summary>Redraws the pads, then arms or disarms the debounce.</summary>
    private void Refresh()
    {
        if (_solved) return;

        int lit = 0;

        for (int i = 0; i < _markCount; i++)
        {
            bool isLit = IsLit(i);
            if (isLit) lit++;

            if (marks[i] != null) marks[i].SetLit(isLit);
        }

        bool allHeld = _markCount == 4 && lit == 4;

        if (allHeld && !_armed)
        {
            _armed = true;
            _armedAt = Time.time;
            SendCustomEventDelayedSeconds(nameof(Evaluate), debounceSeconds);
        }
        else if (!allHeld)
        {
            _armed = false;
        }
    }

    /// <summary>
    /// Fires debounceSeconds after the fourth mark lit. Re-checks the marks
    /// instead of trusting the timer: a guest stepping off during the wait
    /// cancels that attempt instead of solving a puzzle nobody is standing on.
    /// </summary>
    public void Evaluate()
    {
        if (_solved || !_armed) return;

        if (Time.time - _armedAt < debounceSeconds)
        {
            // Timer fires once per arm; if it arrived early, wait out the rest.
            SendCustomEventDelayedSeconds(nameof(Evaluate), debounceSeconds - (Time.time - _armedAt));
            return;
        }

        for (int i = 0; i < _markCount; i++)
        {
            if (!IsLit(i))
            {
                _armed = false;
                return;
            }
        }

        Solved();
    }

    private void Solved()
    {
        _solved = true;
        _armed = false;

        LightAllMarks();

        if (onAllMarksEnable != null)
        {
            for (int i = 0; i < onAllMarksEnable.Length; i++)
            {
                if (onAllMarksEnable[i] != null) onAllMarksEnable[i].SetActive(true);
            }
        }

        if (logMarkEvents) Debug.Log("[Carnival] Curtain solved — four marks held.");

        if (lockRef != null) lockRef.Solve();
    }

    private void LightAllMarks()
    {
        for (int i = 0; i < _markCount; i++)
        {
            _latchUntil[i] = 0f;
            if (marks[i] != null) marks[i].SetLit(true);
        }
    }

    /// <summary>
    /// The ring back to dark: no latches, no occupancy, the centre count cleared.
    ///
    /// Clearing the centre count is deliberate. A party still standing in the ring
    /// when the lever is pulled has not chosen to stay for the next show, and if
    /// their entry events are never re-sent the count would otherwise carry a
    /// guest into an ending they did not earn.
    /// </summary>
    public void ResetPuzzle()
    {
        _solved = false;
        _armed = false;
        _centreCount = 0;

        for (int i = 0; i < MaxMarks; i++)
        {
            _occupancy[i] = 0;
            _latchUntil[i] = 0f;
        }

        for (int i = 0; i < _markCount; i++)
        {
            if (marks[i] != null) marks[i].SetLit(false);
        }

        if (onAllMarksEnable != null)
        {
            for (int i = 0; i < onAllMarksEnable.Length; i++)
            {
                if (onAllMarksEnable[i] != null) onAllMarksEnable[i].SetActive(false);
            }
        }
    }

    /// <summary>Debug helper: solve the curtain without gathering four guests.</summary>
    public void ForceSolve()
    {
        if (_solved) return;
        _markCount = marks == null ? 0 : marks.Length;
        if (_markCount > MaxMarks) _markCount = MaxMarks;

        Solved();
    }
}
