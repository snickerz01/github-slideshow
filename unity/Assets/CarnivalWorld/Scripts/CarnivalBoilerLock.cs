using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Lock 05, the boiler's lullaby. Four valves, four notes, one phrase painted
/// on the boiler in graffiti: valve i has to sit on melody[i].
///
/// The four positions live in a single synced int, two bits per valve, so the
/// whole mechanism costs one networked field per write. A wrong note springs
/// that valve back to zero with a hiss and asks the SurpriseDirector for a
/// scare — the director is allowed to say no, which is the point: a group on
/// intensity 1 or 0 gets the hiss and no jump.
///
/// Nothing here can end a run: the cost of a wrong note is time and possibly a
/// scare, never a lockout.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalBoilerLock : UdonSharpBehaviour
{
    [Header("Wiring")]
    [Tooltip("The CarnivalLock on LOCK_05_BoilerLullaby. The melody ends in its Solve().")]
    public CarnivalLock lockRef;

    [Tooltip("Optional: without it a wrong note hisses but never scares.")]
    public SurpriseDirector surprises;

    [Tooltip("The four valves, index matching CarnivalValve.valveIndex.")]
    public CarnivalValve[] valves;

    [Header("Melody")]
    [Tooltip("The calliope phrase in the graffiti: one note (0-3) per valve, left to right.")]
    public int[] melody = new int[] { 2, 0, 3, 1 };

    [Header("Rules")]
    [Tooltip("Seconds before this boiler will ask for another scare for the same guest.")]
    public float wrongNoteCooldown = 2f;

    [Header("Presentation")]
    [Tooltip("Enabled for hissSeconds after a wrong note (steam, shudder).")]
    public GameObject[] onWrongNoteEnable;

    [Tooltip("Enabled once the phrase is complete (pipes lit, pressure gauge pegged).")]
    public GameObject[] onMelodyCompleteEnable;

    public float hissSeconds = 1.2f;

    [Header("Debug")]
    public bool logNotes = true;

    // Two bits per valve: the entire phrase in one synced int.
    [UdonSynced] public int positions;

    private int _valveCount;
    private int _noteCount;
    private float _nextWrongNoteAllowed;
    private float _hissUntil;
    private bool _solved;

    void Start()
    {
        _valveCount = valves == null ? 0 : valves.Length;
        _noteCount = melody == null ? 0 : melody.Length;

        // A guest arriving after the phrase was played still sees the right dials.
        if (lockRef != null && lockRef.manager != null && lockRef.manager.IsSolved(lockRef.lockId))
        {
            _solved = true;
        }

        ApplyAll();

        if (logNotes && (_valveCount != 4 || _noteCount != 4))
        {
            Debug.LogWarning("[Carnival] Boiler has " + _valveCount + " valve(s) and " + _noteCount +
                             " note(s); it needs four of each.");
        }
    }

    // ------------------------------------------------------------- turning

    /// <summary>Called by a CarnivalValve when a guest turns it.</summary>
    public void TurnValve(int index)
    {
        if (_solved) return;
        if (index < 0 || index >= _valveCount) return;

        TakeOwnership();

        int next = (GetPosition(index) + 1) & 3;
        SetPosition(index, next);
        RequestSerialization();
        ApplyAll();

        if (logNotes)
        {
            Debug.Log("[Carnival] Boiler valve " + (index + 1) + " turned to note " + next + ".");
        }

        if (next == TargetNote(index))
        {
            if (IsComplete()) Solved();
            return;
        }

        WrongNote(index, next);
    }

    /// <summary>
    /// The note is wrong: the valve springs back to zero so a dial never sits on
    /// a lie, and the boiler complains.
    /// </summary>
    private void WrongNote(int index, int played)
    {
        SetPosition(index, 0);
        RequestSerialization();
        ApplyAll();

        Hiss();

        if (logNotes)
        {
            Debug.Log("[Carnival] Boiler rejected note " + played + " on valve " + (index + 1) +
                      " (wanted " + TargetNote(index) + ").");
        }

        AskForScare(Networking.LocalPlayer);
    }

    private void AskForScare(VRCPlayerApi player)
    {
        if (surprises == null || player == null) return;
        if (Time.time < _nextWrongNoteAllowed) return;

        _nextWrongNoteAllowed = Time.time + wrongNoteCooldown;

        // The director owns the economy: intensity 0 refuses outright, and so
        // does a guest who has used their six scares.
        surprises.TryConsumeScare(player);
    }

    private void Hiss()
    {
        _hissUntil = Time.time + hissSeconds;

        if (onWrongNoteEnable != null)
        {
            for (int i = 0; i < onWrongNoteEnable.Length; i++)
            {
                if (onWrongNoteEnable[i] != null) onWrongNoteEnable[i].SetActive(true);
            }
        }

        SendCustomEventDelayedSeconds(nameof(ClearHiss), hissSeconds);
    }

    public void ClearHiss()
    {
        if (Time.time < _hissUntil) return;

        if (onWrongNoteEnable != null)
        {
            for (int i = 0; i < onWrongNoteEnable.Length; i++)
            {
                if (onWrongNoteEnable[i] != null) onWrongNoteEnable[i].SetActive(false);
            }
        }
    }

    // --------------------------------------------------------------- state

    private int TargetNote(int index)
    {
        if (melody == null || index < 0 || index >= melody.Length) return 0;
        return melody[index] & 3;
    }

    private int GetPosition(int index)
    {
        return (positions >> (index * 2)) & 3;
    }

    private void SetPosition(int index, int value)
    {
        int shift = index * 2;
        int cleared = positions & ~(3 << shift);

        positions = cleared | ((value & 3) << shift);
    }

    private bool IsComplete()
    {
        if (_noteCount != 4 || _valveCount != 4) return false;

        for (int i = 0; i < 4; i++)
        {
            if (GetPosition(i) != TargetNote(i)) return false;
        }

        return true;
    }

    private void ApplyAll()
    {
        if (valves == null) return;

        for (int i = 0; i < valves.Length; i++)
        {
            if (valves[i] != null) valves[i].ShowPosition(GetPosition(i));
        }
    }

    public override void OnDeserialization()
    {
        ApplyAll();

        // A fresh instance must react to a phrase that was already played, and
        // a late write cannot un-solve what is already finished.
        if (!_solved && IsComplete()) Solved();
    }

    /// <summary>
    /// Four dials back on zero, no hiss, no lit pipes. The phrase can be played
    /// again from the graffiti instead of starting on its own answer.
    /// </summary>
    public void ResetPuzzle()
    {
        TakeOwnership();

        positions = 0;
        _solved = false;
        _nextWrongNoteAllowed = 0f;
        _hissUntil = 0f;

        RequestSerialization();
        ApplyAll();

        int i = 0;

        if (onWrongNoteEnable != null)
        {
            for (i = 0; i < onWrongNoteEnable.Length; i++)
            {
                if (onWrongNoteEnable[i] != null) onWrongNoteEnable[i].SetActive(false);
            }
        }

        if (onMelodyCompleteEnable != null)
        {
            for (i = 0; i < onMelodyCompleteEnable.Length; i++)
            {
                if (onMelodyCompleteEnable[i] != null) onMelodyCompleteEnable[i].SetActive(false);
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
        if (_solved) return;

        _solved = true;

        if (onMelodyCompleteEnable != null)
        {
            for (int i = 0; i < onMelodyCompleteEnable.Length; i++)
            {
                if (onMelodyCompleteEnable[i] != null) onMelodyCompleteEnable[i].SetActive(true);
            }
        }

        if (logNotes) Debug.Log("[Carnival] Boiler melody complete.");

        if (lockRef != null) lockRef.Solve();
    }

    /// <summary>Debug helper: set the phrase without four guests and a graffiti hint.</summary>
    public void ForceSolve()
    {
        if (_solved) return;

        TakeOwnership();

        for (int i = 0; i < 4; i++) SetPosition(i, TargetNote(i));

        RequestSerialization();
        ApplyAll();
        Solved();
    }
}
