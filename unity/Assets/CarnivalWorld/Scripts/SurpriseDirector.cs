using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Enforces the surprise economy: a per-player cooldown, a per-run budget, and
/// an intensity setting the guest chooses in the hideout.
///
/// This is the component that keeps the world from becoming a jumpscare
/// machine. Every scare in the world asks this director for permission first.
///
/// Player arrays are fixed size because UdonSharp has no dictionaries; 80
/// entries covers a full VRChat instance with room to spare.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SurpriseDirector : UdonSharpBehaviour
{
    [Header("Economy")]
    [Tooltip("Seconds a single player cannot be scared again (deck calls for 90).")]
    public float cooldownSeconds = 90f;

    [Tooltip("Scares a single player may receive in one run (deck calls for 6).")]
    public int scaresPerPlayer = 6;

    [Header("Intensity")]
    [Tooltip("0 = off, 1 = ambient only, 2 = full.")]
    [UdonSynced] public int intensity = 2;

    /// <summary>
    /// Bumped by every reset, and by nothing else. A scare that is booked now
    /// and fires a second later carries the epoch it was booked in, so a lever
    /// pulled in between makes the ticket stale and the scare returns instead of
    /// landing on the party that just walked in.
    /// </summary>
    [UdonSynced] public int runEpoch;

    [Header("Wiring")]
    [Tooltip("Cheap scares that are always allowed (bulb flicker, seat creak).")]
    public GameObject[] ambientProps;

    [Tooltip("Each slot is one scripted beat. Only enabled at intensity 2.")]
    public GameObject[] scriptedBeats;

    private const int Slots = 80;

    private int[] _playerIds = new int[Slots];
    private float[] _lastScareTime = new float[Slots];
    private int[] _scareCount = new int[Slots];

    // The last epoch this client acted on, so a reset arriving from the lever
    // clears the budget here too.
    private int _seenEpoch;

    void Start()
    {
        ClearBudget();
        _seenEpoch = runEpoch;

        ApplyIntensity();
    }

    public void SetIntensity(int value)
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        intensity = value < 0 ? 0 : (value > 2 ? 2 : value);
        RequestSerialization();
        ApplyIntensity();
    }

    public override void OnDeserialization()
    {
        // A reset pulled by someone else. The budgets are per-client, so every
        // client has to hear about it, not just the guest who pulled the lever.
        if (runEpoch != _seenEpoch)
        {
            _seenEpoch = runEpoch;
            ClearBudget();
        }

        ApplyIntensity();
    }

    /// <summary>
    /// Ask before every reactive scare. Returns false when the player is out of
    /// budget, inside the cooldown, or has the director switched off.
    /// </summary>
    public bool CanScare(VRCPlayerApi player)
    {
        if (intensity == 0) return false;
        if (player == null) return false;

        int slot = SlotFor(player.playerId, true);
        if (slot < 0) return false;

        if (_scareCount[slot] >= scaresPerPlayer) return false;
        if (Time.time - _lastScareTime[slot] < cooldownSeconds) return false;

        return true;
    }

    /// <summary>Record a scare that actually fired.</summary>
    public void RegisterScare(VRCPlayerApi player)
    {
        if (player == null) return;

        int slot = SlotFor(player.playerId, true);
        if (slot < 0) return;

        _lastScareTime[slot] = Time.time;
        _scareCount[slot] = _scareCount[slot] + 1;
    }

    /// <summary>Convenience: permission plus booking in one call.</summary>
    public bool TryConsumeScare(VRCPlayerApi player)
    {
        if (!CanScare(player)) return false;
        RegisterScare(player);
        return true;
    }

    /// <summary>
    /// Book a scare that will fire a moment from now, and take the run's ticket
    /// for it. Returns -1 when the economy refuses, which is the answer CanScare
    /// would have given.
    ///
    /// The budget and the cooldown are spent at booking, not at firing: a scare
    /// the group was owed is owed whether or not the prop gets there, and a prop
    /// that has to ask twice could be scheduled twice by one mistake.
    /// </summary>
    public int ScheduleScare(VRCPlayerApi player)
    {
        if (!TryConsumeScare(player)) return -1;

        return runEpoch;
    }

    /// <summary>
    /// Whether a booked scare still belongs to this run. Check it at the moment
    /// the prop actually fires: the reset moves the epoch, so anything booked
    /// before it returns silently rather than landing on the next party.
    /// </summary>
    public bool ScareStillScheduled(int ticket)
    {
        if (ticket < 0) return false;
        if (ticket != runEpoch) return false;

        return intensity != 0;
    }

    public int ScaresRemaining(VRCPlayerApi player)
    {
        if (player == null) return 0;

        int slot = SlotFor(player.playerId, true);
        if (slot < 0) return 0;

        return scaresPerPlayer - _scareCount[slot];
    }

    /// <summary>
    /// Called by the reset lever when a run ends. The epoch moves before the
    /// budgets are published, so a scare booked a frame before the lever went
    /// cannot outlive it.
    /// </summary>
    public void ResetRun()
    {
        ClearBudget();

        runEpoch++;
        _seenEpoch = runEpoch;

        RequestSerialization();
    }

    private void ClearBudget()
    {
        for (int i = 0; i < Slots; i++)
        {
            _scareCount[i] = 0;
            _lastScareTime[i] = -999f;
        }
    }

    private int SlotFor(int playerId, bool create)
    {
        int free = -1;

        for (int i = 0; i < Slots; i++)
        {
            if (_playerIds[i] == playerId) return i;
            if (_playerIds[i] == -1 && free == -1) free = i;
        }

        if (!create || free == -1) return -1;

        _playerIds[free] = playerId;
        return free;
    }

    private void ApplyIntensity()
    {
        int i = 0;

        if (ambientProps != null)
        {
            for (i = 0; i < ambientProps.Length; i++)
            {
                if (ambientProps[i] != null) ambientProps[i].SetActive(intensity >= 1);
            }
        }

        if (scriptedBeats != null)
        {
            for (i = 0; i < scriptedBeats.Length; i++)
            {
                if (scriptedBeats[i] != null) scriptedBeats[i].SetActive(intensity >= 2);
            }
        }
    }
}
