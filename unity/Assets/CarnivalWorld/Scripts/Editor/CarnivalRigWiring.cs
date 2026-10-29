#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wires the UdonSharp show rig onto the greybox: one manager, six locks, the
/// clock, the role board, the surprise budget, the exit gate and the gate line.
///
/// UdonSharp types are resolved by name through reflection rather than by
/// reference. That keeps this tool in the same assembly style as the greybox
/// builder — it compiles and runs against whatever the project actually has,
/// and it reports a missing type instead of failing to build the editor.
///
/// The tool is idempotent: existing components and markers are reused, so it can
/// be re-run after moving anchors without duplicating the rig.
///
/// Menu: Carnival World > 4. Wire Show Rig (playable)
/// </summary>
public static class CarnivalRigWiring
{
    private const string RootName = "CarnivalWorld_Greybox";
    private const string SystemsName = "_Systems";
    private const string MatFolder = "Assets/CarnivalWorld/Greybox/Materials";

    // Order is the lock order on the puzzle sheet: index 0 is lockId 1.
    // Internal so the validator checks the same six anchors this tool wires.
    internal static readonly string[] LockAnchors =
    {
        "LOCK_01_TicketTear",
        "LOCK_02_WeightOfTheShow",
        "LOCK_03_MirrorsAside",
        "LOCK_04_BalloonLogic",
        "LOCK_05_BoilerLullaby",
        "LOCK_06_Curtain"
    };

    internal static readonly string[] LockTitles =
    {
        "Ticket Tear",
        "Weight of the Show",
        "Mirrors Aside",
        "Balloon Logic",
        "Boiler Lullaby",
        "The Curtain"
    };

    internal static readonly string[] BeatAnchors =
    {
        "BEAT_1_AudienceTurns",
        "BEAT_2_ReflectionLags",
        "BEAT_3_GrinnyBows"
    };

    // Twelve teeth, as anchor + local offset. Spread so the hidden ending costs a
    // detour through every zone: it is twelve small decisions, not one cupboard.
    internal static readonly string[] ToothAnchors =
    {
        "LOCK_01_TicketTear",
        "LOCK_01_TicketTear",
        "LOCK_02_WeightOfTheShow",
        "LOCK_02_WeightOfTheShow",
        "LOCK_03_MirrorsAside",
        "LOCK_03_MirrorsAside",
        "LOCK_04_BalloonLogic",
        "LOCK_04_BalloonLogic",
        "LOCK_05_BoilerLullaby",
        "LOCK_05_BoilerLullaby",
        "LOCK_06_Curtain",
        "HIDEOUT_DOORLINE"
    };

    internal static readonly Vector3[] ToothOffsets =
    {
        new Vector3(-1.5f, 1.1f, 1.4f),
        new Vector3(1.6f, 0.35f, 1.7f),
        new Vector3(-4f, 1.2f, 2.5f),
        new Vector3(4.5f, 0.4f, -2.5f),
        new Vector3(-2.5f, 1.2f, 4f),
        new Vector3(2.8f, 0.4f, -4f),
        new Vector3(-3f, 1f, 4f),
        new Vector3(3.2f, 0.5f, -4.2f),
        new Vector3(-2f, 1.1f, 2.6f),
        new Vector3(2.4f, 0.4f, -2.4f),
        new Vector3(3.4f, 0.8f, 0f),
        new Vector3(0.6f, 1.3f, 1f)
    };

    private static readonly List<string> Problems = new List<string>();

