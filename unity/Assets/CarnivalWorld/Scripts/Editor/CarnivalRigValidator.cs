#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pre-upload check for the wired show rig. Answers one question: if I upload
/// this right now, will the run actually work?
///
/// Three classes of problem, which are the three that kill a test instance:
///
///   empty references   the wiring tool could not find a component, field or
///                      marker, and the run silently does nothing at that step
///   mask mismatches    requiredMask disagrees with the locks that exist, so the
///                      finale either fires immediately or can never fire
///   unreachable        a trigger that is disabled, buried in solid geometry, or
///     triggers         too small to cross — the clock never starts
///
/// UdonSharp types are resolved by name, like the wiring and budget tools, so
/// this compiles and runs whatever the project has installed.
///
/// Read-only: it inspects the scene and reports. It changes nothing, so it is
/// safe to run at any point in a build.
///
/// Menu: Carnival World > 5. Validate Rig (pre-upload)
/// </summary>
public static class CarnivalRigValidator
{
    private const string RootName = "CarnivalWorld_Greybox";
    private const int MaxLockId = 31;
    private const int RoleCount = 4;

    private enum Severity
    {
        Note = 0,
        Warning = 1,
        Error = 2
    }

    private class Finding
    {
        public Severity Severity;
        public string Area;
        public string Message;
        public UnityEngine.Object Context;
    }

    private static readonly List<Finding> Findings = new List<Finding>();

    [MenuItem("Carnival World/5. Validate Rig (pre-upload)", false, 5)]
    public static void ValidateRig()
    {
        Findings.Clear();

        GameObject root = GameObject.Find(RootName);

        if (root == null)
        {
            EditorUtility.DisplayDialog(
                "Carnival World — rig check",
                "No " + RootName + " in this scene.\n\n" +
                "Run Carnival World > 0. New Playable Greybox to build and wire one.",
                "Close");

            return;
        }

        // ---- the four systems --------------------------------------------
        Component manager = Single("CarnivalLockManager", "systems");
        Component timer = Single("CarnivalShowTimer", "systems");
        Component roles = Single("CarnivalRoleBoard", "systems");
        Component surprises = Single("SurpriseDirector", "systems");

        // ---- each one's own contract -------------------------------------
        if (manager != null)
        {
            CheckLocks(root, manager);
            CheckMask(manager);
            CheckFinale(manager);
        }

        if (timer != null) CheckTimer(timer);

        if (roles != null) CheckRoleBoard(roles);

        if (surprises != null) CheckSurprises(surprises);

        CheckCurtain(root);
        CheckBoiler(root);
        CheckTickets(root);
        CheckMirrors(root);
        CheckBalloons(root);
        CheckScale(root);
        CheckGate(root, manager);
        CheckFinaleSequence(root, manager);
        CheckEndings(root, manager);
        CheckReset(root, manager, surprises);
        CheckTriggers(root, timer, surprises);
        CheckDescriptor();

        Report(root);
    }

    // ---------------------------------------------------------------- locks

    private static void CheckLocks(GameObject root, Component manager)
    {
        Type lockType = CarnivalRigWiring.FindType("CarnivalLock");

        if (lockType == null)
        {
            Add(Severity.Error, "locks", "no compiled type named CarnivalLock, so nothing on the anchors can be checked.", manager);
            return;
        }

        List<Component> onAnchors = new List<Component>();
        HashSet<int> ids = new HashSet<int>();

        for (int i = 0; i < CarnivalRigWiring.LockAnchors.Length; i++)
        {
            string anchorName = CarnivalRigWiring.LockAnchors[i];
            GameObject anchor = CarnivalRigWiring.FindDeep(root, anchorName);

            if (anchor == null)
            {
                Add(Severity.Error, "locks", "anchor " + anchorName + " is missing, so lock " + (i + 1) + " has nowhere to live.", root);
                continue;
            }

            Component lockBehaviour = anchor.GetComponent(lockType);

            if (lockBehaviour == null)
            {
                Add(Severity.Error, "locks", anchorName + " carries no CarnivalLock — that lock can never be solved.", anchor);
                continue;
            }

            onAnchors.Add(lockBehaviour);

            int id = Int(lockBehaviour, "lockId");

            if (id < 1 || id > MaxLockId)
            {
                Add(Severity.Error, "locks", anchorName + " has lockId " + id + ", outside 1–" + MaxLockId + "; the solved bitmask cannot store it.", lockBehaviour);
            }
            else if (!ids.Add(id))
            {
                Add(Severity.Error, "locks", "lockId " + id + " is used twice — solving either one would mark both.", lockBehaviour);
            }
            else if (id != i + 1)
            {
                Add(Severity.Warning, "locks", anchorName + " should be lockId " + (i + 1) + " but is " + id + "; the puzzle sheet order no longer matches the scene.", lockBehaviour);
            }

            Component backlink = Field(lockBehaviour, "manager") as Component;

            if (backlink == null)
            {
                Add(Severity.Error, "locks", anchorName + " has no manager assigned, so Solve() returns without doing anything.", lockBehaviour);
            }
            else if (backlink != manager)
            {
                Add(Severity.Error, "locks", anchorName + " points at a different manager than the one on _Systems; its solve would be lost.", lockBehaviour);
            }

            GameObject solvedVisual = Field(lockBehaviour, "solvedVisual") as GameObject;

            if (solvedVisual == null)
            {
                Add(Severity.Warning, "locks", anchorName + " has no solvedVisual, so a solved lock looks identical to an unsolved one.", lockBehaviour);
            }
            else if (solvedVisual.activeSelf)
            {
                Add(Severity.Warning, "locks", solvedVisual.name + " starts enabled, so guests see a solved lock before the run begins.", solvedVisual);
            }

            if (string.IsNullOrEmpty(Field(lockBehaviour, "lockName") as string))
            {
                Add(Severity.Note, "locks", anchorName + " has no lockName; logs will not say which lock completed.", lockBehaviour);
            }

            if (!LockHasCaller(lockBehaviour))
            {
                Add(Severity.Note, "locks",
                    anchorName + " is wired but nothing in the scene holds a reference to it, so no puzzle can call Solve(). " +
                    "The visual is there; the interaction is the missing half.", lockBehaviour);
            }
        }

        // ---- the manager's own array must match the anchors --------------
        Array wired = Field(manager, "locks") as Array;

        if (wired == null || wired.Length == 0)
        {
            Add(Severity.Error, "locks", "manager.locks is empty, so no lock can complete and the finale never fires.", manager);
            return;
        }

        for (int i = 0; i < wired.Length; i++)
        {
            Component entry = wired.GetValue(i) as Component;

            if (entry == null)
            {
                Add(Severity.Error, "locks", "manager.locks[" + i + "] is empty; that slot is skipped at runtime.", manager);
            }
            else if (!onAnchors.Contains(entry))
            {
                Add(Severity.Warning, "locks", "manager.locks[" + i + "] (" + entry.name + ") is not on one of the six anchors; it is probably a stray copy.", entry);
            }
        }

        for (int i = 0; i < onAnchors.Count; i++)
        {
            if (!ArrayContains(wired, onAnchors[i]))
            {
                Add(Severity.Error, "locks", onAnchors[i].name + " is wired to the manager's field but missing from manager.locks, so solving it does nothing.", onAnchors[i]);
            }
        }
    }

    /// <summary>
    /// True when some component in the scene has a field pointing at this lock.
    /// The manager's array is skipped on purpose: being listed there is not the
    /// same as being solvable. A puzzle that forgets to reference its lock is
    /// invisible at runtime — the prop reacts, nothing unlocks.
    /// </summary>
    private static bool LockHasCaller(Component lockBehaviour)
    {
        UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType<Component>(true);

        for (int i = 0; i < all.Length; i++)
        {
            Component candidate = all[i];

            if (candidate == null || candidate == lockBehaviour) continue;

            FieldInfo[] fields = candidate.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);

            for (int f = 0; f < fields.Length; f++)
            {
                if (!typeof(UnityEngine.Object).IsAssignableFrom(fields[f].FieldType)) continue;
                if (fields[f].FieldType.IsArray) continue;

                if (fields[f].GetValue(candidate) == lockBehaviour) return true;
            }
        }

