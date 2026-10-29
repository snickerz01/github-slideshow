using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// The twenty-five minute show clock. One synced float, written by the owner
/// once per second instead of every frame, so the network sees ~1500 writes per
/// run rather than 90,000.
///
/// Start it when the party crosses the gate line, not on instance join — late
/// joiners should not inherit a clock that is already half spent.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalShowTimer : UdonSharpBehaviour
{
    [Header("Show")]
    [Tooltip("15 = quick, 25 = standard, 40 = long.")]
    public float showLengthSeconds = 1500f;

    public bool autoStart = false;

    [Header("Wiring")]
    [Tooltip("Turned on while the show runs (gate, music, storm).")]
    public GameObject[] onStartEnable;

    [Tooltip("Turned on when time runs out (Grinny's 'show's over' state).")]
    public GameObject[] onTimeUpEnable;

    [Tooltip("Turned on for the last 60 seconds (warning lights, ticking).")]
    public GameObject[] onFinalMinuteEnable;

    [Header("Broadcast")]
    public CarnivalLockManager manager;

    [UdonSynced] public float elapsedSeconds;
    [UdonSynced] public bool running;
    [UdonSynced] public bool timeUp;

    private bool _finalMinuteFired;

    void Start()
    {
        if (autoStart) StartShow();
        ApplyState();
    }

    public void StartShow()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        elapsedSeconds = 0f;
        running = true;
        timeUp = false;
        RequestSerialization();
        ApplyState();

        SendCustomEventDelayedSeconds(nameof(Tick), 1f);
    }

    public void StopShow()
    {
        if (!Networking.IsOwner(gameObject)) return;

        running = false;
        RequestSerialization();
    }

    public float RemainingSeconds()
    {
        float remaining = showLengthSeconds - elapsedSeconds;
        return remaining > 0f ? remaining : 0f;
    }

    /// <summary>Owner-only, once per second.</summary>
    public void Tick()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (!running) return;

        elapsedSeconds += 1f;

        if (elapsedSeconds >= showLengthSeconds)
        {
            elapsedSeconds = showLengthSeconds;
            running = false;
            timeUp = true;
            RequestSerialization();
            ApplyState();
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetworkTimeUp));
            return;
        }

        // Sync on the second, not on the frame.
        RequestSerialization();
        ApplyState();
        SendCustomEventDelayedSeconds(nameof(Tick), 1f);
    }

    public override void OnDeserialization()
    {
        ApplyState();
    }

    public void NetworkTimeUp()
    {
        ApplyState();
    }

    /// <summary>
    /// The clock back to zero and stopped, so the gate line can start the next
    /// show. Clearing timeUp is the load-bearing part: while it is set,
    /// CarnivalShowStarter ignores every crossing, so the gate line would stay
    /// shut for the rest of the instance's life.
    /// </summary>
    public void ResetRun()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        elapsedSeconds = 0f;
        running = false;
        timeUp = false;
        _finalMinuteFired = false;

        RequestSerialization();
        ApplyState();

        // ApplyState only ever turns the warning props on. Sitting at zero would
        // leave them all dark anyway, but a reset has to be explicit about it:
        // the next party should not inherit last run's ticking lights.
        if (onFinalMinuteEnable != null)
        {
            for (int i = 0; i < onFinalMinuteEnable.Length; i++)
            {
                if (onFinalMinuteEnable[i] != null) onFinalMinuteEnable[i].SetActive(false);
            }
        }
    }

    private void ApplyState()
    {
        int i = 0;
        bool expired = timeUp || RemainingSeconds() <= 0f;

        if (onStartEnable != null)
        {
            for (i = 0; i < onStartEnable.Length; i++)
            {
                if (onStartEnable[i] != null) onStartEnable[i].SetActive(running);
            }
        }

        if (onTimeUpEnable != null)
        {
            for (i = 0; i < onTimeUpEnable.Length; i++)
            {
                if (onTimeUpEnable[i] != null) onTimeUpEnable[i].SetActive(expired);
            }
        }

        bool finalMinute = running && RemainingSeconds() <= 60f;

        if (finalMinute && !_finalMinuteFired)
        {
            _finalMinuteFired = true;

            if (onFinalMinuteEnable != null)
            {
                for (i = 0; i < onFinalMinuteEnable.Length; i++)
                {
                    if (onFinalMinuteEnable[i] != null) onFinalMinuteEnable[i].SetActive(true);
                }
            }
        }
    }
}