    [MenuItem("Carnival World/4. Wire Show Rig (playable)", false, 4)]
    public static void WireShowRig()
    {
        GameObject root = GameObject.Find(RootName);

        if (root == null)
        {
            bool build = EditorUtility.DisplayDialog(
                "Carnival World",
                "There is no greybox in this scene.\n\nBuild one and wire it now?",
                "Build and wire",
                "Cancel");

            if (!build) return;

            CarnivalGreyboxBuilder.BuildGreybox();
            root = GameObject.Find(RootName);

            if (root == null)
            {
                Debug.LogError("[Carnival] Wiring aborted: the greybox build produced no " + RootName + ".");
                return;
            }
        }

        Problems.Clear();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Wire Carnival Show Rig");
        int undoGroup = Undo.GetCurrentGroup();

        // ---- systems: one object owns the whole run ----------------------
        bool systemsCreated;
        GameObject systems = FindOrCreate(root.transform, SystemsName, out systemsCreated);

        Component manager = AddOrGet(systems, "CarnivalLockManager");
        Component timer = AddOrGet(systems, "CarnivalShowTimer");
        Component roles = AddOrGet(systems, "CarnivalRoleBoard");
        Component surprises = AddOrGet(systems, "SurpriseDirector");

        // The five puzzles that live inside their own blocks below. Declared out
        // here so the reset lever at the end of this method can wire all six
        // without finding them a second time.
        Component boiler = null;
        Component tickets = null;
        Component mirrors = null;
        Component balloons = null;
        Component scalePuzzle = null;

        // ---- six locks ---------------------------------------------------
        List<UnityEngine.Object> locks = new List<UnityEngine.Object>();

        for (int i = 0; i < LockAnchors.Length; i++)
        {
            GameObject anchor = FindDeep(root, LockAnchors[i]);

            if (anchor == null)
            {
                Problems.Add("missing anchor " + LockAnchors[i]);
                continue;
            }

            Component lockBehaviour = AddOrGet(anchor, "CarnivalLock");
            if (lockBehaviour == null) continue;

            SetField(lockBehaviour, "lockId", i + 1);
            SetField(lockBehaviour, "lockName", LockTitles[i]);
            SetField(lockBehaviour, "manager", manager);
            SetField(lockBehaviour, "solvedVisual",
                MakeBlock(anchor.transform, "SOLVED_PIN", new Vector3(0f, 1.7f, 0f),
                          new Vector3(0.2f, 0.2f, 0.2f), false, "M_Greybox_Gold"));

            locks.Add(lockBehaviour);
        }

        if (locks.Count != LockAnchors.Length)
        {
            Problems.Add("wired " + locks.Count + " of " + LockAnchors.Length + " locks");
        }

        if (manager != null && locks.Count > 0)
        {
            SetArrayField(manager, "locks", locks);
            SetField(manager, "requiredMask", 63);
        }

        // ---- curtain: four marks and the puzzle that reads them -----------
        // Lock 06 is the one lock with no prop: guests hold four pads in the ring.
        GameObject curtainAnchor = FindDeep(root, LockAnchors[5]);
        Component curtainPuzzle = null;
        List<UnityEngine.Object> curtainMarks = new List<UnityEngine.Object>();

        if (curtainAnchor == null)
        {
            Problems.Add("missing anchor " + LockAnchors[5] + " (curtain marks)");
        }
        else
        {
            curtainPuzzle = AddOrGet(curtainAnchor, "CarnivalCurtainLock");

            bool marksCreated;
            GameObject markGroup = FindOrCreate(curtainAnchor.transform, "CURTAIN_MARKS", out marksCreated);
            markGroup.transform.localPosition = Vector3.zero;

            for (int i = 0; i < 4; i++)
            {
                float angle = 90f * i * Mathf.Deg2Rad;
                Vector3 spot = new Vector3(Mathf.Sin(angle) * 2.6f, 0f, Mathf.Cos(angle) * 2.6f);

                bool markCreated;
                GameObject markObject = FindOrCreate(markGroup.transform, "CURTAIN_MARK_" + (i + 1), out markCreated);

                // Placed only when created, so a re-run does not undo a move you made.
                if (markCreated) markObject.transform.localPosition = spot;

                BoxCollider volume = markObject.GetComponent<BoxCollider>();

                if (volume == null)
                {
                    volume = Undo.AddComponent<BoxCollider>(markObject);
                    volume.size = new Vector3(1.6f, 1.8f, 1.6f);
                    volume.center = new Vector3(0f, 0.9f, 0f);
                }

                volume.isTrigger = true;

                GameObject pad = MakeBlock(markObject.transform, "MARK_PAD",
                    new Vector3(0f, 0.03f, 0f), new Vector3(1.1f, 0.06f, 1.1f), false, "M_Greybox_Gold");

                Component mark = AddOrGet(markObject, "CarnivalCurtainMark");
                if (mark == null) continue;

                SetField(mark, "markIndex", i);
                SetField(mark, "curtain", curtainPuzzle);
                SetField(mark, "litVisual", pad);

                curtainMarks.Add(mark);
            }

            // The centre pad is not one of the four marks: it keeps counting after
            // the curtain falls, and the finale reads it at the gate beat to decide
            // the understudy ending.
            bool centreCreated;
            GameObject centrePad = FindOrCreate(curtainAnchor.transform, "CURTAIN_CENTRE_MARK", out centreCreated);

            if (centreCreated) centrePad.transform.localPosition = Vector3.zero;

            BoxCollider centreVolume = centrePad.GetComponent<BoxCollider>();

            if (centreVolume == null)
            {
                centreVolume = Undo.AddComponent<BoxCollider>(centrePad);
                centreVolume.size = new Vector3(1.8f, 1.8f, 1.8f);
                centreVolume.center = new Vector3(0f, 0.9f, 0f);
            }

            centreVolume.isTrigger = true;

            GameObject centreGlow = MakeBlock(centrePad.transform, "CENTRE_PAD",
                new Vector3(0f, 0.03f, 0f), new Vector3(1.2f, 0.06f, 1.2f), false, "M_Greybox_Gold");

            Component centreMark = AddOrGet(centrePad, "CarnivalCurtainMark");

            if (centreMark != null)
            {
                SetField(centreMark, "centreMark", true);

                // -1 on purpose: the four-mark array must stay four, and a validator
                // that sees 0-3 on every mark treats a fifth one as a duplicate.
                SetField(centreMark, "markIndex", -1);
                SetField(centreMark, "curtain", curtainPuzzle);
                SetField(centreMark, "litVisual", centreGlow);
            }

            if (curtainPuzzle != null)
            {
                SetField(curtainPuzzle, "lockRef", locks.Count > 5 ? locks[5] as Component : null);
                SetField(curtainPuzzle, "roleBoard", roles);
                SetField(curtainPuzzle, "debounceSeconds", 0.4f);
                SetField(curtainPuzzle, "soloHoldSeconds", 8f);
                SetArrayField(curtainPuzzle, "marks", curtainMarks);
            }

            if (curtainMarks.Count != 4)
            {
                Problems.Add("wired " + curtainMarks.Count + " of 4 curtain marks");
            }
        }

        // ---- boiler: four valves that spell the calliope phrase -----------
        // Valve i cycles 0-3; the boiler compares it with melody[i].
        GameObject boilerAnchor = FindDeep(root, LockAnchors[4]);
        int valvesWired = 0;

        if (boilerAnchor == null)
        {
            Problems.Add("missing anchor " + LockAnchors[4] + " (boiler valves)");
        }
        else
        {
            boiler = AddOrGet(boilerAnchor, "CarnivalBoilerLock");

            bool valveGroupCreated;
            GameObject valveGroup = FindOrCreate(boilerAnchor.transform, "BOILER_VALVES", out valveGroupCreated);
            valveGroup.transform.localPosition = Vector3.zero;

            List<UnityEngine.Object> valveList = new List<UnityEngine.Object>();

            for (int i = 0; i < 4; i++)
            {
                bool valveCreated;
                GameObject valve = FindOrCreate(valveGroup.transform, "VALVE_" + (i + 1), out valveCreated);

                // Placed only when created, so a re-run does not undo a move you made.
                if (valveCreated) valve.transform.localPosition = new Vector3(-1.05f + i * 0.7f, 1.4f, 0f);

                // Interact() needs a solid collider on the behaviour's own object.
                BoxCollider reach = valve.GetComponent<BoxCollider>();

                if (reach == null)
                {
                    reach = Undo.AddComponent<BoxCollider>(valve);
                    reach.size = new Vector3(0.46f, 0.46f, 0.18f);
                    reach.center = Vector3.zero;
                }

                // Plate and knob are visual only: no collider, so they never
                // swallow the interaction.
                GameObject plate = MakeBlock(valve.transform, "VALVE_PLATE", Vector3.zero,
                    new Vector3(0.4f, 0.4f, 0.1f), false, "M_Greybox_Gold");
                plate.SetActive(true);

                GameObject knob = MakeBlock(valve.transform, "VALVE_KNOB", new Vector3(0f, 0f, 0.09f),
                    new Vector3(0.34f, 0.07f, 0.03f), false, "M_Greybox_Bone");
                knob.SetActive(true);

                Component valveComponent = AddOrGet(valve, "CarnivalValve");
                if (valveComponent == null) continue;

                SetField(valveComponent, "valveIndex", i);
                SetField(valveComponent, "boiler", boiler);
                SetField(valveComponent, "knob", knob.transform);

                valveList.Add(valveComponent);
                valvesWired++;
            }

            GameObject steam = MakeBlock(boilerAnchor.transform, "BOILER_STEAM",
                new Vector3(0f, 2.4f, 0f), new Vector3(1.2f, 0.7f, 1.2f), false, "M_Greybox_Bone");

            if (boiler != null)
            {
                SetField(boiler, "lockRef", locks.Count > 4 ? locks[4] as Component : null);
                SetField(boiler, "surprises", surprises);
                SetField(boiler, "wrongNoteCooldown", 2f);
                SetArrayField(boiler, "valves", valveList);
                SetArrayField(boiler, "onWrongNoteEnable", One(steam));
            }

            if (valvesWired != 4)
            {
                Problems.Add("wired " + valvesWired + " of 4 boiler valves");
            }
        }

        // ---- ticket booth: four stubs, one mouth, one photograph ----------
        GameObject boothAnchor = FindDeep(root, LockAnchors[0]);
        int stubsWired = 0;

        if (boothAnchor == null)
        {
            Problems.Add("missing anchor " + LockAnchors[0] + " (ticket stubs)");
        }
        else
        {
            tickets = AddOrGet(boothAnchor, "CarnivalTicketLock");

            bool mouthCreated;
            GameObject mouth = FindOrCreate(boothAnchor.transform, "TICKET_MOUTH", out mouthCreated);

            if (mouthCreated) mouth.transform.localPosition = new Vector3(0f, 1.15f, 0.75f);

            bool stubGroupCreated;
            GameObject stubGroup = FindOrCreate(boothAnchor.transform, "TICKET_STUBS", out stubGroupCreated);
            stubGroup.transform.localPosition = Vector3.zero;

            List<UnityEngine.Object> stubList = new List<UnityEngine.Object>();
            Type pickupType = FindType("VRC_Pickup");

            for (int i = 0; i < 4; i++)
            {
                bool stubCreated;
                GameObject stub = FindOrCreate(stubGroup.transform, "TICKET_STUB_" + (i + 1), out stubCreated);

                if (stubCreated) stub.transform.localPosition = new Vector3(-0.6f + i * 0.4f, 1f, 1.3f);

                // The stub's home for the reset: an empty marker at the stub's
                // own starting spot, so moving the prop in the scene is enough
                // and the reset follows it instead of a number baked in code.
                bool stubHomeCreated;
                GameObject stubHome = FindOrCreate(stubGroup.transform, "TICKET_STUB_HOME_" + (i + 1), out stubHomeCreated);

                if (stubHomeCreated) stubHome.transform.localPosition = stub.transform.localPosition;

                // A pickup needs a collider to grip and to be seen by the cursor.
                BoxCollider grip = stub.GetComponent<BoxCollider>();

                if (grip == null)
                {
                    grip = Undo.AddComponent<BoxCollider>(stub);
                    grip.size = new Vector3(0.12f, 0.03f, 0.2f);
                    grip.center = Vector3.zero;
                }

                GameObject card = MakeBlock(stub.transform, "STUB_CARD", Vector3.zero,
                    new Vector3(0.12f, 0.03f, 0.2f), false, "M_Greybox_Bone");
                card.SetActive(true);

                if (pickupType == null)
                {
                    Problems.Add("no VRC_Pickup type, so the stubs cannot be carried — hand them over with Interact");
                }
                else if (stub.GetComponent(pickupType) == null)
                {
                    Undo.AddComponent(stub, pickupType);
                }

                Component stubComponent = AddOrGet(stub, "CarnivalTicketStub");
                if (stubComponent == null) continue;

                SetField(stubComponent, "stubId", i + 1);
                SetField(stubComponent, "ticketLock", tickets);
                SetField(stubComponent, "mouth", mouth.transform);
                SetField(stubComponent, "home", stubHome.transform);

                stubList.Add(stubComponent);
                stubsWired++;
            }

            // Four lamps over the mouth: the only count the booth shows.
            List<UnityEngine.Object> lamps = new List<UnityEngine.Object>();

            for (int i = 0; i < 4; i++)
            {
                lamps.Add(MakeBlock(boothAnchor.transform, "STUB_LAMP_" + (i + 1),
                    new Vector3(-0.45f + i * 0.3f, 1.55f, 0.72f),
                    new Vector3(0.08f, 0.08f, 0.06f), false, "M_Greybox_Acid"));
            }

            GameObject photo = MakeBlock(boothAnchor.transform, "PHOTO_1986",
                new Vector3(0f, 1.85f, 0.88f), new Vector3(0.55f, 0.42f, 0.05f), false, "M_Greybox_Bone");

            if (tickets != null)
            {
                SetField(tickets, "lockRef", locks.Count > 0 ? locks[0] as Component : null);
                SetArrayField(tickets, "stubs", stubList);
                SetArrayField(tickets, "stubLights", lamps);
                SetArrayField(tickets, "onRevealEnable", One(photo));
            }

            if (stubsWired != 4)
            {
                Problems.Add("wired " + stubsWired + " of 4 ticket stubs");
            }
        }

        // ---- hall of mirrors: four frames, four beams, one rune ----------
        GameObject mirrorAnchor = FindDeep(root, LockAnchors[2]);
        int framesWired = 0;

        if (mirrorAnchor == null)
        {
            Problems.Add("missing anchor " + LockAnchors[2] + " (mirror frames)");
        }
        else
        {
            mirrors = AddOrGet(mirrorAnchor, "CarnivalMirrorLock");

            bool frameGroupCreated;
            GameObject frameGroup = FindOrCreate(mirrorAnchor.transform, "MIRROR_FRAMES", out frameGroupCreated);
            frameGroup.transform.localPosition = Vector3.zero;

            List<UnityEngine.Object> frameList = new List<UnityEngine.Object>();

            for (int i = 0; i < 4; i++)
            {
                bool frameCreated;
                GameObject frame = FindOrCreate(frameGroup.transform, "MIRROR_FRAME_" + (i + 1), out frameCreated);

                if (frameCreated) frame.transform.localPosition = new Vector3(-1.8f + i * 1.2f, 1.1f, -1.5f);

                // The pane is visual only. A solid child would put a collider in
                // front of the frame, and the interact ray would hit it instead
                // of the behaviour, so the mirror would never turn.
                GameObject glass = MakeBlock(frame.transform, "FRAME_GLASS", Vector3.zero,
                    new Vector3(0.1f, 1.8f, 0.9f), false, "M_Greybox_Bone");
                glass.SetActive(true);

                // Beam points along the frame's forward, so it swings as the
                // frame turns and only reads correctly on the solution angle.
                GameObject beam = MakeBlock(frame.transform, "FRAME_BEAM", new Vector3(0f, 0f, 1.05f),
                    new Vector3(0.05f, 0.05f, 2f), false, "M_Greybox_Acid");

                BoxCollider reach = frame.GetComponent<BoxCollider>();

                if (reach == null)
                {
                    reach = Undo.AddComponent<BoxCollider>(frame);
                    reach.size = new Vector3(0.2f, 1.9f, 1f);
                    reach.center = Vector3.zero;
                }

                Component frameComponent = AddOrGet(frame, "CarnivalMirrorFrame");
                if (frameComponent == null) continue;

                SetField(frameComponent, "frameIndex", i);
                SetField(frameComponent, "mirrorLock", mirrors);
                SetField(frameComponent, "frame", frame.transform);
                SetArrayField(frameComponent, "beamWhenAligned", One(beam));

                frameList.Add(frameComponent);
                framesWired++;
            }

            GameObject rune = MakeBlock(mirrorAnchor.transform, "RUNE_LIT",
                new Vector3(0f, 1.1f, 3.4f), new Vector3(0.55f, 0.55f, 0.06f), false, "M_Greybox_Gold");

            GameObject lag = MakeBlock(mirrorAnchor.transform, "LAGGING_REFLECTION",
                new Vector3(4.6f, 1.6f, 0f), new Vector3(0.08f, 1.7f, 0.9f), false, "M_Greybox_Blood");

            if (mirrors != null)
            {
                SetField(mirrors, "lockRef", locks.Count > 2 ? locks[2] as Component : null);
                SetArrayField(mirrors, "frames", frameList);
                SetArrayField(mirrors, "onRevealEnable", Two(lag, rune));
            }

            if (framesWired != 4)
            {
                Problems.Add("wired " + framesWired + " of 4 mirror frames");
            }
        }

        // ---- funhouse: three balloons and the chalkboard answer ------------
        GameObject balloonAnchor = FindDeep(root, LockAnchors[3]);
        int balloonsWired = 0;

        if (balloonAnchor == null)
        {
            Problems.Add("missing anchor " + LockAnchors[3] + " (balloons)");
        }
        else
        {
            balloons = AddOrGet(balloonAnchor, "CarnivalBalloonLock");

            bool rigCreated;
            GameObject rig = FindOrCreate(balloonAnchor.transform, "BALLOON_RIG", out rigCreated);
            rig.transform.localPosition = Vector3.zero;

            List<UnityEngine.Object> balloonList = new List<UnityEngine.Object>();
            Type pickupType = FindType("VRC_Pickup");

            for (int i = 0; i < 3; i++)
            {
                bool balloonCreated;
                GameObject balloon = FindOrCreate(rig.transform, "BALLOON_" + (i + 1), out balloonCreated);

                if (balloonCreated) balloon.transform.localPosition = new Vector3(-1.2f + i * 1.2f, 1.2f, 0f);

                // Skin, burst and swatches are visual only: a solid child in front
                // of the balloon would eat the interact ray.
                GameObject skin = MakeBlock(balloon.transform, "BALLOON_SKIN", Vector3.zero,
                    new Vector3(0.5f, 0.5f, 0.5f), false, "M_Greybox_Blood");
                skin.SetActive(true);

                GameObject burst = MakeBlock(balloon.transform, "BALLOON_POP", Vector3.zero,
                    new Vector3(0.7f, 0.7f, 0.7f), false, "M_Greybox_Acid");

                // Five swatches in a row: the lit position is the colour index,
                // so the greybox carries the code without pretending to be art.
                List<UnityEngine.Object> swatches = new List<UnityEngine.Object>();

                for (int c = 0; c < 5; c++)
                {
                    swatches.Add(MakeBlock(balloon.transform, "SWATCH_" + (c + 1),
                        new Vector3(-0.3f + c * 0.15f, -0.55f, 0f),
                        new Vector3(0.1f, 0.1f, 0.1f), false, "M_Greybox_Gold"));
                }

                BoxCollider reach = balloon.GetComponent<BoxCollider>();

                if (reach == null)
                {
                    reach = Undo.AddComponent<BoxCollider>(balloon);
                    reach.size = new Vector3(0.5f, 0.5f, 0.5f);
                    reach.center = Vector3.zero;
                }

                if (pickupType != null && balloon.GetComponent(pickupType) == null)
                {
                    Undo.AddComponent(balloon, pickupType);
                }

                Component balloonComponent = AddOrGet(balloon, "CarnivalBalloon");
                if (balloonComponent == null) continue;

                SetField(balloonComponent, "balloonIndex", i);
                SetField(balloonComponent, "balloonLock", balloons);
                SetField(balloonComponent, "skin", skin.transform);
                SetArrayField(balloonComponent, "colourSwatches", swatches);
                SetArrayField(balloonComponent, "onPopEnable", One(burst));

                balloonList.Add(balloonComponent);
                balloonsWired++;
            }

            GameObject confetti = MakeBlock(balloonAnchor.transform, "BALLOON_CONFETTI",
                new Vector3(0f, 2.6f, 0f), new Vector3(0.4f, 0.4f, 0.4f), false, "M_Greybox_Acid");

            GameObject warmerCard = MakeBlock(balloonAnchor.transform, "WARMER_CARD",
                new Vector3(2.4f, 1.5f, 0f), new Vector3(0.5f, 0.35f, 0.05f), false, "M_Greybox_Bone");

            if (balloons != null)
            {
                SetField(balloons, "lockRef", locks.Count > 3 ? locks[3] as Component : null);
                SetArrayField(balloons, "balloons", balloonList);
                SetArrayField(balloons, "onRevealEnable", Two(confetti, warmerCard));
            }

            if (balloonsWired != 3)
            {
                Problems.Add("wired " + balloonsWired + " of 3 balloons");
            }
        }

        // ---- sawdust ring: three barrels on a beam scale ------------------
        // The barrels weigh fixed amounts; the counterweight is the one thing a
        // guest adjusts, so the tolerance is what decides when the beam counts
        // as level. Socket markers are children of the beam, so they tilt too.
        GameObject scaleAnchor = FindDeep(root, LockAnchors[1]);
        int barrelsWired = 0;

        if (scaleAnchor == null)
        {
            Problems.Add("missing anchor " + LockAnchors[1] + " (barrels and scale)");
        }
        else
        {
            bool standCreated;
            GameObject stand = FindOrCreate(scaleAnchor.transform, "WEIGHT_SCALE", out standCreated);

            if (standCreated) stand.transform.localPosition = new Vector3(0f, 0f, -1.2f);

            // Interact needs a solid collider on the behaviour's own object, and
            // the stand is the part a guest actually reaches for.
            BoxCollider reach = stand.GetComponent<BoxCollider>();

            if (reach == null)
            {
                reach = Undo.AddComponent<BoxCollider>(stand);
                reach.size = new Vector3(1.1f, 1.2f, 0.9f);
                reach.center = new Vector3(0f, 0.6f, 0f);
            }

            GameObject pedestal = MakeBlock(stand.transform, "SCALE_PEDESTAL",
                new Vector3(0f, 0.55f, 0f), new Vector3(0.32f, 1.1f, 0.32f), false, "M_Greybox_Gold");
            pedestal.SetActive(true);

            bool beamCreated;
            GameObject beam = FindOrCreate(stand.transform, "SCALE_BEAM", out beamCreated);

            if (beamCreated) beam.transform.localPosition = new Vector3(0f, 1.18f, 0f);

            GameObject beamBar = MakeBlock(beam.transform, "BEAM_BAR", Vector3.zero,
                new Vector3(2.9f, 0.12f, 0.14f), false, "M_Greybox_Bone");
            beamBar.SetActive(true);

            // Step 0 sits at the home marker below; the component drives this
            // transform from its synced step, so the reset has one place to go
            // back to that the artist can move like any other prop.
            GameObject counterweight = MakeBlock(beam.transform, "SCALE_COUNTERWEIGHT",
                new Vector3(-0.2f, 0.12f, 0f), new Vector3(0.34f, 0.3f, 0.3f), false, "M_Greybox_Blood");
            counterweight.SetActive(true);

            bool cwHomeCreated;
            GameObject counterweightHome = FindOrCreate(beam.transform, "SCALE_CW_HOME", out cwHomeCreated);

            if (cwHomeCreated) counterweightHome.transform.localPosition = new Vector3(-0.2f, 0.12f, 0f);

            List<UnityEngine.Object> socketList = new List<UnityEngine.Object>();

            for (int i = 0; i < 3; i++)
            {
                bool socketCreated;
                GameObject socket = FindOrCreate(beam.transform, "SCALE_SOCKET_" + (i + 1), out socketCreated);

                if (socketCreated)
                {
                    socket.transform.localPosition = new Vector3(0.85f + i * 0.32f, 0.2f, 0f);
                }

                GameObject plate = MakeBlock(socket.transform, "SOCKET_PLATE", Vector3.zero,
                    new Vector3(0.26f, 0.04f, 0.26f), false, "M_Greybox_Acid");
                plate.SetActive(true);

                socketList.Add(socket.transform);
            }

            scalePuzzle = AddOrGet(stand, "CarnivalScaleLock");

            bool barrelGroupCreated;
            GameObject barrelGroup = FindOrCreate(scaleAnchor.transform, "WEIGHT_BARRELS", out barrelGroupCreated);
            barrelGroup.transform.localPosition = Vector3.zero;

            List<UnityEngine.Object> barrelList = new List<UnityEngine.Object>();

            // 18.4 + 22.0 + 19.3 = 59.7 kg on the pan. The counterweight steps
            // through 56.5, 57.5, 58.5, 59.5, 60.5 kg, so against a 0.5 kg
            // tolerance exactly one step balances — the puzzle is the carry, not
            // arithmetic nobody can do in a headset.
            float[] masses = new float[] { 18.4f, 22f, 19.3f };

            for (int i = 0; i < 3; i++)
            {
                bool barrelCreated;
                GameObject barrel = FindOrCreate(barrelGroup.transform, "BARREL_" + (i + 1), out barrelCreated);

                if (barrelCreated)
                {
                    barrel.transform.localPosition = new Vector3(-3f + i * 3f, 0.35f, 2.2f);
                }

                // The barrel's home for the reset. Kept off the pan and out of
                // the sockets' snap radius so a reset does not seat it for free.
                bool barrelHomeCreated;
                GameObject barrelHome = FindOrCreate(barrelGroup.transform, "BARREL_HOME_" + (i + 1), out barrelHomeCreated);

                if (barrelHomeCreated) barrelHome.transform.localPosition = barrel.transform.localPosition;

                // Body and band are visual only, so neither steals the interact ray.
                GameObject body = MakeBlock(barrel.transform, "BARREL_BODY", Vector3.zero,
                    new Vector3(0.5f, 0.7f, 0.5f), false, "M_Greybox_Bone");
                body.SetActive(true);

                GameObject band = MakeBlock(barrel.transform, "BARREL_BAND", new Vector3(0f, 0.1f, 0f),
                    new Vector3(0.54f, 0.1f, 0.54f), false, "M_Greybox_Gold");
                band.SetActive(true);

                BoxCollider grab = barrel.GetComponent<BoxCollider>();

                if (grab == null)
                {
                    grab = Undo.AddComponent<BoxCollider>(barrel);
                    grab.size = new Vector3(0.55f, 0.75f, 0.55f);
                    grab.center = new Vector3(0f, 0.1f, 0f);
                }

                Component barrelComponent = AddOrGet(barrel, "CarnivalBarrel");
                if (barrelComponent == null) continue;

                SetField(barrelComponent, "barrelIndex", i);
                SetField(barrelComponent, "mass", masses[i]);
                SetField(barrelComponent, "scale", scalePuzzle);
                SetField(barrelComponent, "roleBoard", roles);
                SetField(barrelComponent, "home", barrelHome.transform);

                barrelList.Add(barrelComponent);
                barrelsWired++;
            }

            GameObject mannequins = MakeBlock(scaleAnchor.transform, "RING_MANNEQUINS",
                new Vector3(0f, 1.6f, 3.4f), new Vector3(2.4f, 1f, 0.12f), false, "M_Greybox_Blood");

            if (scalePuzzle != null)
            {
                SetField(scalePuzzle, "lockRef", locks.Count > 1 ? locks[1] as Component : null);
                SetArrayField(scalePuzzle, "barrels", barrelList);
                SetArrayField(scalePuzzle, "sockets", socketList);
                SetField(scalePuzzle, "beam", beam.transform);
                SetField(scalePuzzle, "counterweight", counterweight.transform);
                SetField(scalePuzzle, "counterweightHome", counterweightHome.transform);
                SetArrayField(scalePuzzle, "onRevealEnable", One(mannequins));
                SetField(scalePuzzle, "tolerance", 0.5f);
                SetField(scalePuzzle, "settleSeconds", 1f);
            }

            if (barrelsWired != 3)
            {
                Problems.Add("wired " + barrelsWired + " of 3 barrels");
            }
        }

        // ---- exit gate: one barrier, one sign ----------------------------
        GameObject gateAnchor = FindDeep(root, "EXIT_GATE");
        GameObject gateBarrier = null;
        GameObject gateSign = null;
        GameObject finalMinuteSign = null;
        GameObject timeUpSign = null;

        if (gateAnchor != null)
        {
            // The doorway of 07_ExitGate is the -X wall, 2.6 m wide.
            gateBarrier = MakeBlock(gateAnchor.transform, "GATE_BARRIER",
                new Vector3(-3f, 1.6f, 0f), new Vector3(0.4f, 3.2f, 2.8f), true, "M_Greybox_Blood");

            gateSign = MakeBlock(gateAnchor.transform, "GATE_OPEN_SIGN",
                new Vector3(-3f, 3.4f, 0f), new Vector3(0.3f, 0.6f, 0.9f), false, "M_Greybox_Gold");

            finalMinuteSign = MakeBlock(gateAnchor.transform, "SIGN_FINAL_MINUTE",
                new Vector3(0f, 2.4f, -2.35f), new Vector3(1.2f, 0.4f, 0.12f), false, "M_Greybox_Acid");

            timeUpSign = MakeBlock(gateAnchor.transform, "SIGN_TIME_UP",
                new Vector3(0f, 1.8f, -2.35f), new Vector3(1.2f, 0.4f, 0.12f), false, "M_Greybox_Blood");
        }
        else
        {
            Problems.Add("missing anchor EXIT_GATE");
        }

        // ---- the twelve teeth: the hidden ending's whole condition --------
        // Kept outside the finale block on purpose: a scene with no CarnivalFinale
        // can still have collectibles, and the ledger is harmless on its own.
        Component teethLedger = AddOrGet(systems, "CarnivalTeeth");
        int teethWired = 0;

        if (teethLedger != null)
        {
            SetField(teethLedger, "requiredTeeth", 12);

            List<UnityEngine.Object> toothList = new List<UnityEngine.Object>();

            for (int i = 0; i < ToothAnchors.Length; i++)
            {
                GameObject toothHost = FindDeep(root, ToothAnchors[i]);

                if (toothHost == null)
                {
                    Problems.Add("missing anchor " + ToothAnchors[i] + " (tooth " + (i + 1) + ")");
                    continue;
                }

                // Solid, because Interact needs a collider on the prop itself. The
                // tooth disables itself when found, which is the whole reveal.
                GameObject tooth = MakeBlock(toothHost.transform, "TOOTH_" + (i + 1).ToString("00"),
                    ToothOffsets[i], new Vector3(0.16f, 0.16f, 0.16f), true, "M_Greybox_Bone");

                Component toothComponent = AddOrGet(tooth, "CarnivalTooth");
                if (toothComponent == null) continue;

                SetField(toothComponent, "toothIndex", i);
                SetField(toothComponent, "teeth", teethLedger);
                SetArrayField(toothComponent, "onFoundDisable", One(tooth));
                SetArrayField(toothComponent, "onFoundEnable", new List<UnityEngine.Object>());

                toothList.Add(toothComponent);
                teethWired++;
            }

            SetArrayField(teethLedger, "teeth", toothList);
            SetArrayField(teethLedger, "onAllFoundEnable", new List<UnityEngine.Object>());

            if (teethWired != ToothAnchors.Length)
            {
                Problems.Add("wired " + teethWired + " of " + ToothAnchors.Length + " teeth");
            }
        }

        // ---- finale: three beats instead of one switch --------------------
        // The manager keeps holding the gate as a fallback. When this component
        // is wired the manager defers to it, so nothing opens in the same frame
        // the last lock falls.
        Component finale = AddOrGet(systems, "CarnivalFinale");

        if (finale != null)
        {
            // Grinny, on the perch, invisible until he bows.
            GameObject perch = FindDeep(root, "GRINNY_PERCH");
            Transform bowParent = perch != null ? perch.transform : systems.transform;

            GameObject bowProp = MakeBlock(bowParent, "GRINNY_BOW",
                new Vector3(0f, -2.4f, 0f), new Vector3(0.9f, 2f, 0.5f), false, "M_Greybox_Blood");

            GameObject bowSpot = MakeBlock(bowParent, "BOW_SPOT",
                new Vector3(0f, -0.6f, 0f), new Vector3(1.6f, 0.08f, 1.6f), false, "M_Greybox_Gold");

            // Four practicals in the ring: the only thing in the greybox that
            // reads as "the lights are on", so the blackout has something to cut.
            GameObject ringAnchorForLights = FindDeep(root, LockAnchors[1]);
            Transform lightParent = ringAnchorForLights != null ? ringAnchorForLights.transform : systems.transform;
            List<UnityEngine.Object> houseLights = new List<UnityEngine.Object>();

            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f;

                houseLights.Add(MakeBlock(lightParent, "HOUSE_LIGHT_" + i,
                    new Vector3(Mathf.Cos(angle) * 6f, 5.2f, Mathf.Sin(angle) * 6f),
                    new Vector3(0.7f, 0.3f, 0.7f), true, "M_Greybox_Bone"));
            }

            SetField(finale, "manager", manager);
            SetField(finale, "bowDelay", 2.2f);
            SetField(finale, "gateDelay", 5f);
            SetArrayField(finale, "onBlackoutEnable", new List<UnityEngine.Object>());
            SetArrayField(finale, "onBlackoutDisable", houseLights);
            SetArrayField(finale, "onBowEnable", Two(bowProp, bowSpot));
            SetArrayField(finale, "onBowDisable", new List<UnityEngine.Object>());
            SetArrayField(finale, "onGateOpenEnable", One(gateSign));
            SetArrayField(finale, "onGateOpenDisable", One(gateBarrier));

            // ---- three endings, one visible at a time ---------------------
            // Each overlay lives where its ending belongs: dawn past the gate, a
            // spot on the ring, a deed at the booth and a wing in the hideout.
            GameObject endingGate = FindDeep(root, "EXIT_GATE");
            GameObject endingRing = FindDeep(root, LockAnchors[5]);
            GameObject endingBooth = FindDeep(root, LockAnchors[0]);
            GameObject endingHideout = FindDeep(root, "06_Hideout");

            GameObject endingDawn = endingGate != null
                ? MakeBlock(endingGate.transform, "ENDING_DAWN", new Vector3(-4.6f, 1.6f, 0f),
                    new Vector3(0.2f, 3f, 2.6f), false, "M_Greybox_Bone")
                : null;

            GameObject endingSpot = endingRing != null
                ? MakeBlock(endingRing.transform, "ENDING_UNDERSTUDY_SPOT", new Vector3(0f, 3.1f, 0f),
                    new Vector3(1.4f, 0.12f, 1.4f), false, "M_Greybox_Gold")
                : null;

            GameObject endingDeed = endingBooth != null
                ? MakeBlock(endingBooth.transform, "ENDING_DEED_PLATE", new Vector3(0f, 1.5f, 0.86f),
                    new Vector3(0.5f, 0.34f, 0.04f), false, "M_Greybox_Gold")
                : null;

            GameObject endingWing = endingHideout != null
                ? MakeBlock(endingHideout.transform, "ENDING_OWNER_WING", new Vector3(3.2f, 2.2f, 1.2f),
                    new Vector3(0.6f, 0.4f, 0.2f), false, "M_Greybox_Gold")
                : null;

            SetField(finale, "teeth", teethLedger);
            SetField(finale, "curtain", curtainPuzzle);
            SetArrayField(finale, "onCleanEnable", One(endingDawn));
            SetArrayField(finale, "onUnderstudyEnable", One(endingSpot));
            SetArrayField(finale, "onOwnerEnable", Two(endingDeed, endingWing));

            if (endingDawn == null) Problems.Add("the clean break has no dawn to show");
            if (endingSpot == null) Problems.Add("the understudy ending has no spot to light");
            if (endingDeed == null && endingWing == null) Problems.Add("the hidden ending reveals nothing");

            if (manager != null) SetField(manager, "finale", finale);

            if (gateSign == null || gateBarrier == null)
            {
                Problems.Add("the finale has no gate to open");
            }
        }

