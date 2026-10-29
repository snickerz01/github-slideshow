using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// One barrel in the weight of the show. Nobody lifts a barrel alone: a guest
/// takes one end, a second guest takes the other, and the barrel follows the
/// midpoint between them. The Bearded Lady can carry one on her own — the
/// puzzle sheet promises that, and it is what keeps the lock solvable by a
/// single guest in a private test instance.
///
/// There is no rigidbody and no VRC_Pickup here. A physics barrel dropped on a
/// tilting beam rolls, jitters and slides off the pan; this one is driven by
/// position and only ever snaps to a socket the scale owns.
///
/// Everything replicated is an id: which end is held, and which socket the
/// barrel sits in. Every client derives the carry position from those ids, so
/// guests see the barrel in the same place without syncing a transform.
///
/// That derivation is also why a reset needs a fence. A reset clears the holders
/// and writes the barrel home, but a guest who was carrying it never got a
/// use-up: for the frame or two before the reset's state reaches their client,
/// Update() is still lerping the barrel towards their hands. <c>resetEpoch</c>
/// moves with every reset, and a packet carrying an older one is refused instead
/// of applied — otherwise a release sent from that stale frame could seat the
/// barrel back on the pan and undo the reset for the whole instance.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalBarrel : UdonSharpBehaviour
{
    [Header("Identity")]
    [Tooltip("0-2. Must match this barrel's slot in CarnivalScaleLock.barrels.")]
    public int barrelIndex;

    [Tooltip("Kilograms. The three barrels have to add up to the counterweight, within the scale's tolerance.")]
    public float mass = 18.4f;

    [Header("Wiring")]
    public CarnivalScaleLock scale;

    [Tooltip("Only needed for the Bearded Lady's solo lift.")]
    public CarnivalRoleBoard roleBoard;

    [Header("Carry")]
    [Tooltip("Ends that must be held before the barrel moves: two guests, or the Bearded Lady alone.")]
    public int carriersNeeded = 2;

    [Tooltip("Metres from the barrel a guest may stand and still take hold of it.")]
    public float grabRadius = 2.5f;

    [Tooltip("Metres from a free socket at which setting the barrel down seats it.")]
    public float snapRadius = 1.2f;

    public float carryHeight = 1.05f;
    public float restHeightAboveFloor = 0.35f;
    public float followLerp = 0.35f;

    [Tooltip("The Bearded Lady lifts a barrel alone, as the puzzle sheet says.")]
    public bool allowBeardedLadySolo = true;

    [Header("Home")]
    [Tooltip("Where this barrel stands when the run is reset: a mark in the ring, off the pan.")]
    public Transform home;

    [Header("Debug")]
    public bool logLifts = true;

    // Player ids, or -1 for a free end. Ids are stable for the instance.
    [UdonSynced] public int holderA = -1;
    [UdonSynced] public int holderB = -1;

    // -1 means the barrel is loose in the ring.
    [UdonSynced] public int socketIndex = -1;

    // Where the barrel was last set down away from the scale. A late joiner
    // needs it, because position is derived rather than synced: y = 0 means
    // the barrel has never rested, so it stays at its scene position.
    [UdonSynced] public Vector3 restPosition;

    /// <summary>
    /// MOVED BY A RESET ONLY, and only ever forwards. It is what lets a client
    /// tell an old carry from a current one: a packet tagged below the epoch
    /// this client has already accepted was written before the reset it has
    /// seen, so applying it would resurrect the carry the reset just cleared.
    /// </summary>
    [UdonSynced] public int resetEpoch;

    // The last epoch this client accepted. Not synced: it is this client's own
    // memory of the reset, which is exactly what the comparison needs.
    private int _seenEpoch;

    public bool IsSeated()
    {
        return socketIndex >= 0;
    }

    public float MassOf()
    {
        return mass > 0.1f ? mass : 0.1f;
    }

    public bool HeldBy(int playerId)
    {
        if (playerId < 0) return false;
        return holderA == playerId || holderB == playerId;
    }

    void Update()
    {
        if (socketIndex >= 0) return;
        if (holderA < 0 && holderB < 0) return;

        FollowHolders();
    }

    // ------------------------------------------------------------ hands

    /// <summary>
    /// Interact takes or releases one end of the barrel. Two ends are needed to
    /// move it; a barrel already seated is lifted off its socket first, so a
    /// guest can correct a placement without calling anything debug-only.
    /// </summary>
    public override void Interact()
    {
        if (scale == null)
        {
            Debug.LogWarning("[Carnival] Barrel " + (barrelIndex + 1) + " has no scale assigned.");
            return;
        }

        VRCPlayerApi local = Networking.LocalPlayer;

        if (local == null) return;

        int id = local.playerId;

        if (HeldBy(id))
        {
            LetGo(id);
            return;
        }

        if (Vector3.Distance(transform.position, local.GetPosition()) > grabRadius)
        {
            if (logLifts) Debug.Log("[Carnival] Barrel " + (barrelIndex + 1) + " is out of reach.");
            return;
        }

        if (socketIndex >= 0)
        {
            // Lifting it off the pan leaves the barrel loose until it is seated
            // again, which is what makes a wrong placement recoverable.
            TakeOwnership();
            socketIndex = -1;
            RequestSerialization();

            if (scale != null) scale.RefreshBalance();
        }

        Grab(id);
    }

    private void Grab(int id)
    {
        TakeOwnership();

        if (holderA < 0) holderA = id;
        else if (holderB < 0) holderB = id;
        else
        {
            if (logLifts)
            {
                Debug.Log("[Carnival] Barrel " + (barrelIndex + 1) + " already has two hands on it.");
            }

            return;
        }

        RequestSerialization();

        if (logLifts)
        {
            bool pair = holderA >= 0 && holderB >= 0;

            Debug.Log(pair
                ? "[Carnival] Barrel " + (barrelIndex + 1) + " lifted by two guests."
                : "[Carnival] Barrel " + (barrelIndex + 1) + " has one end held — it needs a second pair of hands.");
        }

        if (scale != null) scale.RefreshBalance();
    }

    /// <summary>
    /// Letting go either sets the barrel down on a socket it is standing over,
    /// or leaves it on the floor. A barrel with one end still held does neither:
    /// the other guest is still carrying it.
    /// </summary>
    private void LetGo(int id)
    {
        TakeOwnership();

        if (holderA == id) holderA = -1;
        if (holderB == id) holderB = -1;

        int freeSocket = NearestFreeSocket();

        if (freeSocket >= 0)
        {
            socketIndex = freeSocket;
            holderA = -1;
            holderB = -1;
            RequestSerialization();

            if (scale != null) scale.RefreshBalance();

            if (logLifts)
            {
                Debug.Log("[Carnival] Barrel " + (barrelIndex + 1) + " set down on socket " +
                          (freeSocket + 1) + ".");
            }

            return;
        }

        if (holderA < 0 && holderB < 0) RestOnFloor();

        RequestSerialization();

        if (scale != null) scale.RefreshBalance();
    }

    private int NearestFreeSocket()
    {
        if (scale == null) return -1;

        Transform[] sockets = scale.SocketMarkers();

        if (sockets == null) return -1;

        int best = -1;
        float bestDistance = snapRadius;

        for (int i = 0; i < sockets.Length; i++)
        {
            if (sockets[i] == null) continue;
            if (scale.SocketTaken(i)) continue;

            float distance = Vector3.Distance(transform.position, sockets[i].position);

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    // --------------------------------------------------------- carrying

    private void FollowHolders()
    {
        VRCPlayerApi a = FindPlayer(holderA);
        VRCPlayerApi b = FindPlayer(holderB);

        bool hasA = a != null;
        bool hasB = b != null;

        if (!hasA && !hasB) return;

        Vector3 target;

        if (hasA && hasB)
        {
            target = (a.GetPosition() + b.GetPosition()) * 0.5f + Vector3.up * carryHeight;
        }
        else
        {
            VRCPlayerApi solo = hasA ? a : b;
            int soloId = hasA ? holderA : holderB;

            // One pair of hands is not enough unless it is the Bearded Lady.
            if (carriersNeeded > 1 && !SinglyAllowed(soloId)) return;

            target = solo.GetPosition() + Vector3.up * carryHeight +
                     solo.GetRotation() * Vector3.forward * 0.45f;
        }

        transform.position = Vector3.Lerp(transform.position, target, followLerp);
    }

    private bool SinglyAllowed(int playerId)
    {
        if (!allowBeardedLadySolo || roleBoard == null) return false;

        VRCPlayerApi player = FindPlayer(playerId);

        if (player == null) return false;

        return roleBoard.HasRole(player, CarnivalRoleBoard.RoleBeardedLady);
    }

    private VRCPlayerApi FindPlayer(int playerId)
    {
        if (playerId < 0) return null;

        VRCPlayerApi[] all = VRCPlayerApi.AllPlayers;

        if (all == null) return null;

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].playerId == playerId) return all[i];
        }

        return null;
    }

    /// <summary>Called by the scale whenever a barrel seats, so it draws on the pan.</summary>
    public void SeatAt(Transform socket)
    {
        if (socket == null) return;

        transform.position = socket.position;
        transform.rotation = socket.rotation;
    }

    private void RestOnFloor()
    {
        Vector3 rest = transform.position;
        rest.y = restHeightAboveFloor;

        transform.position = rest;
        restPosition = rest;
    }

    public override void OnDeserialization()
    {
        if (resetEpoch < _seenEpoch)
        {
            // Written before a reset this client has already accepted. It still
            // names the old holders or the old socket, so applying it would put
            // the carry, or the barrel on the pan, straight back.
            RejectStalePacket();
            return;
        }

        bool reset = resetEpoch != _seenEpoch;
        _seenEpoch = resetEpoch;

        if (reset)
        {
            // The reset just landed. Someone's Update() may have pulled this
            // barrel a frame away from its mark, and no use-up is coming, so
            // nothing else will put it back.
            PutOnMark();
        }
        else if (socketIndex < 0 && holderA < 0 && holderB < 0 && restPosition.y > 0.001f)
        {
            transform.position = restPosition;
        }

        // The mass on the pan changed for everyone: let the beam re-weigh.
        if (scale != null) scale.RefreshBalance();
    }

    /// <summary>
    /// A packet from before the reset: undo it locally rather than apply it.
    /// The local fields are set back to the reset's answer without serialising —
    /// this client is not the one to re-announce a reset it does not own.
    /// </summary>
    private void RejectStalePacket()
    {
        resetEpoch = _seenEpoch;
        PutOnMark();

        if (logLifts)
        {
            Debug.Log("[Carnival] Barrel " + (barrelIndex + 1) +
                      " ignored a packet from before the reset and went back to its mark.");
        }

        if (scale != null) scale.RefreshBalance();
    }

    /// <summary>
    /// Free both hands, off the pan, standing on the home marker — the same
    /// answer ReturnHome writes, applied locally. Falls back to the synced rest
    /// position when the barrel has no marker, and leaves the barrel where the
    /// accepted reset put it when it has neither.
    /// </summary>
    private void PutOnMark()
    {
        holderA = -1;
        holderB = -1;
        socketIndex = -1;

        if (home != null)
        {
            restPosition = home.position;
            transform.position = home.position;
            transform.rotation = home.rotation;

            return;
        }

        if (restPosition.y > 0.001f) transform.position = restPosition;
    }

    /// <summary>A guest carrying one end left the instance: free that end.</summary>
    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player == null) return;
        if (!Networking.IsOwner(gameObject)) return;

        bool changed = false;

        if (holderA == player.playerId)
        {
            holderA = -1;
            changed = true;
        }

        if (holderB == player.playerId)
        {
            holderB = -1;
            changed = true;
        }

        if (!changed) return;

        if (holderA < 0 && holderB < 0 && socketIndex < 0) RestOnFloor();

        RequestSerialization();

        if (scale != null) scale.RefreshBalance();
    }

    /// <summary>
    /// Back on its mark in the ring: both hands freed, off the pan, standing at
    /// home. The spot is written into restPosition, which is already synced, so
    /// every client puts the barrel back without a second piece of state.
    ///
    /// Clearing the holders is not tidiness: Update() would otherwise keep
    /// lerping a barrel that is being carried straight back out of its home.
    /// The epoch moves first, so the whole packet — new epoch, no holders, home
    /// position — reads as one reset, and a carry written a frame later is
    /// recognisably older instead of indistinguishable from the current state.
    /// </summary>
    public void ReturnHome()
    {
        TakeOwnership();

        resetEpoch++;
        _seenEpoch = resetEpoch;

        holderA = -1;
        holderB = -1;
        socketIndex = -1;

        if (home != null)
        {
            restPosition = home.position;

            transform.position = home.position;
            transform.rotation = home.rotation;
        }
        else
        {
            if (logLifts)
            {
                Debug.LogWarning("[Carnival] Barrel " + (barrelIndex + 1) +
                                 " has no home marker, so a reset can free it but not put it back.");
            }

            RestOnFloor();
        }

        RequestSerialization();

        if (scale != null) scale.RefreshBalance();
    }

    private void TakeOwnership()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }
}
