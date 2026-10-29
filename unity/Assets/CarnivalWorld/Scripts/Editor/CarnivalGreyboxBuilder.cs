#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the greybox for Midnight Midway from code so the layout in the design
/// deck can be walked in VR before a single art asset exists.
///
/// Deliberately references no UdonSharp or VRChat types: it works in a fresh
/// project the moment the package is imported, and it never blocks compilation
/// if the VRChat SDK is not installed yet.
///
/// Menu: Carnival World > 1. Build Greybox
/// </summary>
public static class CarnivalGreyboxBuilder
{
    private const string RootName = "CarnivalWorld_Greybox";
    private const string MatFolder = "Assets/CarnivalWorld/Greybox/Materials";

    private static Material _bone;
    private static Material _blood;
    private static Material _gold;
    private static Material _acid;

    [MenuItem("Carnival World/1. Build Greybox", false, 1)]
    public static void BuildGreybox()
    {
        GameObject existing = GameObject.Find(RootName);

        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "Carnival World",
                "A greybox already exists in this scene. Rebuild it?\n\nThis deletes " + RootName + " and its children.",
                "Rebuild",
                "Cancel");

            if (!replace) return;
            Object.DestroyImmediate(existing);
        }

        EnsureMaterialFolder();
        _bone = MakeMaterial("M_Greybox_Bone", new Color(0.85f, 0.81f, 0.71f));
        _blood = MakeMaterial("M_Greybox_Blood", new Color(0.55f, 0.09f, 0.11f));
        _gold = MakeMaterial("M_Greybox_Gold", new Color(0.75f, 0.58f, 0.22f));
        _acid = MakeMaterial("M_Greybox_Acid", new Color(0.34f, 0.72f, 0.22f));

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Carnival Greybox");

        // ---- zones, in metres, aligned with the floor plan ----
        CreateRoom(root.transform, "00_TicketBooth", new Vector3(0f, 0f, -9f), 6f, 5f, 4f, Vector3.back, _blood);
        CreateRoom(root.transform, "01_Midway", new Vector3(0f, 0f, 0f), 40f, 8f, 6f, Vector3.back, _bone);
        CreateBigTop(root.transform, "02_BigTop", new Vector3(0f, 0f, 22f), 13f, 12f);
        CreateRoom(root.transform, "03_HallOfMirrors", new Vector3(-24f, 0f, 20f), 12f, 14f, 5f, Vector3.right, _bone);
        CreateRoom(root.transform, "04_Funhouse", new Vector3(24f, 0f, 20f), 14f, 14f, 5f, Vector3.left, _acid);
        CreateRoom(root.transform, "05_BackstageBoiler", new Vector3(24f, 0f, 8f), 10f, 8f, 4f, Vector3.left, _blood);
        CreateRoom(root.transform, "06_Hideout", new Vector3(-24f, 0f, -8f), 9f, 6f, 3f, Vector3.right, _gold);
        CreateRoom(root.transform, "07_ExitGate", new Vector3(24f, 0f, -14f), 6f, 5f, 5f, Vector3.left, _gold);

        // ---- anchors that the UdonSharp components get attached to ----
        Transform anchors = new GameObject("_Anchors").transform;
        anchors.SetParent(root.transform, false);

        CreateAnchor(anchors, "SPAWN", new Vector3(0f, 0f, -12f));
        CreateAnchor(anchors, "LOCK_01_TicketTear", new Vector3(0f, 0f, -9f));
        CreateAnchor(anchors, "LOCK_02_WeightOfTheShow", new Vector3(0f, 0f, 22f));
        CreateAnchor(anchors, "LOCK_03_MirrorsAside", new Vector3(-24f, 0f, 20f));
        CreateAnchor(anchors, "LOCK_04_BalloonLogic", new Vector3(24f, 0f, 20f));
        CreateAnchor(anchors, "LOCK_05_BoilerLullaby", new Vector3(24f, 0f, 8f));
        CreateAnchor(anchors, "LOCK_06_Curtain", new Vector3(0f, 0f, 25f));
        CreateAnchor(anchors, "GRINNY_PERCH", new Vector3(0f, 3.4f, 20f));
        CreateAnchor(anchors, "ROLE_BOARD", new Vector3(-24f, 1.4f, -9.6f));
        CreateAnchor(anchors, "HIDEOUT_DOORLINE", new Vector3(-19.4f, 0f, -8f));
        CreateAnchor(anchors, "EXIT_GATE", new Vector3(24f, 0f, -14f));

        // ---- the three scripted surprise beats from the deck ----
        Transform beats = new GameObject("_ScriptedBeats").transform;
        beats.SetParent(root.transform, false);
        CreateAnchor(beats, "BEAT_1_AudienceTurns", new Vector3(0f, 1.5f, 22f));
        CreateAnchor(beats, "BEAT_2_ReflectionLags", new Vector3(-24f, 1.6f, 20f));
        CreateAnchor(beats, "BEAT_3_GrinnyBows", new Vector3(0f, 1.2f, 25f));

        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log("[Carnival] Greybox built: 8 zones, 12 anchors, 3 scripted beats. " +
                  "Next: Carnival World > 3. Quest Budget Report, then attach the UdonSharp components to the anchors.");
    }

    [MenuItem("Carnival World/2. Apply Quest Lighting Preset", false, 2)]
    public static void ApplyQuestLighting()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.12f, 0.12f);
        RenderSettings.fog = false;
        RenderSettings.skybox = null;

        Camera main = Camera.main;

        if (main != null)
        {
            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = new Color(0.04f, 0.025f, 0.03f);
            main.farClipPlane = 120f;
        }

        Debug.Log("[Carnival] Quest lighting preset applied: flat ambient, no fog, no skybox. " +
                  "Bake lighting with Progressive CPU before testing on device.");
    }

    // ---------------------------------------------------------------- helpers

    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/CarnivalWorld/Greybox"))
        {
            AssetDatabase.CreateFolder("Assets/CarnivalWorld", "Greybox");
        }

        if (!AssetDatabase.IsValidFolder(MatFolder))
        {
            AssetDatabase.CreateFolder("Assets/CarnivalWorld/Greybox", "Materials");
        }
    }

    private static Material MakeMaterial(string name, Color color)
    {
        string path = MatFolder + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null) return existing;

        Shader shader = Shader.Find("VRChat/Mobile/Lightmapped");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.name = name;

        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        material.color = color;

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void CreateRoom(Transform parent, string name, Vector3 center,
                                   float width, float depth, float height,
                                   Vector3 doorDirection, Material material)
    {
        GameObject room = new GameObject(name);
        room.transform.SetParent(parent, false);
        room.transform.position = center;

        const float thickness = 0.3f;
        Transform t = room.transform;

        // floor
        CreateBox(t, "Floor", new Vector3(center.x, center.y - thickness * 0.5f, center.z),
                  new Vector3(width, thickness, depth), material, true);

        // four walls, with a 2.6 m doorway on the requested side
        CreateWall(t, center, new Vector3(1, 0, 0), width, depth, height, doorDirection, material);
        CreateWall(t, center, new Vector3(-1, 0, 0), width, depth, height, doorDirection, material);
        CreateWall(t, center, new Vector3(0, 0, 1), width, depth, height, doorDirection, material);
        CreateWall(t, center, new Vector3(0, 0, -1), width, depth, height, doorDirection, material);
    }

    private static void CreateWall(Transform room, Vector3 center, Vector3 normal,
                                   float width, float depth, float height,
                                   Vector3 doorDirection, Material material)
    {
        bool horizontal = Mathf.Abs(normal.z) > 0.5f;
        float span = horizontal ? width : depth;

        Vector3 wallCenter = center + new Vector3(
            normal.x * (width * 0.5f),
            height * 0.5f,
            normal.z * (depth * 0.5f));

        Vector3 wallSize = horizontal
            ? new Vector3(width, height, 0.3f)
            : new Vector3(0.3f, height, depth);

        bool isDoorWall = Vector3.Dot(normal, doorDirection) > 0.5f;

        if (!isDoorWall)
        {
            CreateBox(room, "Wall", wallCenter, wallSize, material, true);
            return;
        }

        // split into two segments with a 2.6 m gap in the middle
        const float gap = 2.6f;
        float segment = (span - gap) * 0.5f;

        if (segment <= 0.1f) return;

        float offset = gap * 0.5f + segment * 0.5f;

        if (horizontal)
        {
            CreateBox(room, "Wall_L", wallCenter + new Vector3(-offset, 0f, 0f),
                      new Vector3(segment, height, 0.3f), material, true);
            CreateBox(room, "Wall_R", wallCenter + new Vector3(offset, 0f, 0f),
                      new Vector3(segment, height, 0.3f), material, true);
        }
        else
        {
            CreateBox(room, "Wall_L", wallCenter + new Vector3(0f, 0f, -offset),
                      new Vector3(0.3f, height, segment), material, true);
            CreateBox(room, "Wall_R", wallCenter + new Vector3(0f, 0f, offset),
                      new Vector3(0.3f, height, segment), material, true);
        }
    }

    private static void CreateBigTop(Transform parent, string name, Vector3 center,
                                     float radius, float height)
    {
        GameObject tent = new GameObject(name);
        tent.transform.SetParent(parent, false);
        tent.transform.position = center;

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        floor.name = "RingFloor";
        floor.transform.SetParent(tent.transform, false);
        floor.transform.position = center + new Vector3(0f, -0.15f, 0f);
        floor.transform.localScale = new Vector3(radius * 2f, 0.15f, radius * 2f);
        floor.GetComponent<Renderer>().sharedMaterial = _bone;
        MakeStatic(floor, true, true);

        // eight tent panels around the ring
        const int panels = 8;

        for (int i = 0; i < panels; i++)
        {
            float angle = (360f / panels) * i;
            float rad = angle * Mathf.Deg2Rad;
            Vector3 pos = center + new Vector3(Mathf.Sin(rad) * radius, height * 0.5f, Mathf.Cos(rad) * radius);

            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "TentPanel_" + i;
            panel.transform.SetParent(tent.transform, false);
            panel.transform.position = pos;
            panel.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            panel.transform.localScale = new Vector3(radius * 0.82f, height, 0.4f);
            panel.GetComponent<Renderer>().sharedMaterial = (i % 2 == 0) ? _blood : _bone;
            MakeStatic(panel, true, false);
        }

        GameObject centrePole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        centrePole.name = "CentrePole_Chimney";
        centrePole.transform.SetParent(tent.transform, false);
        centrePole.transform.position = center + new Vector3(0f, height * 0.5f, 0f);
        centrePole.transform.localScale = new Vector3(1.2f, height * 0.5f, 1.2f);
        centrePole.GetComponent<Renderer>().sharedMaterial = _gold;
        MakeStatic(centrePole, false, false);
    }

    private static void CreateBox(Transform parent, string name, Vector3 center,
                                  Vector3 size, Material material, bool occluder)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.position = center;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        MakeStatic(box, true, occluder);
    }

    private static void CreateAnchor(Transform parent, string name, Vector3 position)
    {
        GameObject anchor = new GameObject(name);
        anchor.transform.SetParent(parent, false);
        anchor.transform.position = position;
    }

    private static void MakeStatic(GameObject go, bool contributeGI, bool occluder)
    {
        StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;

        if (contributeGI) flags |= StaticEditorFlags.ContributeGI;
        if (occluder) flags |= StaticEditorFlags.OccluderStatic;

        GameObjectUtility.SetStaticEditorFlags(go, flags);
    }
}
#endif