        if (manager != null)
        {
            // Fallback only: used when no CarnivalFinale is present.
            SetArrayField(manager, "onShowCompleteEnable", One(gateSign));
            SetArrayField(manager, "onShowCompleteDisable", One(gateBarrier));
        }

        // ---- clock -------------------------------------------------------
        if (timer != null)
        {
            SetField(timer, "showLengthSeconds", 1500f);

            // Started by the gate line, not on instance join.
            SetField(timer, "autoStart", false);
            SetField(timer, "manager", manager);

            SetArrayField(timer, "onFinalMinuteEnable", One(finalMinuteSign));
            SetArrayField(timer, "onTimeUpEnable", One(timeUpSign));
        }

        // ---- surprise economy -------------------------------------------
        if (surprises != null)
        {
            List<UnityEngine.Object> beats = new List<UnityEngine.Object>();

            for (int i = 0; i < BeatAnchors.Length; i++)
            {
                GameObject beat = FindDeep(root, BeatAnchors[i]);

                if (beat == null) Problems.Add("missing anchor " + BeatAnchors[i]);
                else beats.Add(beat);
            }

            SetArrayField(surprises, "scriptedBeats", beats);
        }

        // ---- four role cards on the hideout board ------------------------
        GameObject boardAnchor = FindDeep(root, "ROLE_BOARD");