        return false;
    }

    private static void CheckMask(Component manager)
    {
        int required = Int(manager, "requiredMask");
        int wired = 0;

        Array array = Field(manager, "locks") as Array;

        if (array != null)
        {
            for (int i = 0; i < array.Length; i++)
            {
                Component entry = array.GetValue(i) as Component;
                if (entry == null) continue;

                int id = Int(entry, "lockId");
                if (id < 1 || id > MaxLockId) continue;

                wired |= 1 << (id - 1);
            }
        }

        if (required == 0)
        {
            Add(Severity.Error, "mask", "requiredMask is 0, so the show counts as complete before the first guest arrives.", manager);
            return;
        }

        int missing = required & ~wired;

        if (missing != 0)
        {
            Add(Severity.Error, "mask",
                "requiredMask needs lock(s) " + ListBits(missing) + " but no such lock exists — the finale can never fire. " +
                "This is the failure that only shows up after the 25 minutes are spent.", manager);
        }

        int extra = wired & ~required;

        if (extra != 0)
        {
            Add(Severity.Warning, "mask",
                "lock(s) " + ListBits(extra) + " are wired but not required, so solving them does nothing.", manager);
        }

        int allSix = 0;

        for (int i = 0; i < CarnivalRigWiring.LockAnchors.Length; i++)
        {
            allSix |= 1 << i;
        }

        if (required != allSix)
        {
            Add(Severity.Note, "mask",
                "requiredMask is " + required + " (" + ListBits(required) + "); the full run is " + allSix + " (" + ListBits(allSix) + ").", manager);
        }
    }

    private static void CheckFinale(Component manager)
    {
        Array enable = Field(manager, "onShowCompleteEnable") as Array;
        Array disable = Field(manager, "onShowCompleteDisable") as Array;

        if (enable == null || enable.Length == 0)
        {
            Add(Severity.Warning, "finale", "onShowCompleteEnable is empty — solving all six locks changes nothing visible.", manager);
        }

        if (disable == null || disable.Length == 0)
        {
            Add(Severity.Error, "finale", "onShowCompleteDisable is empty, so winning the run opens nothing and the guests stay walled in.", manager);
        }

        if (FinaleFor(manager) != null)
        {
            Add(Severity.Note, "finale",
                "the finale's two arrays above are the fallback for scenes with no CarnivalFinale; CheckFinaleSequence tests the " +
                "sequence that actually runs.", manager);
        }

        ReportNullEntries(enable, "onShowCompleteEnable", "finale", manager);
        ReportNullEntries(disable, "onShowCompleteDisable", "finale", manager);
    }

    // ---------------------------------------------------------------- clock

    private static void CheckTimer(Component timer)
    {
        float length = Float(timer, "showLengthSeconds");

        if (length < 120f)
        {
            Add(Severity.Error, "clock", "showLengthSeconds is " + length + "s — far too short to solve six locks, so nobody reaches the finale.", timer);
        }
        else if (length < 300f)
        {
            Add(Severity.Warning, "clock", "showLengthSeconds is " + length + "s, a 5-minute run. Playable for testing, tight for four strangers in VR.", timer);
        }
        else if (length > 3600f)
        {
            Add(Severity.Warning, "clock", "showLengthSeconds is " + length + "s; a run longer than an hour rarely ends with anyone still in the instance.", timer);
        }

        if (Bool(timer, "autoStart"))
        {
            Add(Severity.Warning, "clock",
                "autoStart is on, so the clock starts when the instance loads. A group arriving second inherits a partly spent run; " +
                "the gate line is what should start it.", timer);
        }

        Array timeUp = Field(timer, "onTimeUpEnable") as Array;

        if (timeUp == null || timeUp.Length == 0)
        {
            Add(Severity.Note, "clock", "onTimeUpEnable is empty — nothing announces that the 25 minutes are gone.", timer);
        }

        Array finalMinute = Field(timer, "onFinalMinuteEnable") as Array;

        if (finalMinute == null || finalMinute.Length == 0)
        {
            Add(Severity.Note, "clock", "onFinalMinuteEnable is empty, so the last minute has no warning.", timer);
        }
    }

    // ---------------------------------------------------------------- roles

    private static void CheckRoleBoard(Component roles)
    {
        Array markers = Field(roles, "roleMarkers") as Array;

        if (markers == null || markers.Length != RoleCount)
        {
            Add(Severity.Error, "roles",
                "roleMarkers has " + (markers == null ? 0 : markers.Length) + " entries; it needs exactly " + RoleCount +
                " in role order (Ringmaster, Contortionist, Bearded Lady, Cannonball).", roles);
        }
        else
        {
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers.GetValue(i) == null)
                {
                    Add(Severity.Error, "roles", "roleMarkers[" + i + "] is empty, so that role never shows as claimed.", roles);
                }
            }
        }

        Array claims = Field(roles, "claimByRole") as Array;

        if (claims == null || claims.Length != RoleCount)
        {
            Add(Severity.Warning, "roles",
                "claimByRole has " + (claims == null ? 0 : claims.Length) + " slots; the board expects " + RoleCount +
                ". It resets itself at Start, but clients can disagree before the first sync.", roles);
        }
    }

    // --------------------------------------------------------------- scares

    private static void CheckSurprises(Component surprises)
    {
        Array beats = Field(surprises, "scriptedBeats") as Array;

        if (beats == null || beats.Length == 0)
        {
            Add(Severity.Error, "scares", "scriptedBeats is empty, so intensity 2 behaves exactly like intensity 1.", surprises);
        }
        else if (beats.Length != CarnivalRigWiring.BeatAnchors.Length)
        {
            Add(Severity.Warning, "scares",
                "scriptedBeats has " + beats.Length + " entries but the greybox has " + CarnivalRigWiring.BeatAnchors.Length + " beat anchors.", surprises);
        }

        float cooldown = Float(surprises, "cooldownSeconds");

        if (cooldown <= 0f)
        {
            Add(Severity.Error, "scares", "cooldownSeconds is " + cooldown + "; every scare is allowed immediately, which is the jumpscare machine this component exists to prevent.", surprises);
        }
        else if (cooldown < 30f)
        {
            Add(Severity.Warning, "scares", "cooldownSeconds is " + cooldown + "s; the deck calls for 90.", surprises);
        }

        int perRun = Int(surprises, "scaresPerPlayer");

        if (perRun < 1)
        {
            Add(Severity.Error, "scares", "scaresPerPlayer is " + perRun + ", so no scare can ever fire.", surprises);
        }
        else if (perRun > 6)
        {
            Add(Severity.Warning, "scares", "scaresPerPlayer is " + perRun + "; the deck caps a run at six per guest.", surprises);
        }

        int intensity = Int(surprises, "intensity");

        if (intensity < 0 || intensity > 2)
        {
            Add(Severity.Warning, "scares", "intensity is " + intensity + "; expected 0, 1 or 2.", surprises);
        }
    }

    // ------------------------------------------------------------- curtain

    private static void CheckCurtain(GameObject root)
    {
        Type curtainType = CarnivalRigWiring.FindType("CarnivalCurtainLock");

        if (curtainType == null) return;

        GameObject anchor = CarnivalRigWiring.FindDeep(root, CarnivalRigWiring.LockAnchors[5]);
        Component curtain = anchor == null ? null : anchor.GetComponent(curtainType);

        if (curtain == null) curtain = FindAny(curtainType);

        if (curtain == null)
        {
            Add(Severity.Error, "curtain",
                "CarnivalCurtainLock exists but no instance is in the scene, so lock 6 has no puzzle. " +
                "Run Carnival World > 4. Wire Show Rig (playable).", root);

            return;
        }

        Component lockRef = Field(curtain, "lockRef") as Component;

        if (lockRef == null)
        {
            Add(Severity.Error, "curtain",
                "lockRef is empty — holding all four marks would never reach the manager, so lock 6 can never fall.", curtain);
        }
        else if (Int(lockRef, "lockId") != 6)
        {
            Add(Severity.Warning, "curtain",
                "lockRef points at lockId " + Int(lockRef, "lockId") + "; the curtain is lock 6 on the puzzle sheet.", curtain);
        }

        if (Field(curtain, "roleBoard") == null)
        {
            Add(Severity.Warning, "curtain",
                "no roleBoard, so the Contortionist's eight-second hold is disabled and a three-player group cannot finish this lock.", curtain);
        }

        if (Float(curtain, "debounceSeconds") <= 0f)
        {
            Add(Severity.Warning, "curtain",
                "debounceSeconds is 0, so a guest walking across the ring could solve the lock by accident.", curtain);
        }

        bool soloHold = Bool(curtain, "contortionistSoloHold") && Float(curtain, "soloHoldSeconds") > 0f;

        if (Bool(curtain, "contortionistSoloHold") && Float(curtain, "soloHoldSeconds") <= 0f)
        {
            Add(Severity.Warning, "curtain",
                "contortionistSoloHold is on but soloHoldSeconds is " + Float(curtain, "soloHoldSeconds") + ", so the hold does nothing.", curtain);
        }

        if (!soloHold)
        {
            Add(Severity.Note, "curtain",
                "the solo hold is off: the curtain needs four guests at once, which the deck rules out for a group of three.", curtain);
        }

        Array marks = Field(curtain, "marks") as Array;

        if (marks == null || marks.Length != 4)
        {
            Add(Severity.Error, "curtain",
                "marks has " + (marks == null ? 0 : marks.Length) + " entries; the ring needs exactly four.", curtain);

            return;
        }

        HashSet<int> seen = new HashSet<int>();

        for (int i = 0; i < marks.Length; i++)
        {
            Component mark = marks.GetValue(i) as Component;

            if (mark == null)
            {
                Add(Severity.Error, "curtain", "marks[" + i + "] is empty, so one of the four pads does nothing.", curtain);
                continue;
            }

            int index = Int(mark, "markIndex");

            if (index < 0 || index > 3)
            {
                Add(Severity.Error, "curtain", mark.name + " has markIndex " + index + ", outside 0–3.", mark);
            }
            else if (!seen.Add(index))
            {
                Add(Severity.Error, "curtain", "two marks claim markIndex " + index + "; the second pad can never light.", mark);
            }
            else if (index != i)
            {
                Add(Severity.Warning, "curtain",
                    "marks[" + i + "] is " + mark.name + " with markIndex " + index + "; the array order no longer matches the pads.", mark);
            }

            Component backlink = Field(mark, "curtain") as Component;

            if (backlink == null)
            {
                Add(Severity.Error, "curtain", mark.name + " has no curtain assigned, so standing on it does nothing.", mark);
            }
            else if (backlink != curtain)
            {
                Add(Severity.Error, "curtain", mark.name + " reports to a different curtain than the one wired to lock 6.", mark);
            }

            Collider volume = mark.GetComponent<Collider>();

            if (volume == null || !volume.isTrigger)
            {
                Add(Severity.Error, "curtain", mark.name + " has no trigger collider, so a guest cannot stand on it.", mark);
            }
        }

        // The centre pad is the understudy ending's only input. It is not one of
        // the four marks, so a missing or mislabelled pad leaves the ring looking
        // complete while one of the three endings can never happen.
        GameObject centre = CarnivalRigWiring.FindDeep(root, "CURTAIN_CENTRE_MARK");
        Type markType = CarnivalRigWiring.FindType("CarnivalCurtainMark");

        if (centre == null)
        {
            Add(Severity.Warning, "curtain",
                "no CURTAIN_CENTRE_MARK under the ring, so the understudy ending can never fire — the finale will always read zero guests in the middle.", curtain);
        }
        else if (markType != null)
        {
            Component centreMark = centre.GetComponent(markType);

            if (centreMark == null)
            {
                Add(Severity.Error, "curtain",
                    centre.name + " has no CarnivalCurtainMark, so standing on it is never counted.", centre);
            }
            else
            {
                if (!Bool(centreMark, "centreMark"))
                {
                    Add(Severity.Error, "curtain",
                        centre.name + " is not flagged as the centre pad, so it counts as a fifth mark and the understudy ending never sees a guest.", centreMark);
                }

                if (Field(centreMark, "curtain") != curtain)
                {
                    Add(Severity.Error, "curtain",
                        centre.name + " does not report to this curtain, so its occupants are counted nowhere.", centreMark);
                }
            }

            Collider centreVolume = centre.GetComponent<Collider>();

            if (centreVolume == null || !centreVolume.isTrigger)
            {
                Add(Severity.Error, "curtain",
                    centre.name + " has no trigger collider, so a guest can stand in the middle of the ring and never be counted.", centre);
            }
        }
    }

    // ----------------------------------------------------------- balloons

    private static void CheckBalloons(GameObject root)
    {
        Type balloonLockType = CarnivalRigWiring.FindType("CarnivalBalloonLock");

        if (balloonLockType == null) return;

        GameObject anchor = CarnivalRigWiring.FindDeep(root, CarnivalRigWiring.LockAnchors[3]);
        Component balloons = anchor == null ? null : anchor.GetComponent(balloonLockType);

        if (balloons == null) balloons = FindAny(balloonLockType);

        if (balloons == null)
        {
            Add(Severity.Error, "balloons",
                "CarnivalBalloonLock exists but no instance is in the scene, so lock 4 has no puzzle. " +
                "Run Carnival World > 4. Wire Show Rig (playable).", root);

            return;
        }

        Component lockRef = Field(balloons, "lockRef") as Component;

        if (lockRef == null)
        {
            Add(Severity.Error, "balloons",
                "lockRef is empty — three matching balloons would never reach the manager, so lock 4 can never fall.", balloons);
        }
        else if (Int(lockRef, "lockId") != 4)
        {
            Add(Severity.Warning, "balloons",
                "lockRef points at lockId " + Int(lockRef, "lockId") + "; the funhouse is lock 4 on the puzzle sheet.", balloons);
        }

        Array targets = Field(balloons, "targets") as Array;

        if (targets == null || targets.Length != 3)
        {
            Add(Severity.Error, "balloons",
                "targets has " + (targets == null ? 0 : targets.Length) + " colour(s); one per balloon, so three.", balloons);
        }
        else
        {
            for (int i = 0; i < targets.Length; i++)
            {
                object value = targets.GetValue(i);
                int colour = value is int ? (int)value : -1;

                if (colour < 0 || colour > 4)
                {
                    Add(Severity.Error, "balloons",
                        "targets[" + i + "] is " + colour + "; a balloon only snaps to five colours (0–4), so that answer is unreachable.", balloons);
                }
            }
        }

        Array list = Field(balloons, "balloons") as Array;

        if (list == null || list.Length != 3)
        {
            Add(Severity.Error, "balloons",
                "balloons has " + (list == null ? 0 : list.Length) + " entries; the joke is three animals.", balloons);

            return;
        }

        HashSet<int> seen = new HashSet<int>();

        for (int i = 0; i < list.Length; i++)
        {
            Component balloon = list.GetValue(i) as Component;

            if (balloon == null)
            {
                Add(Severity.Error, "balloons", "balloons[" + i + "] is empty, so one colour of the joke is unwinnable.", balloons);
                continue;
            }

            int index = Int(balloon, "balloonIndex");

            if (index < 0 || index > 2)
            {
                Add(Severity.Error, "balloons", balloon.name + " has balloonIndex " + index + ", outside 0–2.", balloon);
            }
            else if (!seen.Add(index))
            {
                Add(Severity.Error, "balloons", "two balloons claim balloonIndex " + index + "; one colour can never be set.", balloon);
            }
            else if (index != i)
            {
                Add(Severity.Warning, "balloons",
                    "balloons[" + i + "] is " + balloon.name + " with balloonIndex " + index + "; the array order no longer matches the answer.", balloon);
            }

            Component backlink = Field(balloon, "balloonLock") as Component;

            if (backlink == null)
            {
                Add(Severity.Error, "balloons", balloon.name + " has no balloonLock assigned, so releasing it tells nobody.", balloon);
            }
            else if (backlink != balloons)
            {
                Add(Severity.Error, "balloons", balloon.name + " reports to a different lock than the one wired to lock 4.", balloon);
            }

            Collider reach = balloon.GetComponent<Collider>();

            if (reach == null)
            {
                Add(Severity.Error, "balloons", balloon.name + " has no collider, so it can be neither grabbed nor clicked.", balloon);
            }

            Array swatches = Field(balloon, "colourSwatches") as Array;

            if (swatches == null || swatches.Length != 5)
            {
                Add(Severity.Warning, "balloons",
                    balloon.name + " has " + (swatches == null ? 0 : swatches.Length) + " swatch(es); it needs five or the colour is unreadable.", balloon);
            }

            if (Field(balloon, "skin") == null)
            {
                Add(Severity.Note, "balloons", balloon.name + " has no skin transform, so the fill is invisible.", balloon);
            }
        }

        Array reveal = Field(balloons, "onRevealEnable") as Array;

        if (reveal == null || reveal.Length == 0)
        {
            Add(Severity.Warning, "balloons",
                "onRevealEnable is empty — the balloons pop and the funhouse stays exactly as it was.", balloons);
        }
    }

    // ------------------------------------------------------------- weight

    private static void CheckScale(GameObject root)
    {
        Type scaleType = CarnivalRigWiring.FindType("CarnivalScaleLock");

        if (scaleType == null) return;

        // The scale lives on WEIGHT_SCALE under the lock anchor, not on the
        // anchor itself, because Interact needs its collider on that object.
        GameObject anchor = CarnivalRigWiring.FindDeep(root, CarnivalRigWiring.LockAnchors[1]);
        Component scale = anchor == null ? null : anchor.GetComponentInChildren(scaleType, true);

        if (scale == null) scale = FindAny(scaleType);

        if (scale == null)
        {
            Add(Severity.Error, "weight",
                "CarnivalScaleLock exists but no instance is in the scene, so lock 2 has no puzzle. " +
                "Run Carnival World > 4. Wire Show Rig (playable).", root);

            return;
        }

        Component lockRef = Field(scale, "lockRef") as Component;

        if (lockRef == null)
        {
            Add(Severity.Error, "weight",
                "lockRef is empty — a level beam would never reach the manager, so lock 2 can never fall.", scale);
        }
        else if (Int(lockRef, "lockId") != 2)
        {
            Add(Severity.Warning, "weight",
                "lockRef points at lockId " + Int(lockRef, "lockId") + "; the sawdust ring is lock 2 on the puzzle sheet.", scale);
        }

        Collider reach = scale.GetComponent<Collider>();

        if (reach == null)
        {
            Add(Severity.Error, "weight",
                "the scale has no collider, so Interact() never fires and the counterweight cannot be moved — the beam is stuck off balance.", scale);
        }
        else if (reach.isTrigger)
        {
            Add(Severity.Error, "weight",
                "the scale's collider is a trigger; VRChat cannot interact with a trigger, so the counterweight is unreachable.", scale);
        }

        Transform beam = Field(scale, "beam") as Transform;

        if (beam == null)
        {
            Add(Severity.Warning, "weight",
                "no beam transform, so a guest gets no tilt to read and the balance is guesswork.", scale);
        }

        Transform counterweight = Field(scale, "counterweight") as Transform;
        Transform counterweightHome = Field(scale, "counterweightHome") as Transform;

        if (counterweight == null)
        {
            Add(Severity.Warning, "weight",
                "no counterweight transform, so the step is stored but nothing on the scale visibly moves.", scale);
        }
        else if (counterweightHome == null)
        {
            Add(Severity.Warning, "weight",
                "no counterweightHome marker, so a reset uses the scripted rest position rather than the prop's own — it drifts out of step the first time the prop is moved in the scene.", scale);
        }

        object rawStart = Field(scale, "counterweightStartMass");
        object rawStep = Field(scale, "counterweightStepMass");
        object rawTolerance = Field(scale, "tolerance");
        object rawSettle = Field(scale, "settleSeconds");

        float startMass = rawStart is float ? (float)rawStart : 0f;
        float stepMass = rawStep is float ? (float)rawStep : 0f;
        float tolerance = rawTolerance is float ? (float)rawTolerance : -1f;
        float settle = rawSettle is float ? (float)rawSettle : 0f;
        int steps = Int(scale, "counterweightSteps");

        if (stepMass <= 0f || steps < 2)
        {
            Add(Severity.Error, "weight",
                "counterweightStepMass is " + stepMass + " and counterweightSteps is " + steps +
                "; the beam needs at least two steps of positive mass before anything can balance.", scale);
        }

        if (tolerance < 0f)
        {
            Add(Severity.Error, "weight", "tolerance is negative, so no beam can ever read as level.", scale);
        }

        if (settle <= 0f)
        {
            Add(Severity.Warning, "weight",
                "settleSeconds is 0, so a single click that passes over the balance point counts instantly — no settle, no read.", scale);
        }

        Array sockets = Field(scale, "sockets") as Array;

        if (sockets == null || sockets.Length != 3)
        {
            Add(Severity.Error, "weight",
                "sockets has " + (sockets == null ? 0 : sockets.Length) + " marker(s); three barrels need three sockets on the pan.", scale);
        }
        else
        {
            for (int i = 0; i < sockets.Length; i++)
            {
                Transform socket = sockets.GetValue(i) as Transform;

                if (socket == null)
                {
                    Add(Severity.Error, "weight",
                        "sockets[" + i + "] is empty, so a barrel set down there has nowhere to sit.", scale);
                }
                else if (beam != null && !socket.IsChildOf(beam))
                {
                    Add(Severity.Warning, "weight",
                        socket.name + " is not a child of the beam, so it will not tilt with the pan and a seated barrel will not follow it.", socket);
                }
            }
        }

        CheckCounterweightHome(beam, counterweightHome, sockets);

        Array barrels = Field(scale, "barrels") as Array;

        if (barrels == null || barrels.Length != 3)
        {
            Add(Severity.Error, "weight",
                "barrels has " + (barrels == null ? 0 : barrels.Length) + " entries; the ring has three, and three is what the check requires.", scale);

            return;
        }

        HashSet<int> seen = new HashSet<int>();
        float panMass = 0f;
        bool massKnown = true;

        for (int i = 0; i < barrels.Length; i++)
        {
            Component barrel = barrels.GetValue(i) as Component;

            if (barrel == null)
            {
                Add(Severity.Error, "weight", "barrels[" + i + "] is empty, so the pan can never reach three.", scale);
                massKnown = false;
                continue;
            }

            int index = Int(barrel, "barrelIndex");

            if (index < 0 || index > 2)
            {
                Add(Severity.Error, "weight", barrel.name + " has barrelIndex " + index + ", outside 0–2.", barrel);
            }
            else if (!seen.Add(index))
            {
                Add(Severity.Error, "weight", "two barrels claim barrelIndex " + index + "; one of them can never seat right.", barrel);
            }
            else if (index != i)
            {
                Add(Severity.Warning, "weight",
                    "barrels[" + i + "] is " + barrel.name + " with barrelIndex " + index + "; the array order no longer matches the ring.", barrel);
            }

            Component backlink = Field(barrel, "scale") as Component;

            if (backlink == null)
            {
                Add(Severity.Error, "weight", barrel.name + " has no scale assigned, so lifting it tells the beam nothing.", barrel);
            }
            else if (backlink != scale)
            {
                Add(Severity.Error, "weight", barrel.name + " reports to a different scale than the one wired to lock 2.", barrel);
            }

            Collider grab = barrel.GetComponent<Collider>();

            if (grab == null)
            {
                Add(Severity.Error, "weight", barrel.name + " has no collider, so no guest can take hold of it.", barrel);
            }
            else if (grab.isTrigger)
            {
                Add(Severity.Error, "weight", barrel.name + "'s collider is a trigger; VRChat cannot interact with a trigger.", barrel);
            }

            Transform home = Field(barrel, "home") as Transform;

            if (home == null)
            {
                Add(Severity.Error, "weight",
                    barrel.name + " has no home marker, so a reset frees it and leaves it standing wherever the last group put it — possibly still on the pan.", barrel);
            }
            else
            {
                float snap = Float(barrel, "snapRadius");

                if (sockets != null && snap > 0f)
                {
                    for (int s = 0; s < sockets.Length; s++)
                    {
                        Transform socket = sockets.GetValue(s) as Transform;

                        if (socket == null) continue;

                        float distance = Vector3.Distance(home.position, socket.position);

                        if (distance <= snap)
                        {
                            Add(Severity.Error, "weight",
                                barrel.name + "'s home is " + distance.ToString("0.00") + " m from " + socket.name +
                                ", inside its " + snap + " m snap radius: the first set-down after a reset seats it, and the pan starts the run already loaded.",
                                barrel);

                            break;
                        }
                    }
                }

                for (int j = 0; j < i; j++)
                {
                    Component other = barrels.GetValue(j) as Component;
                    Transform otherHome = other == null ? null : FieldQuiet(other, "home") as Transform;

                    if (otherHome == null) continue;

                    if (Vector3.Distance(otherHome.position, home.position) < 0.6f)
                    {
                        Add(Severity.Warning, "weight",
                            barrel.name + " and " + other.name + " share a home within 0.6 m; the two barrels overlap on that mark and the inner one cannot be reached.", barrel);

                        break;
                    }
                }
            }

            object rawMass = Field(barrel, "mass");
            float mass = rawMass is float ? (float)rawMass : 0f;

            if (mass <= 0f)
            {
                Add(Severity.Error, "weight", barrel.name + " has mass " + mass + "; a barrel that weighs nothing cannot tip a beam.", barrel);
                massKnown = false;
            }
            else
            {
                panMass += mass;
            }
        }

        // This lock's version of an unreachable trigger: is there any
        // counterweight setting that lands inside the tolerance at all?
        if (massKnown && stepMass > 0f && steps >= 2 && tolerance >= 0f)
        {
            int hits = 0;
            int first = -1;

            for (int step = 0; step < steps; step++)
            {
                float counterMass = startMass + step * stepMass;

                if (Mathf.Abs(panMass - counterMass) <= tolerance)
                {
                    hits++;

                    if (first < 0) first = step;
                }
            }

            if (hits == 0)
            {
                Add(Severity.Error, "weight",
                    "the pan weighs " + panMass + " kg and the counterweight runs " + startMass + " to " +
                    (startMass + (steps - 1) * stepMass) + " kg; no step lands inside the " + tolerance +
                    " kg tolerance, so the beam can never read as level and the lock is unwinnable.", scale);
            }
            else if (hits > 1)
            {
                Add(Severity.Note, "weight",
                    hits + " counterweight steps read as level at " + panMass + " kg against a " + tolerance +
                    " kg tolerance, so the beam settles at more than one setting.", scale);
            }
            else
            {
                Add(Severity.Note, "weight",
                    "balance check: " + panMass + " kg on the pan, step " + first + " lands level within " +
                    tolerance + " kg.", scale);
            }
        }

        Array reveal = Field(scale, "onRevealEnable") as Array;

        if (reveal == null || reveal.Length == 0)
        {
            Add(Severity.Warning, "weight",
                "onRevealEnable is empty — the beam settles and the ring stays exactly as it was.", scale);
        }
    }

    // ------------------------------------------------------------ mirrors

    private static void CheckMirrors(GameObject root)
    {
        Type mirrorType = CarnivalRigWiring.FindType("CarnivalMirrorLock");

        if (mirrorType == null) return;

        GameObject anchor = CarnivalRigWiring.FindDeep(root, CarnivalRigWiring.LockAnchors[2]);
        Component mirrors = anchor == null ? null : anchor.GetComponent(mirrorType);

        if (mirrors == null) mirrors = FindAny(mirrorType);

        if (mirrors == null)
        {
            Add(Severity.Error, "mirrors",
                "CarnivalMirrorLock exists but no instance is in the scene, so lock 3 has no puzzle. " +
                "Run Carnival World > 4. Wire Show Rig (playable).", root);

            return;
        }

        Component lockRef = Field(mirrors, "lockRef") as Component;

        if (lockRef == null)
        {
            Add(Severity.Error, "mirrors",
                "lockRef is empty — aligning all four frames would never reach the manager, so lock 3 can never fall.", mirrors);
        }
        else if (Int(lockRef, "lockId") != 3)
        {
            Add(Severity.Warning, "mirrors",
                "lockRef points at lockId " + Int(lockRef, "lockId") + "; the hall of mirrors is lock 3 on the puzzle sheet.", mirrors);
        }

        Array solution = Field(mirrors, "solution") as Array;

        if (solution == null || solution.Length != 4)
        {
            Add(Severity.Error, "mirrors",
                "solution has " + (solution == null ? 0 : solution.Length) + " angle(s); one per frame, so four.", mirrors);
        }
        else
        {
            for (int i = 0; i < solution.Length; i++)
            {
                object value = solution.GetValue(i);
                int angle = value is int ? (int)value : -1;

                if (angle < 0 || angle > 3)
                {
                    Add(Severity.Error, "mirrors",
                        "solution[" + i + "] is " + angle + "; a frame only has four quarter turns (0–3), so that angle is unreachable.", mirrors);
                }
            }
        }

        Array frames = Field(mirrors, "frames") as Array;

        if (frames == null || frames.Length != 4)
        {
            Add(Severity.Error, "mirrors",
                "frames has " + (frames == null ? 0 : frames.Length) + " entries; the hall needs exactly four.", mirrors);

            return;
        }

        HashSet<int> seen = new HashSet<int>();

        for (int i = 0; i < frames.Length; i++)
        {
            Component frame = frames.GetValue(i) as Component;

            if (frame == null)
            {
                Add(Severity.Error, "mirrors", "frames[" + i + "] is empty, so that mirror can never be turned.", mirrors);
                continue;
            }

            int index = Int(frame, "frameIndex");

            if (index < 0 || index > 3)
            {
                Add(Severity.Error, "mirrors", frame.name + " has frameIndex " + index + ", outside 0–3.", frame);
            }
            else if (!seen.Add(index))
            {
                Add(Severity.Error, "mirrors",
                    "two frames claim frameIndex " + index + "; turning one would turn both beam checks.", frame);
            }
            else if (index != i)
            {
                Add(Severity.Warning, "mirrors",
                    "frames[" + i + "] is " + frame.name + " with frameIndex " + index + "; the array order no longer matches the wall.", frame);
            }

            Component backlink = Field(frame, "mirrorLock") as Component;

            if (backlink == null)
            {
                Add(Severity.Error, "mirrors", frame.name + " has no mirrorLock assigned, so turning it does nothing.", frame);
            }
            else if (backlink != mirrors)
            {
                Add(Severity.Error, "mirrors", frame.name + " reports to a different lock than the one wired to lock 3.", frame);
            }

            Collider reach = frame.GetComponent<Collider>();

            if (reach == null)
            {
                Add(Severity.Error, "mirrors", frame.name + " has no collider, so Interact() never fires and the frame cannot be turned.", frame);
            }
            else if (reach.isTrigger)
            {
                Add(Severity.Error, "mirrors", frame.name + "'s collider is a trigger; VRChat cannot interact with a trigger.", frame);
            }

            Array beams = Field(frame, "beamWhenAligned") as Array;

            if (beams == null || beams.Length == 0)
            {
                Add(Severity.Note, "mirrors",
                    frame.name + " has no beam, so a correct angle is invisible until all four are right.", frame);
            }
        }

        Array reveal = Field(mirrors, "onRevealEnable") as Array;

        if (reveal == null || reveal.Length == 0)
        {
            Add(Severity.Warning, "mirrors",
                "onRevealEnable is empty — the beam arrives and nothing in the hall changes.", mirrors);
        }
    }

    // ------------------------------------------------------------ tickets

    private static void CheckTickets(GameObject root)
    {
        Type ticketType = CarnivalRigWiring.FindType("CarnivalTicketLock");

        if (ticketType == null) return;

        GameObject anchor = CarnivalRigWiring.FindDeep(root, CarnivalRigWiring.LockAnchors[0]);
        Component tickets = anchor == null ? null : anchor.GetComponent(ticketType);

        if (tickets == null) tickets = FindAny(ticketType);

        if (tickets == null)
        {
            Add(Severity.Error, "tickets",
                "CarnivalTicketLock exists but no instance is in the scene, so lock 1 has no puzzle. " +
                "Run Carnival World > 4. Wire Show Rig (playable).", root);

            return;
        }

        Component lockRef = Field(tickets, "lockRef") as Component;

        if (lockRef == null)
        {
            Add(Severity.Error, "tickets",
                "lockRef is empty — the fourth stub would never reach the manager, so lock 1 can never fall.", tickets);
        }
        else if (Int(lockRef, "lockId") != 1)
        {
            Add(Severity.Warning, "tickets",
                "lockRef points at lockId " + Int(lockRef, "lockId") + "; the ticket booth is lock 1 on the puzzle sheet.", tickets);
        }

        Array stubs = Field(tickets, "stubs") as Array;

        if (stubs == null || stubs.Length != 4)
        {
            Add(Severity.Error, "tickets",
                "stubs has " + (stubs == null ? 0 : stubs.Length) + " entries; the booth needs exactly four.", tickets);

            return;
        }

        HashSet<int> seen = new HashSet<int>();

        for (int i = 0; i < stubs.Length; i++)
        {
            Component stub = stubs.GetValue(i) as Component;

            if (stub == null)
            {
                Add(Severity.Error, "tickets", "stubs[" + i + "] is empty, so that ticket cannot be handed in.", tickets);
                continue;
            }

            int id = Int(stub, "stubId");

            if (id < 1 || id > 4)
            {
                Add(Severity.Error, "tickets", stub.name + " has stubId " + id + ", outside 1–4.", stub);
            }
            else if (!seen.Add(id))
            {
                Add(Severity.Error, "tickets",
                    "two stubs share stubId " + id + "; the booth counts numbers, so the run could never reach four.", stub);
            }

            Component backlink = Field(stub, "ticketLock") as Component;

            if (backlink == null)
            {
                Add(Severity.Error, "tickets", stub.name + " has no ticketLock assigned, so handing it over does nothing.", stub);
            }
            else if (backlink != tickets)
            {
                Add(Severity.Error, "tickets", stub.name + " reports to a different booth than the one wired to lock 1.", stub);
            }

            Transform mouth = Field(stub, "mouth") as Transform;

            if (mouth == null)
            {
                Add(Severity.Error, "tickets",
                    stub.name + " has no mouth assigned, so it refuses every drop and can never be handed in.", stub);
            }

            Transform home = Field(stub, "home") as Transform;

            if (home == null)
            {
                Add(Severity.Error, "tickets",
                    stub.name + " has no home marker, so a reset leaves it wherever the last party dropped it.", stub);
            }
            else
            {
                if (mouth != null)
                {
                    object rawExtents = FieldQuiet(stub, "mouthHalfExtents");
                    Vector3 extents = rawExtents is Vector3 ? (Vector3)rawExtents : new Vector3(0.45f, 0.35f, 0.5f);
                    Vector3 local = mouth.InverseTransformPoint(home.position);

                    if (Mathf.Abs(local.x) <= extents.x &&
                        Mathf.Abs(local.y) <= extents.y &&
                        Mathf.Abs(local.z) <= extents.z)
                    {
                        Add(Severity.Warning, "tickets",
                            stub.name + "'s home is inside the booth mouth, so a reset parks it where a drop counts — the first guest to pick it up and let go hands it in for free.", stub);
                    }
                }

                for (int j = 0; j < i; j++)
                {
                    Component other = stubs.GetValue(j) as Component;
                    Transform otherHome = other == null ? null : FieldQuiet(other, "home") as Transform;

                    if (otherHome == null) continue;

                    if (Vector3.Distance(otherHome.position, home.position) < 0.25f)
                    {
                        Add(Severity.Warning, "tickets",
                            stub.name + " and " + other.name + " share a home within 0.25 m; two stubs on one mark read as a single stub, and the hidden one is easy to lose.", stub);

                        break;
                    }
                }
            }

            if (stub.GetComponent("VRC_Pickup") == null)
            {
                Add(Severity.Warning, "tickets",
                    stub.name + " has no VRC_Pickup, so guests cannot carry it to the booth; Interact still hands it over.", stub);
            }
        }

        Array lamps = Field(tickets, "stubLights") as Array;

        if (lamps == null || lamps.Length != 4)
        {
            Add(Severity.Note, "tickets",
                "stubLights has " + (lamps == null ? 0 : lamps.Length) + " entries; without four lamps the booth never shows how many stubs it has.", tickets);
        }

        Array reveal = Field(tickets, "onRevealEnable") as Array;

        if (reveal == null || reveal.Length == 0)
        {
            Add(Severity.Warning, "tickets",
                "onRevealEnable is empty — the fourth stub unlocks the lock but shows the group nothing.", tickets);
        }
    }

    // ------------------------------------------------------------- boiler

    private static void CheckBoiler(GameObject root)
    {
        Type boilerType = CarnivalRigWiring.FindType("CarnivalBoilerLock");

        if (boilerType == null) return;

        GameObject anchor = CarnivalRigWiring.FindDeep(root, CarnivalRigWiring.LockAnchors[4]);
        Component boiler = anchor == null ? null : anchor.GetComponent(boilerType);

        if (boiler == null) boiler = FindAny(boilerType);

        if (boiler == null)
        {
            Add(Severity.Error, "boiler",
                "CarnivalBoilerLock exists but no instance is in the scene, so lock 5 has no puzzle. " +
                "Run Carnival World > 4. Wire Show Rig (playable).", root);

            return;
        }

        Component lockRef = Field(boiler, "lockRef") as Component;

        if (lockRef == null)
        {
            Add(Severity.Error, "boiler",
                "lockRef is empty — playing the phrase would never reach the manager, so lock 5 can never fall.", boiler);
        }
        else if (Int(lockRef, "lockId") != 5)
        {
            Add(Severity.Warning, "boiler",
                "lockRef points at lockId " + Int(lockRef, "lockId") + "; the boiler is lock 5 on the puzzle sheet.", boiler);
        }

        if (Field(boiler, "surprises") == null)
        {
            Add(Severity.Warning, "boiler",
                "no SurpriseDirector, so a wrong note hisses but never scares — the reactive beat is silently missing.", boiler);
        }

        Array melody = Field(boiler, "melody") as Array;

        if (melody == null || melody.Length != 4)
        {
            Add(Severity.Error, "boiler",
                "melody has " + (melody == null ? 0 : melody.Length) + " note(s); the phrase is four notes, one per valve.", boiler);
        }
        else
        {
            for (int i = 0; i < melody.Length; i++)
            {
                object value = melody.GetValue(i);
                int note = value is int ? (int)value : -1;

                if (note < 0 || note > 3)
                {
                    Add(Severity.Error, "boiler",
                        "melody[" + i + "] is " + note + "; a valve only has four positions (0–3), so that note can never be played.", boiler);
                }
            }
        }

        Array valves = Field(boiler, "valves") as Array;

        if (valves == null || valves.Length != 4)
        {
            Add(Severity.Error, "boiler",
                "valves has " + (valves == null ? 0 : valves.Length) + " entries; the boiler needs exactly four.", boiler);

            return;
        }

        HashSet<int> seen = new HashSet<int>();

        for (int i = 0; i < valves.Length; i++)
        {
            Component valve = valves.GetValue(i) as Component;

            if (valve == null)
            {
                Add(Severity.Error, "boiler", "valves[" + i + "] is empty, so one dial is missing from the phrase.", boiler);
                continue;
            }

            int index = Int(valve, "valveIndex");

            if (index < 0 || index > 3)
            {
                Add(Severity.Error, "boiler", valve.name + " has valveIndex " + index + ", outside 0–3.", valve);
            }
            else if (!seen.Add(index))
            {
                Add(Severity.Error, "boiler", "two valves claim valveIndex " + index + "; turning one would read the other's note.", valve);
            }
            else if (index != i)
            {
                Add(Severity.Warning, "boiler",
                    "valves[" + i + "] is " + valve.name + " with valveIndex " + index + "; the array order no longer matches the dials.", valve);
            }

            Component backlink = Field(valve, "boiler") as Component;

            if (backlink == null)
            {
                Add(Severity.Error, "boiler", valve.name + " has no boiler assigned, so turning it does nothing.", valve);
            }
            else if (backlink != boiler)
            {
                Add(Severity.Error, "boiler", valve.name + " reports to a different boiler than the one wired to lock 5.", valve);
            }

            Collider reach = valve.GetComponent<Collider>();

            if (reach == null)
            {
                Add(Severity.Error, "boiler", valve.name + " has no collider, so Interact() never fires and the dial cannot be turned.", valve);
            }
            else if (reach.isTrigger)
            {
                Add(Severity.Error, "boiler", valve.name + "'s collider is a trigger; VRChat cannot interact with a trigger.", valve);
            }
        }
    }

    private static Component FindAny(Type type)
    {
        UnityEngine.Object[] found = UnityEngine.Object.FindObjectsOfType(type, true);
        return found.Length > 0 ? found[0] as Component : null;
    }

    // ------------------------------------------------------------- the gate

    private static void CheckGate(GameObject root, Component manager)
    {
        GameObject gate = CarnivalRigWiring.FindDeep(root, "EXIT_GATE");

        if (gate == null)
        {
            Add(Severity.Error, "gate", "anchor EXIT_GATE is missing, so the run has no visible way out.", root);
            return;
        }

        GameObject barrier = CarnivalRigWiring.FindDeep(gate, "GATE_BARRIER");

        if (barrier == null)
        {
            Add(Severity.Error, "gate", "no GATE_BARRIER under EXIT_GATE — the exit stands open for the whole run, so the 'locked in' premise never happens.", gate);
        }
        else
        {
            Collider collider = barrier.GetComponent<Collider>();

            if (!barrier.activeSelf)
            {
                Add(Severity.Warning, "gate", "GATE_BARRIER starts disabled, so guests can leave whenever they like.", barrier);
            }

            if (collider == null || collider.isTrigger)
            {
                Add(Severity.Error, "gate", "GATE_BARRIER has no solid collider, so it cannot hold anyone in.", barrier);
            }
            else if (!collider.enabled)
            {
                Add(Severity.Error, "gate", "GATE_BARRIER's collider is disabled, so it blocks nobody.", barrier);
            }

            Component finale = FinaleFor(manager);
            Array opener = finale != null
                ? Field(finale, "onGateOpenDisable") as Array
                : (manager != null ? Field(manager, "onShowCompleteDisable") as Array : null);
            string openerName = finale != null ? "the finale's onGateOpenDisable" : "onShowCompleteDisable";

            if (manager != null && !ArrayContains(opener, barrier))
            {
                Add(Severity.Error, "gate",
                    "GATE_BARRIER is not in " + openerName + ", so a finished run still leaves the guests walled in. " +
                    "The finale needs something to open.", barrier);
            }
        }

        GameObject sign = CarnivalRigWiring.FindDeep(gate, "GATE_OPEN_SIGN");

        if (sign == null)
        {
            Add(Severity.Warning, "gate", "no GATE_OPEN_SIGN; opening the exit is invisible unless a guest walks into the doorway.", gate);
        }
        else if (sign.activeSelf)
        {
            Add(Severity.Warning, "gate", "GATE_OPEN_SIGN starts visible, which tells guests the exit matters before they have solved anything.", sign);
        }

        string[] warnSigns = { "SIGN_FINAL_MINUTE", "SIGN_TIME_UP" };

        for (int i = 0; i < warnSigns.Length; i++)
        {
            GameObject warnSign = CarnivalRigWiring.FindDeep(gate, warnSigns[i]);

            if (warnSign != null && warnSign.activeSelf)
            {
                Add(Severity.Warning, "gate", warnSigns[i] + " starts visible; it should only appear when its trigger fires.", warnSign);
            }
        }
    }

    // ------------------------------------------------------- the finale itself

    /// <summary>
    /// The three beats. A finale that fires in one frame looks like a door
    /// unlock, not an ending, and the two ways that happens are a manager that
    /// was never pointed at the finale and a beat list with a hole in it.
    /// </summary>
    private static void CheckFinaleSequence(GameObject root, Component manager)
    {
        Type finaleType = CarnivalRigWiring.FindType("CarnivalFinale");

        if (finaleType == null)
        {
            Add(Severity.Note, "finale",
                "no compiled type named CarnivalFinale, so the ending is a switch: lights, bow and gate in one frame.", manager);

            return;
        }

        Component finale = FindAny(finaleType);

        if (finale == null)
        {
            Add(Severity.Warning, "finale",
                "CarnivalFinale is not in the scene. Solving the last lock still opens the gate through the manager's arrays, " +
                "but the lights, the bow and the gate all happen in the same frame.", root);

            return;
        }

        if (manager != null && FinaleFor(manager) != finale)
        {
            Add(Severity.Error, "finale",
                "CarnivalLockManager.finale is empty or points elsewhere, so the sequence never starts — the manager will only " +
                "toggle the gate and the three beats stay in their idle state.", finale);
        }

        float bow = Float(finale, "bowDelay");
        float gate = Float(finale, "gateDelay");

        if (gate < bow)
        {
            Add(Severity.Error, "finale",
                "gateDelay (" + gate + "s) is shorter than bowDelay (" + bow + "s), so the exit opens before Grinny bows — " +
                "the one beat that sells the ending happens after the group has already turned to leave.", finale);
        }

        if (gate < 1.5f)
        {
            Add(Severity.Warning, "finale",
                "the whole finale runs in " + gate + "s. A guest in a headset needs longer than that to notice the lights went out.", finale);
        }
        else if (gate > 20f)
        {
            Add(Severity.Warning, "finale",
                "the gate opens " + gate + "s after the blackout; a group that has solved everything will assume the run is broken " +
                "and start pushing at a barrier that is about to disappear.", finale);
        }

        Array blackoutOff = Field(finale, "onBlackoutDisable") as Array;

        if (blackoutOff == null || blackoutOff.Length == 0)
        {
            Add(Severity.Warning, "finale", "onBlackoutDisable is empty — the lights never die, so beat one of three is missing.", finale);
        }

        Array bowOn = Field(finale, "onBowEnable") as Array;

        if (bowOn == null || bowOn.Length == 0)
        {
            Add(Severity.Warning, "finale", "onBowEnable is empty — the lights go out and nothing appears, so the ending has no Grinny in it.", finale);
        }

        Array gateOn = Field(finale, "onGateOpenEnable") as Array;
        Array gateOff = Field(finale, "onGateOpenDisable") as Array;

        if (gateOn == null || gateOn.Length == 0)
        {
            Add(Severity.Warning, "finale",
                "onGateOpenEnable is empty — the barrier comes down with no sign to mark it, so the exit opens in the dark.", finale);
        }

        if (gateOff == null || gateOff.Length == 0)
        {
            Add(Severity.Error, "finale",
                "onGateOpenDisable is empty, so the last beat opens a sign and leaves the wall standing.", finale);
        }

        GameObject gate = CarnivalRigWiring.FindDeep(root, "EXIT_GATE");
        GameObject barrier = gate == null ? null : CarnivalRigWiring.FindDeep(gate, "GATE_BARRIER");
        GameObject sign = gate == null ? null : CarnivalRigWiring.FindDeep(gate, "GATE_OPEN_SIGN");

        if (barrier != null && !ArrayContains(gateOff, barrier))
        {
            Add(Severity.Error, "finale",
                "GATE_BARRIER is not in the finale's onGateOpenDisable, so the ending plays and the guests stay locked in.", barrier);
        }

        if (sign != null && !ArrayContains(gateOn, sign))
        {
            Add(Severity.Warning, "finale",
                "GATE_OPEN_SIGN is not in onGateOpenEnable, so nothing lights up when the exit opens.", sign);
        }

        if (sign != null && ArrayContains(gateOff, sign))
        {
            Add(Severity.Error, "finale",
                "GATE_OPEN_SIGN is in onGateOpenDisable, so the finale hides the very sign it is supposed to light.", sign);
        }

        int stage = Int(finale, "stage");

        if (stage != 0)
        {
            Add(Severity.Warning, "finale",
                "the finale is saved at stage " + stage + ". Every guest arrives to a show that has already ended — reset stage to 0 before uploading.",
                finale);
        }

        ReportNullEntries(blackoutOff, "onBlackoutDisable", "finale", finale);
        ReportNullEntries(bowOn, "onBowEnable", "finale", finale);
        ReportNullEntries(gateOn, "onGateOpenEnable", "finale", finale);
        ReportNullEntries(gateOff, "onGateOpenDisable", "finale", finale);
    }

    /// <summary>The finale the manager actually points at, if any.</summary>
    private static Component FinaleFor(Component manager)
    {
        return manager == null ? null : FieldQuiet(manager, "finale") as Component;
    }

    // -------------------------------------------------------------- endings

    /// <summary>
    /// The three endings. A clean break cannot be broken by a wiring mistake; the
    /// other two can, and both fail silently — a run that could never produce them
    /// looks exactly like a run that could. That is why the unreachable ones are
    /// errors and not notes.
    /// </summary>
    private static void CheckEndings(GameObject root, Component manager)
    {
        Type teethType = CarnivalRigWiring.FindType("CarnivalTeeth");

        if (teethType == null)
        {
            Add(Severity.Note, "endings",
                "no compiled type named CarnivalTeeth, so the hidden ending is unavailable: every run ends on a clean break or an understudy.", root);

            return;
        }

        Component teeth = FindAny(teethType);

        if (teeth == null)
        {
            Add(Severity.Error, "endings",
                "CarnivalTeeth is not in the scene. The tooth props still take an Interact, but nothing counts them, so all twelve can never add up.", root);

            return;
        }

        int required = Int(teeth, "requiredTeeth");
        Array ledger = Field(teeth, "teeth") as Array;
        int wired = ledger == null ? 0 : ledger.Length;

        if (required < 1 || required > 31)
        {
            Add(Severity.Error, "endings", "requiredTeeth is " + required + "; the mask only has room for 1–31.", teeth);
        }
        else if (wired != required)
        {
            Add(Severity.Error, "endings",
                "teeth has " + wired + " entries but requiredTeeth is " + required + ". The ledger can never fill, so the hidden ending is unreachable " +
                "while the hierarchy looks finished.", teeth);
        }

        if (required != 12)
        {
            Add(Severity.Note, "endings", "requiredTeeth is " + required + "; the deck's hidden ending asks for twelve.", teeth);
        }

        if (ledger != null)
        {
            HashSet<int> seen = new HashSet<int>();

            for (int i = 0; i < ledger.Length; i++)
            {
                Component tooth = ledger.GetValue(i) as Component;

                if (tooth == null)
                {
                    Add(Severity.Error, "endings", "teeth[" + i + "] is empty, so that one can never be found.", teeth);
                    continue;
                }

                int index = Int(tooth, "toothIndex");

                if (index < 0 || index >= 31)
                {
                    Add(Severity.Error, "endings", tooth.name + " has toothIndex " + index + ", outside 0–30.", tooth);
                }
                else if (!seen.Add(index))
                {
                    Add(Severity.Error, "endings",
                        "two teeth claim toothIndex " + index + "; taking either one marks the other as found, so the count fills with a socket still holding a tooth.", tooth);
                }

                if (Field(tooth, "teeth") != teeth)
                {
                    Add(Severity.Error, "endings", tooth.name + " does not report to the teeth ledger, so it is a prop that cannot be picked up.", tooth);
                }

                Collider reach = tooth.GetComponent<Collider>();

                if (reach == null || reach.isTrigger)
                {
                    Add(Severity.Error, "endings", tooth.name + " has no solid collider, so no guest can ever reach it with the cursor.", tooth);
                }
            }
        }

        Type finaleType = CarnivalRigWiring.FindType("CarnivalFinale");

        if (finaleType == null) return;

        Component finale = FindAny(finaleType);

        if (finale == null) return;

        if (Field(finale, "teeth") != teeth)
        {
            Add(Severity.Error, "endings",
                "the finale is not pointed at the teeth ledger, so twelve teeth change nothing and the hidden ending plays as a clean break.", finale);
        }

        if (Field(finale, "curtain") == null)
        {
            Add(Severity.Warning, "endings",
                "the finale has no curtain reference, so the understudy ending can never fire: nobody is counted in the ring.", finale);
        }

        Array clean = Field(finale, "onCleanEnable") as Array;
        Array understudy = Field(finale, "onUnderstudyEnable") as Array;
        Array owner = Field(finale, "onOwnerEnable") as Array;

        if (clean == null || clean.Length == 0)
        {
            Add(Severity.Warning, "endings", "onCleanEnable is empty, so a clean break opens the gate with nothing to mark it.", finale);
        }

        if (understudy == null || understudy.Length == 0)
        {
            Add(Severity.Error, "endings",
                "onUnderstudyEnable is empty. The understudy then looks identical to a clean break, so the guest who stayed behind gets no payoff for staying.", finale);
        }

        if (owner == null || owner.Length == 0)
        {
            Add(Severity.Error, "endings",
                "onOwnerEnable is empty. Twelve teeth, and the hidden ending pays out as an ordinary clean break.", finale);
        }

        ReportShared(clean, "onCleanEnable", understudy, "onUnderstudyEnable", finale);
        ReportShared(understudy, "onUnderstudyEnable", owner, "onOwnerEnable", finale);
        ReportShared(clean, "onCleanEnable", owner, "onOwnerEnable", finale);

        ReportActiveAtLoad(clean, "onCleanEnable", finale);
        ReportActiveAtLoad(understudy, "onUnderstudyEnable", finale);
        ReportActiveAtLoad(owner, "onOwnerEnable", finale);

        int ending = Int(finale, "ending");

        if (ending != 0)
        {
            Add(Severity.Error, "endings",
                "the finale is saved with ending " + ending + ". Every guest arrives to an ending decided in someone else's session — reset it to 0 before uploading.", finale);
        }
    }

    /// <summary>One prop in two endings: whichever write lands second wins, so the ending depends on array order.</summary>
    private static void ReportShared(Array first, string firstField, Array second, string secondField, Component owner)
    {
        if (first == null || second == null) return;

        for (int i = 0; i < first.Length; i++)
        {
            UnityEngine.Object item = first.GetValue(i) as UnityEngine.Object;

            if (item == null) continue;

            if (ArrayContains(second, item))
            {
                Add(Severity.Error, "endings",
                    item.name + " is in both " + firstField + " and " + secondField + "; two endings would fight over the same prop.", item);
            }
        }
    }

    private static void ReportActiveAtLoad(Array array, string fieldName, Component owner)
    {
        if (array == null) return;

        for (int i = 0; i < array.Length; i++)
        {
            GameObject prop = array.GetValue(i) as GameObject;

            if (prop == null) continue;

            if (prop.activeSelf)
            {
                Add(Severity.Warning, "endings",
                    fieldName + "[" + i + "] (" + prop.name + ") starts visible, so an ending is already showing before the gate opens.", prop);
            }
        }
    }

    // ------------------------------------------------------------- replay

    /// <summary>
    /// The reset lever. A run that cannot be reset is a run the second party
    /// inherits solved: the mask, the stage and the teeth all outlive the guests
    /// who earned them. Every check here is a piece of per-run state that nothing
    /// else clears.
    /// </summary>
    private static void CheckReset(GameObject root, Component manager, Component systemsSurprises)
    {
        Type resetType = CarnivalRigWiring.FindType("CarnivalRunReset");
        Component reset = null;

        if (resetType != null)
        {
            Component[] all = root.GetComponentsInChildren<Component>(true);

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;

                if (resetType.IsInstanceOfType(all[i]))
                {
                    reset = all[i];
                    break;
                }
            }
        }

        if (reset == null)
        {
            Add(Severity.Error, "replay",
                "no CarnivalRunReset in the scene, so a finished show cannot be replayed: the solved mask, " +
                "the finale stage, the endings and the twelve teeth all stay where the last party left them.",
                root);

            return;
        }

        if (!reset.gameObject.activeInHierarchy)
        {
            Add(Severity.Error, "replay",
                reset.gameObject.name + " is disabled, so the lever cannot be pulled and no run can be reset.", reset);
        }

        if (reset.GetComponent<Collider>() == null)
        {
            Add(Severity.Error, "replay",
                reset.gameObject.name + " has no collider, so Interact never reaches it. A lever nobody can click " +
                "is not a lever.", reset);
        }

        if (Float(reset, "cooldownSeconds") <= 0f)
        {
            Add(Severity.Warning, "replay",
                "cooldownSeconds is 0, so a double Interact resets twice in the same frame.", reset);
        }

        // The six puzzles. Each owns a synced answer the manager cannot clear,
        // which is the whole reason this component lists them by hand.
        string[] fields = { "ticketLock", "scaleLock", "mirrorLock", "balloonLock", "boilerLock", "curtainLock" };
        List<int> covered = new List<int>();

        for (int i = 0; i < fields.Length; i++)
        {
            Component puzzle = Field(reset, fields[i]) as Component;

            if (puzzle == null)
            {
                Add(Severity.Error, "replay",
                    "no " + fields[i] + " on the reset lever. That puzzle keeps its own synced answer, so its lock " +
                    "is solved again a frame after the mask is cleared.",
                    reset);

                continue;
            }

            if (fields[i] == "mirrorLock") CheckMirrorReset(puzzle);

            Component lockRef = Field(puzzle, "lockRef") as Component;

            if (lockRef == null)
            {
                Add(Severity.Warning, "replay",
                    fields[i] + " has no lockRef, so there is no way to tell which lock it clears.", puzzle);

                continue;
            }

            covered.Add(Int(lockRef, "lockId"));
        }

        if (manager != null)
        {
            Array locks = Field(manager, "locks") as Array;

            if (locks != null)
            {
                for (int i = 0; i < locks.Length; i++)
                {
                    Component lockBehaviour = locks.GetValue(i) as Component;

                    if (lockBehaviour == null) continue;

                    int id = Int(lockBehaviour, "lockId");

                    if (covered.Contains(id)) continue;

                    string lockName = Field(lockBehaviour, "lockName") as string;

                    Add(Severity.Error, "replay",
                        "lock " + id + " (" + (lockName == null ? lockBehaviour.name : lockName) + ") has no puzzle " +
                        "behind the reset lever, so it stays solved for the next party.",
                        lockBehaviour);
                }
            }
        }

        Component finale = Field(reset, "finale") as Component;
        Component teeth = Field(reset, "teeth") as Component;
        Component timer = Field(reset, "timer") as Component;
        Component starter = Field(reset, "starter") as Component;

        if (finale == null)
        {
            Add(Severity.Error, "replay",
                "no finale on the reset lever: the stage stays at 3, so the next run walks in to an open gate.", reset);
        }
        else if (manager != null)
        {
            Component managerFinale = Field(manager, "finale") as Component;

            if (managerFinale != null && managerFinale != finale)
            {
                Add(Severity.Error, "replay",
                    "the reset lever clears a different CarnivalFinale than the manager hands the run to, so the " +
                    "one that opens the gate is never reset.",
                    reset);
            }
        }

        if (teeth == null)
        {
            Add(Severity.Error, "replay",
                "no teeth ledger on the reset lever: the twelve stay found, so every later run earns the hidden " +
                "ending for free.",
                reset);
        }

        if (timer == null)
        {
            Add(Severity.Error, "replay",
                "no clock on the reset lever. timeUp stays set, and CarnivalShowStarter ignores every crossing " +
                "while it is, so the second party can never start a run.",
                reset);
        }

        if (starter == null)
        {
            Add(Severity.Error, "replay",
                "no gate line on the reset lever: the starter stays fired, so the next crossing starts nothing.", reset);
        }

        // The scare budget is the one part of the reset that is not a synced
        // field on the lever's own client: each guest's count lives in an array
        // on their own copy of the director, and only the epoch moving under it
        // clears it. A lever with no director leaves every guest spent, so the
        // second party walks in with the first party's scares gone.
        Component surprises = Field(reset, "surprises") as Component;

        if (surprises == null)
        {
            Add(Severity.Error, "replay",
                "no SurpriseDirector on the reset lever: every guest's six scares stay spent, and no booked scare " +
                "is cancelled, so the second party inherits both.", reset);
        }
        else if (systemsSurprises != null && surprises != systemsSurprises)
        {
            Add(Severity.Error, "replay",
                "the reset lever clears a different SurpriseDirector than the one on _Systems, so the budget that " +
                "gets cleared is a director no prop asks, and the one they do ask stays spent.", reset);
        }
    }

    /// <summary>
    /// A reset puts every mirror frame back on angle 0, so an answer made entirely
    /// of zeros is a lock the reset has already opened.
    /// </summary>
    private static void CheckMirrorReset(Component puzzle)
    {
        Array solution = Field(puzzle, "solution") as Array;

        if (solution == null || solution.Length == 0) return;

        bool allZero = true;

        for (int i = 0; i < solution.Length; i++)
        {
            object value = solution.GetValue(i);

            if (value is int && (int)value != 0)
            {
                allZero = false;
                break;
            }
        }

        if (!allZero) return;

        Add(Severity.Error, "replay",
            "the mirror solution is all zeros, which is exactly where a reset puts every frame — the run starts " +
            "with that lock already aligned.",
            puzzle);
    }

    /// <summary>
    /// The counterweight's step-0 spot. Two ways it lies without looking wrong: a
    /// home on the pan's side of the fulcrum, where moving the weight out moves it
    /// towards the load and the lightest setting is the one furthest out; and a
    /// home sitting on the fulcrum, where step 0 has no leverage at all.
    /// </summary>
    private static void CheckCounterweightHome(Transform beam, Transform home, Array sockets)
    {
        if (home == null || beam == null) return;

        Vector3 homeInBeam = beam.InverseTransformPoint(home.position);

        if (!home.IsChildOf(beam))
        {
            Add(Severity.Note, "weight",
                home.name + " is not a child of the beam. Its spot is read through the beam either way, but a marker parented elsewhere stops following the arm when it tilts.",
                home);
        }

        if (sockets == null || sockets.Length == 0) return;

        float panSide = 0f;
        int counted = 0;

        for (int i = 0; i < sockets.Length; i++)
        {
            Transform socket = sockets.GetValue(i) as Transform;

            if (socket == null) continue;

            panSide += beam.InverseTransformPoint(socket.position).x;
            counted++;
        }

        if (counted == 0) return;

        panSide /= counted;

        if (Mathf.Abs(homeInBeam.x) < 0.05f)
        {
            Add(Severity.Note, "weight",
                home.name + " sits on the fulcrum, so step 0 shows no leverage and the first step swings the arm more than the last.", home);
        }
        else if (Mathf.Sign(homeInBeam.x) == Mathf.Sign(panSide))
        {
            Add(Severity.Warning, "weight",
                home.name + " is on the pan's side of the fulcrum, so stepping the counterweight out moves it towards the load: the lightest setting is the one furthest from the middle, which is the opposite of what a guest reading the arm is told.",
                home);
        }
    }

    // ----------------------------------------------------------- triggers

    private static void CheckTriggers(GameObject root, Component timer, Component surprises)
    {
        Physics.SyncTransforms();

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        Type starterType = CarnivalRigWiring.FindType("CarnivalShowStarter");

        int triggers = 0;
        int starters = 0;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];

            if (collider == null || !collider.isTrigger) continue;

            triggers++;

            string label = collider.gameObject.name;

            if (!collider.gameObject.activeInHierarchy)
            {
                Add(Severity.Error, "triggers", label + " is disabled, so no guest can ever cross it and whatever it starts never starts.", collider);
                continue;
            }

            Vector3 size = collider.bounds.size;

            if (size.x < 1.5f || size.z < 1.5f || size.y < 1.6f)
            {
                Add(Severity.Warning, "triggers",
                    label + " measures " + Fmt(size) + " m. A guest can cross beside or under a volume that thin without touching it.", collider);
            }

            ReportBuried(collider, label);

            Component starter = starterType != null ? collider.GetComponent(starterType) : null;

            if (starter != null)
            {
                starters++;

                Component timerRef = Field(starter, "timer") as Component;

                if (timerRef == null)
                {
                    Add(Severity.Error, "triggers", label + " has a CarnivalShowStarter with no timer assigned, so crossing it starts nothing.", starter);
                }
                else if (timer != null && timerRef != timer)
                {
                    Add(Severity.Warning, "triggers", label + " points at a different clock than the one on _Systems.", starter);
                }

                Component surpriseRef = Field(starter, "surprises") as Component;

                if (surpriseRef == null)
                {
                    Add(Severity.Warning, "triggers", label + " has no SurpriseDirector, so the second run starts with the first run's scare budget already spent.", starter);
                }
                else if (surprises != null && surpriseRef != surprises)
                {
                    Add(Severity.Note, "triggers", label + " points at a SurpriseDirector that is not the one on _Systems.", starter);
                }
            }
            else if (!HasUdonBehaviour(collider.gameObject))
            {
                Add(Severity.Warning, "triggers", label + " is a trigger volume with no UdonSharp behaviour on it, so nothing happens when a guest crosses.", collider);
            }
        }

        if (triggers == 0)
        {
            Add(Severity.Error, "triggers", "there are no trigger volumes under " + RootName + ", so nothing can start the run.", root);
        }

        int gateLines = 0;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == "GATE_LINE") gateLines++;
        }

        if (gateLines > 1)
        {
            Add(Severity.Warning, "triggers", gateLines + " objects are named GATE_LINE; only the one with the starter should exist.", root);
        }

        if (starters == 0 && timer != null)
        {
            if (Bool(timer, "autoStart"))
            {
                Add(Severity.Note, "triggers", "no gate line, but autoStart is on, so the clock still begins at instance load.", timer);
            }
            else
            {
                Add(Severity.Error, "triggers",
                    "nothing starts the clock: no CarnivalShowStarter on any trigger and autoStart is off. " +
                    "The run has no time limit and no finale within the show's fiction.", timer);
            }
        }
    }

    /// <summary>
    /// A trigger inside a wall or a floor slab is unreachable even though it
    /// looks correct in the hierarchy. Overlap test, inset first so touching
    /// surfaces do not count as overlap.
    /// </summary>
    private static void ReportBuried(Collider collider, string label)
    {
        BoxCollider box = collider as BoxCollider;

        Vector3 half;
        Vector3 center;
        Quaternion rotation;

        if (box != null)
        {
            half = Vector3.Scale(box.size * 0.5f, Abs(box.transform.lossyScale));
            center = box.transform.TransformPoint(box.center);
            rotation = box.transform.rotation;
        }
        else
        {
            half = collider.bounds.extents;
            center = collider.bounds.center;
            rotation = Quaternion.identity;
        }

        half = new Vector3(Mathf.Max(0.05f, half.x - 0.15f),
                           Mathf.Max(0.05f, half.y - 0.15f),
                           Mathf.Max(0.05f, half.z - 0.15f));

        Collider[] hits = Physics.OverlapBox(center, half, rotation, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];

            if (hit == null || hit == collider) continue;
            if (hit.transform.IsChildOf(collider.transform) || collider.transform.IsChildOf(hit.transform)) continue;

            Add(Severity.Error, "triggers",
                label + " occupies the same space as " + Path(hit.gameObject) + " (solid). A guest can never reach the volume, " +
                "so nothing it triggers ever happens.", collider);

            return;
        }
    }

    // ---------------------------------------------------------------- world

    private static void CheckDescriptor()
    {
        Component descriptor = null;
        Component[] all = UnityEngine.Object.FindObjectsOfType<Component>(true);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].GetType().Name.Contains("SceneDescriptor"))
            {
                descriptor = all[i];
                break;
            }
        }

        if (descriptor == null)
        {
            Add(Severity.Warning, "vrc",
                "no VRChat world descriptor in the scene. This tool does not create one on purpose — add the VRCWorld prefab, " +
                "set the spawn points and the reference camera before uploading.", null);
            return;
        }

        Array spawns = FieldQuiet(descriptor, "spawns") as Array;

        if (spawns == null || spawns.Length == 0)
        {
            Add(Severity.Warning, "vrc", "the world descriptor has no spawn points, so guests arrive at the origin.", descriptor);
        }
    }

    // --------------------------------------------------------------- report

    private static void Report(GameObject root)
    {
        int errors = 0;
        int warnings = 0;
        int notes = 0;

        for (int i = 0; i < Findings.Count; i++)
        {
            switch (Findings[i].Severity)
            {
                case Severity.Error: errors++; break;
                case Severity.Warning: warnings++; break;
                default: notes++; break;
            }
        }

        Findings.Sort(delegate (Finding a, Finding b)
        {
            if (a.Severity != b.Severity) return b.Severity.CompareTo(a.Severity);
            return string.Compare(a.Area, b.Area, StringComparison.Ordinal);
        });

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[Carnival] Rig check — " + SceneManager.GetActiveScene().name);
        sb.AppendLine("  " + errors + " error(s), " + warnings + " warning(s), " + notes + " note(s)");

        if (Findings.Count == 0)
        {
            sb.AppendLine("  Nothing to report.");
        }

        UnityEngine.Object firstError = null;

        for (int i = 0; i < Findings.Count; i++)
        {
            Finding finding = Findings[i];
            sb.AppendLine("  " + Tag(finding.Severity) + " " + finding.Area.PadRight(9) + finding.Message);

            if (firstError == null && finding.Severity == Severity.Error && finding.Context != null)
            {
                firstError = finding.Context;
            }
        }

        string verdict;

        if (errors > 0)
        {
            verdict = "NOT READY — " + errors + " error(s) would break the run. Fix those, then re-run.";
        }
        else if (warnings > 0)
        {
            verdict = "PLAYABLE — no errors. The warnings above are worth a look before a public upload.";
        }
        else
        {
            verdict = "CLEAN — every reference is set, the mask matches the locks, and the clock can start.";
        }

        sb.AppendLine();
        sb.AppendLine("  " + verdict);

        if (errors > 0)
        {
            Debug.LogError(sb.ToString());

            if (firstError != null)
            {
                Selection.activeGameObject = firstError is Component
                    ? ((Component)firstError).gameObject
                    : (firstError as GameObject != null ? ((GameObject)firstError) : null);

                EditorGUIUtility.PingObject(firstError);
            }
        }
        else if (warnings > 0)
        {
            Debug.LogWarning(sb.ToString());
        }
        else
        {
            Debug.Log(sb.ToString());
        }

        EditorUtility.DisplayDialog(
            "Carnival World — rig check",
            verdict + "\n\n" +
            errors + " error(s)\n" + warnings + " warning(s)\n" + notes + " note(s)\n\n" +
            "Full list with object context in the Console.",
            "Close");
    }

    // --------------------------------------------------------------- helpers

    private static Component Single(string typeName, string area)
    {
        Type type = CarnivalRigWiring.FindType(typeName);

        if (type == null)
        {
            Add(Severity.Error, area,
                "no compiled type named " + typeName + ". Let UdonSharp finish compiling, then re-run this check.", null);
            return null;
        }

        UnityEngine.Object[] found = UnityEngine.Object.FindObjectsOfType(type, true);

        if (found.Length == 0)
        {
            Add(Severity.Error, area,
                "no " + typeName + " in the scene. Run Carnival World > 4. Wire Show Rig (playable) first.", null);
            return null;
        }

        if (found.Length > 1)
        {
            Add(Severity.Error, area,
                found.Length + " " + typeName + " components exist. Two managers or two clocks split the run state between them.", found[0]);
        }

        return found[0] as Component;
    }

    private static void Add(Severity severity, string area, string message, UnityEngine.Object context)
    {
        Finding finding = new Finding();
        finding.Severity = severity;
        finding.Area = area;
        finding.Message = message;
        finding.Context = context;

        Findings.Add(finding);
    }

    private static void ReportNullEntries(Array array, string fieldName, string area, Component owner)
    {
        if (array == null) return;

        for (int i = 0; i < array.Length; i++)
        {
            if (array.GetValue(i) == null)
            {
                Add(Severity.Warning, area, fieldName + "[" + i + "] is empty; that slot does nothing when the finale fires.", owner);
            }
        }
    }

    private static object Field(Component target, string fieldName)
    {
        if (target == null) return null;

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);

        if (field == null)
        {
            Add(Severity.Error, "script",
                target.GetType().Name + " has no public field '" + fieldName + "'. This tool and the scripts are out of step — " +
                "re-import the package so both come from the same version.", target);

            return null;
        }

        return field.GetValue(target);
    }

    private static object FieldQuiet(Component target, string fieldName)
    {
        if (target == null) return null;

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);

        return field == null ? null : field.GetValue(target);
    }

    private static int Int(Component target, string fieldName)
    {
        object value = Field(target, fieldName);
        return value is int ? (int)value : 0;
    }

    private static float Float(Component target, string fieldName)
    {
        object value = Field(target, fieldName);
        return value is float ? (float)value : 0f;
    }

    private static bool Bool(Component target, string fieldName)
    {
        object value = Field(target, fieldName);
        return value is bool && (bool)value;
    }

    private static bool ArrayContains(Array array, UnityEngine.Object item)
    {
        if (array == null || item == null) return false;

        for (int i = 0; i < array.Length; i++)
        {
            if (array.GetValue(i) == item) return true;
        }

        return false;
    }

    private static bool HasUdonBehaviour(GameObject host)
    {
        Component[] components = host.GetComponents<Component>();

        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) continue;

            Type type = component.GetType();

            while (type != null)
            {
                if (type.Name == "UdonSharpBehaviour") return true;
                type = type.BaseType;
            }
        }

        return false;
    }

    private static string ListBits(int mask)
    {
        StringBuilder sb = new StringBuilder();

        for (int bit = 0; bit < MaxLockId; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;

            if (sb.Length > 0) sb.Append(", ");
            sb.Append(bit + 1);
        }

        return sb.Length == 0 ? "none" : sb.ToString();
    }

    private static string Tag(Severity severity)
    {
        if (severity == Severity.Error) return "<color=#ff6b6b>ERROR  </color>";
        if (severity == Severity.Warning) return "<color=#ffc857>WARN   </color>";

        return "<color=#8fd6ff>NOTE   </color>";
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static string Fmt(Vector3 size)
    {
        return size.x.ToString("0.0") + " × " + size.y.ToString("0.0") + " × " + size.z.ToString("0.0");
    }

    /// <summary>Hierarchy path, so a Console line identifies the object without clicking it.</summary>
    private static string Path(GameObject target)
    {
        string path = target.name;
        Transform parent = target.transform.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }
}
#endif
