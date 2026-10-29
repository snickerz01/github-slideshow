using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Four jobs, one board. Players claim a role at the hideout board, roles are
/// swapped between runs, and nothing is locked behind progression.
///
/// The board syncs four player ids. Role logic itself lives on the props that
/// care about it (vents that only open for the Contortionist, barrels that only
/// move for the Bearded Lady) so this component stays small.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CarnivalRoleBoard : UdonSharpBehaviour
{
    public const int RoleRingmaster = 0;
    public const int RoleContortionist = 1;
    public const int RoleBeardedLady = 2;
    public const int RoleCannonball = 3;

    [Header("Presentation")]
    [Tooltip("Role cards on the wall, index matches the role constants.")]
    public GameObject[] roleMarkers;

    [Tooltip("Optional lights that show which roles are taken.")]
    public GameObject[] claimedLights;

    [Header("Debug")]
    public bool logClaims = true;

    // Player ids per role: -1 means the role is free.
    [UdonSynced] public int[] claimByRole = new int[] { -1, -1, -1, -1 };

    void Start()
    {
        if (claimByRole == null || claimByRole.Length != 4)
        {
            claimByRole = new int[] { -1, -1, -1, -1 };
        }

        ApplyMarkers();
    }

    /// <summary>Claim a free role. Returns false when it is already taken.</summary>
    public bool ClaimRole(int role)
    {
        if (role < 0 || role > 3) return false;

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        if (claimByRole[role] != -1 && claimByRole[role] != Networking.LocalPlayer.playerId)
        {
            return false;
        }

        int previous = RoleOf(Networking.LocalPlayer);

        // Drop whatever this player already held.
        for (int i = 0; i < 4; i++)
        {
            if (claimByRole[i] == Networking.LocalPlayer.playerId) claimByRole[i] = -1;
        }

        claimByRole[role] = Networking.LocalPlayer.playerId;
        RequestSerialization();
        ApplyMarkers();

        if (logClaims)
        {
            Debug.Log("[Carnival] " + Networking.LocalPlayer.displayName + " claimed role " + role +
                      (previous >= 0 ? " (was " + previous + ")" : ""));
        }

        return true;
    }

    public void ReleaseRole(int role)
    {
        if (role < 0 || role > 3) return;
        if (!Networking.IsOwner(gameObject)) return;

        claimByRole[role] = -1;
        RequestSerialization();
        ApplyMarkers();
    }

    public int RoleOf(VRCPlayerApi player)
    {
        if (player == null || claimByRole == null) return -1;

        for (int i = 0; i < claimByRole.Length; i++)
        {
            if (claimByRole[i] == player.playerId) return i;
        }

        return -1;
    }

    public bool HasRole(VRCPlayerApi player, int role)
    {
        return RoleOf(player) == role;
    }

    public override void OnDeserialization()
    {
        ApplyMarkers();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player == null || claimByRole == null) return;
        if (!Networking.IsOwner(gameObject)) return;

        bool changed = false;

        for (int i = 0; i < claimByRole.Length; i++)
        {
            if (claimByRole[i] == player.playerId)
            {
                claimByRole[i] = -1;
                changed = true;
            }
        }

        if (changed)
        {
            RequestSerialization();
            ApplyMarkers();
        }
    }

    private void ApplyMarkers()
    {
        if (roleMarkers != null)
        {
            for (int i = 0; i < roleMarkers.Length && i < 4; i++)
            {
                if (roleMarkers[i] != null)
                {
                    roleMarkers[i].SetActive(claimByRole[i] != -1);
                }
            }
        }

        if (claimedLights != null)
        {
            for (int i = 0; i < claimedLights.Length && i < 4; i++)
            {
                if (claimedLights[i] != null)
                {
                    claimedLights[i].SetActive(claimByRole[i] != -1);
                }
            }
        }
    }
}