        if (boardAnchor != null && roles != null)
        {
            List<UnityEngine.Object> cards = new List<UnityEngine.Object>();

            for (int i = 0; i < 4; i++)
            {
                cards.Add(MakeBlock(boardAnchor.transform, "RoleCard_" + i,
                    new Vector3(-0.9f + i * 0.6f, 0f, 0f), new Vector3(0.5f, 0.35f, 0.05f),
                    false, "M_Greybox_Acid"));
            }

            SetArrayField(roles, "roleMarkers", cards);
        }
        else
        {
            Problems.Add("missing anchor ROLE_BOARD");
        }

        // ---- the gate line that starts the clock -------------------------
        // Positioned only when created: a re-run must not undo a placement you tuned.
        bool gateLineCreated;
        GameObject gateLine = FindOrCreate(root.transform, "GATE_LINE", out gateLineCreated);

        if (gateLineCreated)
        {
            gateLine.transform.position = new Vector3(0f, 1.5f, -5f);
        }

        BoxCollider trigger = gateLine.GetComponent<BoxCollider>();

        if (trigger == null)
        {
            trigger = Undo.AddComponent<BoxCollider>(gateLine);
            trigger.size = new Vector3(6f, 3f, 0.5f);
        }

        trigger.isTrigger = true;

        Component starter = AddOrGet(gateLine, "CarnivalShowStarter");

