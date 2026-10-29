#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Measures the open scene against the Quest 3 budget from the design deck.
///
/// Deliberately references no VRChat types: mirrors are detected by type name
/// so the tool still works before the SDK is installed.
///
/// Menu: Carnival World > 3. Quest Budget Report
/// </summary>
public static class CarnivalBudgetCheck
{
    private const int TriangleBudget = 250000;
    private const int MaterialBudget = 60;
    private const int AudioSourceBudget = 9;
    private const int RealtimeLightBudget = 1;
    private const int MirrorBudget = 1;

    [MenuItem("Carnival World/3. Quest Budget Report", false, 3)]
    public static void Report()
    {
        long triangles = 0;
        int meshCount = 0;
        int renderers = 0;
        int lights = 0;
        int realtimeLights = 0;
        int audioSources = 0;
        int particles = 0;
        int rigidbodies = 0;
        int mirrors = 0;

        HashSet<Material> materials = new HashSet<Material>();

        foreach (MeshFilter filter in Object.FindObjectsOfType<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;

            triangles += filter.sharedMesh.triangles.Length / 3;
            meshCount++;
        }

        foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>(true))
        {
            if (renderer == null) continue;

            renderers++;

            Material[] mats = renderer.sharedMaterials;

            if (mats == null) continue;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null) materials.Add(mats[i]);
            }
        }

        foreach (Light light in Object.FindObjectsOfType<Light>(true))
        {
            lights++;

            if (light.lightmapBakeType == LightmapBakeType.Realtime ||
                light.lightmapBakeType == LightmapBakeType.Mixed)
            {
                realtimeLights++;
            }
        }

        audioSources = Object.FindObjectsOfType<AudioSource>(true).Length;
        particles = Object.FindObjectsOfType<ParticleSystem>(true).Length;
        rigidbodies = Object.FindObjectsOfType<Rigidbody>(true).Length;

        foreach (Component component in Object.FindObjectsOfType<Component>(true))
        {
            if (component == null) continue;

            string typeName = component.GetType().Name;

            if (typeName == "VRCMirrorReflection" || typeName.Contains("Mirror"))
            {
                mirrors++;
            }
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[Carnival] Quest budget report for scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        sb.AppendLine("  triangles      " + triangles + " / " + TriangleBudget + "  " + Verdict(triangles, TriangleBudget));
        sb.AppendLine("  materials      " + materials.Count + " / " + MaterialBudget + "  " + Verdict(materials.Count, MaterialBudget));
        sb.AppendLine("  mesh filters   " + meshCount + " across " + renderers + " renderers");
        sb.AppendLine("  lights         " + lights + " total, " + realtimeLights + " realtime/mixed  " + Verdict(realtimeLights, RealtimeLightBudget));
        sb.AppendLine("  mirrors        " + mirrors + " / " + MirrorBudget + "  " + Verdict(mirrors, MirrorBudget));
        sb.AppendLine("  audio sources  " + audioSources + " / " + AudioSourceBudget + "  " + Verdict(audioSources, AudioSourceBudget));
        sb.AppendLine("  particles      " + particles + " (Quest: keep at 4 or fewer)");
        sb.AppendLine("  rigidbodies    " + rigidbodies + " (world target: 0)");
        sb.AppendLine("  Download size is not measured here — check the build statistics in the SDK Build panel.");

        if (triangles > TriangleBudget)
        {
            sb.AppendLine("  FIX: atlas materials, merge mannequins, decimate hero props, then re-run.");
        }

        Debug.Log(sb.ToString());

        EditorUtility.DisplayDialog(
            "Carnival World — Quest budget",
            "Triangles: " + triangles + " / " + TriangleBudget + "\n" +
            "Materials: " + materials.Count + " / " + MaterialBudget + "\n" +
            "Realtime lights: " + realtimeLights + " / " + RealtimeLightBudget + "\n" +
            "Mirrors: " + mirrors + " / " + MirrorBudget + "\n" +
            "Audio sources: " + audioSources + " / " + AudioSourceBudget + "\n\n" +
            "Full report in the Console.",
            "Close");
    }

    private static string Verdict(long value, long budget)
    {
        if (value <= budget * 0.85f) return "OK";
        if (value <= budget) return "TIGHT";
        return "OVER";
    }
}
#endif
