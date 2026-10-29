using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Lock 02, the weight of the show. Three barrels go on the beam's pan; a
/// counterweight slides along the other arm. The beam tilts by the difference,
/// so a guest can see which side is heavy without reading a number — the tilt
/// is the only hint this lock needs.
///
/// The check is a tolerance, not an equation: the beam counts as level while
/// the two sides differ by less than <c>tolerance</c>, and it has to stay level
/// for <c>settleSeconds</c> before the lock opens. That is what stops a barrel
/// knocked past the balance point, or a guest tapping Interact through the
/// good step, from counting as a solve.
///
/// The barrels are the part that needs the group (each one is two hands to
/// carry, see CarnivalBarrel); the counterweight is the part one guest
/// finishes alone.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalScaleLock : UdonSharpBehaviour
{
    [Header("Wiring")]
    [Tooltip("The CarnivalLock on LOCK_02_WeightOfTheShow. A level beam ends in its Solve().")]
    public CarnivalLock lockRef;

    [Tooltip("The three barrels, index matching CarnivalBarrel.barrelIndex.")]
    public CarnivalBarrel[] barrels;

    [Tooltip("Markers on the pan, one per barrel. Children of the beam, so they tilt with it.")]
    public Transform[] sockets;

    [Tooltip("The arm that tilts. Leave empty for no visible tilt — the lock still checks.")]
    public Transform beam;

    [Tooltip("The sliding weight on the short arm.")]
    public Transform counterweight;

    [Tooltip("Revealed when the beam settles (mannequin heads turning, the ring dropping).")]
    public GameObject[] onRevealEnable;

    [Header("Weights")]
    [Tooltip("Kilograms on the counterweight arm at step 0.")]
    public float counterweightStartMass = 56.5f;

    [Tooltip("Kilograms added per step. Against a 0.5 kg tolerance this makes exactly one step count.")]
    public float counterweightStepMass = 1f;

    public int counterweightSteps = 5;

    [Tooltip("Kilograms either side of level that still read as balanced.")]
    public float tolerance = 0.5f;

    [Header("Rules")]
    [Tooltip("Seconds the beam must stay level before the lock counts.")]
    public float settleSeconds = 1f;

    public float maxTiltDegrees = 18f;

    [Header("Presentation")]
    [Tooltip("Step-0 position of the counterweight, as a marker on the beam. Falls back to the vector below " +
             "when empty, but the marker is what follows the prop if it is moved in the scene.")]
    public Transform counterweightHome;

    [Tooltip("Used when counterweightHome is empty, in the counterweight's parent space.")]
    public Vector3 counterweightRestPosition = new Vector3(-0.2f, 0.12f, 0f);

    [Tooltip("Offset per step, applied away from the fulcrum: further out is heavier.")]
    public Vector3 counterweightStepOffset = new Vector3(-0.28f, 0f, 0f);

    [Header("Debug")]
    public bool logBalance = true;

    // 0 is the lightest counterweight setting, counterweightSteps - 1 the heaviest.
    [UdonSynced] public int counterweightStep;

    private bool _solved;
    private bool _armed;
    private float _armedAt;

    void Start()
    {
        // A guest arriving after the beam settled still sees the ring changed.
        if (lockRef != null && lockRef.manager != null && lockRef.manager.IsSolved(lockRef.lockId))
        {
            _solved = true;
            RevealLocally();
        }

        RefreshBalance();
    }

    // --------------------------------------------------------- the weight

    /// <summary>
    /// One Interact nudges the counterweight one step further out and wraps.
    /// Wrapping matters: a guest who walks it past the right step can keep
    /// clicking instead of hunting for a reset.
    /// </summary>
    public override void Interact()
    {
        if (_solved) return;

        TakeOwnership();

        counterweightStep = (Step() + 1) % Mathf.Max(1, counterweightSteps);
        RequestSerialization();
        RefreshBalance();

        if (logBalance)
        {
            Debug.Log("[Carnival] Counterweight step " + counterweightStep + ": pan " + LoadedMass() +
                      " kg, counterweight " + CounterweightMass() + " kg, tolerance " + tolerance + " kg.");
        }
    }

    // ------------------------------------------------------------ public

    public Transform[] SocketMarkers()
    {
        return sockets;
    }

    /// <summary>True while another barrel already occupies that socket.</summary>
    public bool SocketTaken(int index)
    {
        if (barrels == null) return false;

        for (int i = 0; i < barrels.Length; i++)
        {
            if (barrels[i] != null && barrels[i].socketIndex == index) return true;
        }

        return false;
    }

    public int SeatedCount()
    {
        if (barrels == null) return 0;

        int count = 0;

        for (int i = 0; i < barrels.Length; i++)
        {
            if (barrels[i] != null && barrels[i].IsSeated()) count++;
        }

        return count;
    }

    /// <summary>Kilograms currently on the pan. A loose barrel weighs nothing here.</summary>
    public float LoadedMass()
    {
        if (barrels == null) return 0f;

        float sum = 0f;

        for (int i = 0; i < barrels.Length; i++)
        {
            if (barrels[i] != null && barrels[i].IsSeated()) sum += barrels[i].MassOf();
        }

        return sum;
    }

    public float CounterweightMass()
    {
        return counterweightStartMass + Step() * counterweightStepMass;
    }

    /// <summary>Error in kilograms: positive means the pan is heavy.</summary>
    public float BalanceError()
    {
        return LoadedMass() - CounterweightMass();
    }

    // ----------------------------------------------------------- balance

    /// <summary>
    /// Re-weighs the pan. Called by a barrel whenever it is seated, lifted or
    /// released by a remote guest, and by this component after a counterweight
    /// step, so every client draws the same beam.
    /// </summary>
    public void RefreshBalance()
    {
        PlaceSeated();
        MoveCounterweight();

        if (_solved) return;

        int needed = barrels == null ? 0 : barrels.Length;
        int seated = SeatedCount();
        float error = BalanceError();

        ApplyBeam(error);

        bool balanced = needed == 3 && seated == needed && Mathf.Abs(error) <= tolerance;

        if (balanced && !_armed)
        {
            _armed = true;
            _armedAt = Time.time;
            SendCustomEventDelayedSeconds(nameof(Evaluate), settleSeconds);
        }
        else if (!balanced)
        {
            _armed = false;
        }
    }

    /// <summary>
    /// Fires settleSeconds after the beam first read level. Re-weighs instead of
    /// trusting the timer: a barrel taken off, or the counterweight clicked on
    /// past the balance point, cancels that attempt.
    /// </summary>
    public void Evaluate()
    {
        if (_solved || !_armed) return;

        if (Time.time - _armedAt < settleSeconds)
        {
            SendCustomEventDelayedSeconds(nameof(Evaluate), settleSeconds - (Time.time - _armedAt));
            return;
        }

        int needed = barrels == null ? 0 : barrels.Length;

        if (needed != 3 || SeatedCount() != needed)
        {
            _armed = false;
            return;
        }

        if (Mathf.Abs(BalanceError()) > tolerance)
        {
            _armed = false;
            return;
        }

        Solved();
    }

    private void ApplyBeam(float error)
    {
        if (beam == null) return;

        // Two steps of error is the most the beam shows before it reads as
        // slammed to one end, so the tilt stays readable rather than extreme.
        float span = Mathf.Max(0.01f, counterweightStepMass * 2f);
        float tilt = Mathf.Clamp(error / span, -1f, 1f);

        // Positive error means the pan is heavy, so the load arm drops.
        beam.localRotation = Quaternion.Euler(0f, 0f, -tilt * maxTiltDegrees);
    }

    private void PlaceSeated()
    {
        if (barrels == null || sockets == null) return;

        for (int i = 0; i < barrels.Length; i++)
        {
            if (barrels[i] == null) continue;

            int socket = barrels[i].socketIndex;

            if (socket < 0 || socket >= sockets.Length) continue;
            if (sockets[socket] == null) continue;

            barrels[i].SeatAt(sockets[socket]);
        }
    }

    private void MoveCounterweight()
    {
        if (counterweight == null) return;

        counterweight.localPosition = CounterweightRest() + counterweightStepOffset * Step();
    }

    /// <summary>
    /// Where step 0 sits, in the counterweight's parent space. The marker is read
    /// through the parent rather than used as a local position so it still holds
    /// when the beam is tilted or the marker is parented elsewhere: both points
    /// are converted into the same frame before they are subtracted.
    /// </summary>
    private Vector3 CounterweightRest()
    {
        if (counterweightHome == null) return counterweightRestPosition;

        Transform frame = counterweight.parent != null ? counterweight.parent : transform;

        return frame.InverseTransformPoint(counterweightHome.position);
    }

    private int Step()
    {
        int steps = Mathf.Max(1, counterweightSteps);

        return Mathf.Clamp(counterweightStep, 0, steps - 1);
    }

    public override void OnDeserialization()
    {
        RefreshBalance();
    }

    private void Solved()
    {
        if (_solved) return;

        _solved = true;
        _armed = false;

        RevealLocally();

        if (logBalance)
        {
            Debug.Log("[Carnival] The beam is level: three barrels and the counterweight agree.");
        }

        if (lockRef != null) lockRef.Solve();
    }

    private void RevealLocally()
    {
        if (onRevealEnable == null) return;

        for (int i = 0; i < onRevealEnable.Length; i++)
        {
            if (onRevealEnable[i] != null) onRevealEnable[i].SetActive(true);
        }
    }

    /// <summary>Debug helper: settle the beam without three guests and a carry.</summary>
    public void ForceSolve()
    {
        if (_solved) return;

        TakeOwnership();

        int steps = Mathf.Max(1, counterweightSteps);

        counterweightStep = Mathf.Clamp(
            Mathf.RoundToInt((LoadedMass() - counterweightStartMass) / Mathf.Max(0.01f, counterweightStepMass)),
            0, steps - 1);

        RequestSerialization();

        MoveCounterweight();
        ApplyBeam(0f);
        Solved();
    }

    /// <summary>
    /// The counterweight back to its home step, the barrels back on their marks,
    /// the reveal gone, nothing armed.
    ///
    /// The barrels are sent home by the scale rather than by themselves: it is
    /// the one object that knows all three, and a barrel that reset itself would
    /// need a reference back to the ring. Order matters — they are freed from
    /// their sockets before the counterweight moves, so the re-weigh below sees
    /// an empty pan rather than a solution the last group left standing.
    /// </summary>
    public void ResetPuzzle()
    {
        TakeOwnership();

        if (barrels != null)
        {
            for (int i = 0; i < barrels.Length; i++)
            {
                if (barrels[i] != null) barrels[i].ReturnHome();
            }
        }

        counterweightStep = 0;
        _solved = false;
        _armed = false;

        RequestSerialization();

        if (onRevealEnable != null)
        {
            for (int i = 0; i < onRevealEnable.Length; i++)
            {
                if (onRevealEnable[i] != null) onRevealEnable[i].SetActive(false);
            }
        }

        RefreshBalance();
    }

    private void TakeOwnership()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }
}