        if (starter != null)
        {
            SetField(starter, "timer", timer);
            SetField(starter, "surprises", surprises);
            SetField(starter, "teeth", teethLedger);
        }

        // ---- the next show: one lever that puts the run back -------------
        // The finale leaves a solved world behind by design. This is the only
        // thing that undoes it, and it is a lever rather than a trigger because
        // a finished show and a party talking look identical from a trigger.
        GameObject hideout = FindDeep(root, "06_Hideout");
        Transform leverParent = hideout != null ? hideout.transform : systems.transform;

        GameObject resetLever = MakeBlock(leverParent, "RUN_RESET_LEVER",
            new Vector3(-3.4f, 1.3f, 1.6f), new Vector3(0.5f, 0.35f, 0.2f), true, "M_Greybox_Blood");

        Component runReset = AddOrGet(resetLever, "CarnivalRunReset");

        if (runReset != null)
        {
            SetField(runReset, "manager", manager);
            SetField(runReset, "finale", finale);
            SetField(runReset, "timer", timer);
            SetField(runReset, "surprises", surprises);
            SetField(runReset, "teeth", teethLedger);
            SetField(runReset, "starter", starter);
            SetField(runReset, "ticketLock", tickets);
            SetField(runReset, "scaleLock", scalePuzzle);
            SetField(runReset, "mirrorLock", mirrors);
            SetField(runReset, "balloonLock", balloons);
            SetField(runReset, "boilerLock", boiler);
            SetField(runReset, "curtainLock", curtainPuzzle);

            // Read it back. A lever is only useful if every instance on it is the
            // instance the rest of the rig uses; a second director, clock or teeth
            // ledger sitting in the scene is invisible in the hierarchy and turns
            // one of these resets into a no-op on a copy nobody reads.
            VerifyLeverTargets(runReset);
        }

        // ---- done --------------------------------------------------------
        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = systems;
        EditorGUIUtility.PingObject(systems);

        string report = "[Carnival] Show rig wired: " +
                        locks.Count + " locks, " + curtainMarks.Count + " curtain marks, " +
                        valvesWired + " valves, " + stubsWired + " stubs, " + framesWired + " mirror frames, " +
                        balloonsWired + " balloons, " + barrelsWired + " barrels, " + teethWired + " teeth, " +
                        "clock, role board, surprise budget, exit gate, finale, three endings, gate line, reset lever.";

        Debug.Log(report + "\n" +
                  "  Scene is playable: enter Play mode, or upload straight to a private test world.\n" +
                  "  The clock starts when a guest crosses GATE_LINE into the midway (line at z = -5).\n" +
                  "  requiredMask = 63 (all six locks). Use 31 to test the five puzzle locks only,\n" +
                  "  32 curtain, 16 boiler, 8 balloons, 4 mirrors, 2 weight, 1 booth. 63 = the full run.\n" +
                  "  Finale: lights out, Grinny bows at 2.2 s, gate opens at 5 s (CarnivalFinale on _Systems).\n" +
                  "  Endings: clean break by default; understudy if exactly one guest holds CURTAIN_CENTRE_MARK\n" +
                  "  at the gate beat; buy the carnival if all 12 teeth are found (CarnivalTeeth on _Systems).\n" +
                  "  Reset: pull RUN_RESET_LEVER in the hideout to clear the whole run for the next party —\n" +
                  "  the stubs, the three barrels and the counterweight go back to their home markers.\n" +
                  (Problems.Count > 0
                      ? "  Could not do: " + string.Join("; ", Problems.ToArray())
                      : "  Nothing missing — every anchor was found and every reference was set."));

        if (Problems.Count > 0)
        {
            Debug.LogWarning("[Carnival] Wiring finished with " + Problems.Count + " problem(s). " +
                             "Run Carnival World > 1. Build Greybox first if anchors are missing.");
        }
    }

    /// <summary>
    /// One-click path for an empty scene: build, apply the Quest lighting preset, wire.
    /// </summary>
    [MenuItem("Carnival World/0. New Playable Greybox", false, 0)]
    public static void NewPlayableGreybox()
    {
        CarnivalGreyboxBuilder.BuildGreybox();
        CarnivalGreyboxBuilder.ApplyQuestLighting();
        WireShowRig();
    }

    // ---------------------------------------------------------------- lookup

    private static GameObject FindOrCreate(Transform parent, string name, out bool created)
    {
        Transform existing = parent.Find(name);

        if (existing != null)
        {
            created = false;
            return existing.gameObject;
        }

        GameObject fresh = new GameObject(name);
        fresh.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(fresh, "Create " + name);

        created = true;
        return fresh;
    }

    internal static GameObject FindDeep(GameObject root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == name) return all[i].gameObject;
        }

        return null;
    }

    private static Component AddOrGet(GameObject host, string typeName)
    {
        Type type = FindType(typeName);

        if (type == null)
        {
            Problems.Add("no component type named " + typeName +
                         " (is UdonSharp installed and the scripts compiled?)");
            return null;
        }

        Component existing = host.GetComponent(type);

        if (existing != null) return existing;

        return Undo.AddComponent(host, type);
    }

    internal static Type FindType(string simpleName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        for (int a = 0; a < assemblies.Length; a++)
        {
            Type[] types;

            try
            {
                types = assemblies[a].GetTypes();
            }
            catch (Exception)
            {
                continue;
            }

            for (int t = 0; t < types.Length; t++)
            {
                if (types[t].Name == simpleName) return types[t];
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- wiring

    private static void SetField(Component target, string fieldName, object value)
    {
        if (target == null) return;

        FieldInfo field = target.GetType().GetField(fieldName,
            BindingFlags.Public | BindingFlags.Instance);

        if (field == null)
        {
            Problems.Add(target.GetType().Name + " has no field '" + fieldName + "'");
            return;
        }

        Undo.RecordObject(target, "Wire " + fieldName);

        try
        {
            field.SetValue(target, value);
        }
        catch (Exception error)
        {
            Problems.Add(target.GetType().Name + "." + fieldName + " (" + error.GetType().Name + ")");
        }

        EditorUtility.SetDirty(target);
    }

    private static void SetArrayField(Component target, string fieldName, List<UnityEngine.Object> items)
    {
        if (target == null) return;

        FieldInfo field = target.GetType().GetField(fieldName,
            BindingFlags.Public | BindingFlags.Instance);

        if (field == null || !field.FieldType.IsArray)
        {
            Problems.Add(target.GetType().Name + " has no array field '" + fieldName + "'");
            return;
        }

        Type element = field.FieldType.GetElementType();
        Array array = Array.CreateInstance(element, items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            array.SetValue(items[i], i);
        }

        Undo.RecordObject(target, "Wire " + fieldName);

        try
        {
            field.SetValue(target, array);
        }
        catch (Exception error)
        {
            Problems.Add(target.GetType().Name + "." + fieldName + " (" + error.GetType().Name + ")");
        }

        EditorUtility.SetDirty(target);
    }

    // ------------------------------------------------------- lever read-back

    /// <summary>
    /// Every system on the reset lever has to be the instance the rest of the rig
    /// points at. SetField writes what this tool intends; this reads it back and
    /// compares it against the referrers the rig already carries, so a lever aimed
    /// at a second director, clock, finale or teeth ledger is reported instead of
    /// clearing a copy that nothing else reads — which looks exactly like a reset
    /// that worked until the second party notices what survived.
    ///
    /// Reports only: which instance is the real one is a decision for the person
    /// who built the scene, not for the tool.
    /// </summary>
    private static void VerifyLeverTargets(Component lever)
    {
        if (lever == null) return;

        string[] systems = { "manager", "finale", "timer", "surprises", "teeth", "starter" };

        for (int i = 0; i < systems.Length; i++)
        {
            FieldInfo field = lever.GetType().GetField(systems[i], BindingFlags.Public | BindingFlags.Instance);

            // A field that does not exist is already reported by SetField.
            if (field == null) continue;

            if (field.GetValue(lever) == null)
            {
                Problems.Add("the reset lever's " + systems[i] + " is empty, so that part of the run is never cleared");
            }
        }

        Component manager = Ref(lever, "manager");
        Component finale = Ref(lever, "finale");
        Component timer = Ref(lever, "timer");
        Component surprises = Ref(lever, "surprises");
        Component teeth = Ref(lever, "teeth");
        Component starter = Ref(lever, "starter");

        SameInstance("the gate line's clock", timer, Ref(starter, "timer"));
        SameInstance("the gate line's scare budget", surprises, Ref(starter, "surprises"));
        SameInstance("the boiler's scare budget", surprises, Ref(Ref(lever, "boilerLock"), "surprises"));
        SameInstance("the lock manager's finale", finale, Ref(manager, "finale"));
        SameInstance("the gate line's teeth", teeth, Ref(starter, "teeth"));
        SameInstance("the finale's teeth", teeth, Ref(finale, "teeth"));
    }

    /// <summary>
    /// Two references to the same system, one of them the lever's. Equal is the
    /// only passing answer: two instances of a director or a clock are two
    /// separate runs' worth of state.
    /// </summary>
    private static void SameInstance(string what, Component leverTarget, Component rigTarget)
    {
        if (leverTarget == null || rigTarget == null) return;
        if (leverTarget == rigTarget) return;

        Problems.Add("the reset lever clears " + Describe(leverTarget) + " but " + what + " uses " +
                     Describe(rigTarget) + ", so that part of the run is reset on a copy nothing reads");
    }

    private static string Describe(Component component)
    {
        return component.GetType().Name + " on " + component.gameObject.name;
    }

    private static Component Ref(Component target, string fieldName)
    {
        if (target == null) return null;

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);

        return field == null ? null : field.GetValue(target) as Component;
    }

    private static List<UnityEngine.Object> One(UnityEngine.Object item)
    {
        List<UnityEngine.Object> list = new List<UnityEngine.Object>();

        if (item != null) list.Add(item);

        return list;
    }

    private static List<UnityEngine.Object> Two(UnityEngine.Object first, UnityEngine.Object second)
    {
        List<UnityEngine.Object> list = One(first);

        if (second != null) list.Add(second);

        return list;
    }

    /// <summary>
    /// A box marker. Inactive markers lose their collider so they never block a
    /// guest; the gate barrier keeps its collider because blocking is the point.
    /// </summary>
    private static GameObject MakeBlock(Transform parent, string name, Vector3 localPosition,
                                        Vector3 size, bool active, string materialName)
    {
        Transform existing = parent.Find(name);
        GameObject block;

        if (existing != null)
        {
            block = existing.gameObject;
        }
        else
        {
            block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(block, "Create " + name);
        }

        block.transform.localPosition = localPosition;
        block.transform.localRotation = Quaternion.identity;
        block.transform.localScale = size;

        Renderer renderer = block.GetComponent<Renderer>();
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/" + materialName + ".mat");

        if (renderer != null && material != null) renderer.sharedMaterial = material;

        if (!active)
        {
            Collider collider = block.GetComponent<Collider>();

            if (collider != null) Undo.DestroyObjectImmediate(collider);
        }

        block.SetActive(active);

        return block;
    }
}
#endif
