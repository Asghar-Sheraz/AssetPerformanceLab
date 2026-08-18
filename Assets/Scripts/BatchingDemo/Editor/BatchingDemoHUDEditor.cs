using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The control panel for the demo: pick a target, press a mode, press Play.
///
/// Buttons only work with Play stopped — applying a mode is an edit-time setup so Unity can do its real
/// load-time batching when Play starts. The live readout underneath is read back from Unity, never from
/// the button that was pressed.
/// </summary>
[CustomEditor(typeof(BatchingDemoHUD))]
public class BatchingDemoHUDEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var hud = (BatchingDemoHUD)target;

        EditorGUILayout.Space(10);

        if (hud.profile == null)
        {
            EditorGUILayout.HelpBox("Assign a BatchingDemoProfile to choose modes.", MessageType.Warning);
            if (GUILayout.Button("Create Profile With The Five Default Modes", GUILayout.Height(26f)))
                CreateProfile(hud);
            return;
        }

        if (hud.cubeGroup == null || hud.testAssetsGroup == null)
            EditorGUILayout.HelpBox("A group reference is missing — use Tools > Batching Demo > Set Up Scene.",
                MessageType.Warning);

        var playing = Application.isPlaying;
        if (playing)
            EditorGUILayout.HelpBox("Stop Play to change target or mode. Modes are applied in edit mode, " +
                                    "then you press Play — that is what makes Unity do the real batching.",
                MessageType.Info);

        using (new EditorGUI.DisabledScope(playing))
        {
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTargetButton(hud, BatchingDemoTarget.Cubes, "Cubes");
                DrawTargetButton(hud, BatchingDemoTarget.TestAssets, "Test Assets");
            }

            EditorGUILayout.Space(8);
            DrawMotoCount(hud);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Mode  —  applies now, then press Play", EditorStyles.boldLabel);

            var activeMode = hud.DetectActiveMode();

            // The numbered running order for the talk.
            var number = 0;
            foreach (var spec in hud.profile.modes)
            {
                number++;
                DrawModeButton(hud, spec, spec == activeMode, number + "   " + spec.displayName);
            }
        }

        // ---- live state, read from Unity ----
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Live state", EditorStyles.boldLabel);

        var detected = hud.DetectActiveMode();
        EditorGUILayout.LabelField("Active mode", detected != null ? detected.displayName : "CUSTOM (no match)");

        hud.GetInstancingCounts(out var io, out var it);
        BatchingDemoApplier.CountBatchingStatic(hud.ActiveGroup, out var marked, out var total);

        EditorGUILayout.LabelField("SRP Batcher", hud.SrpBatcherOn ? "ON" : "off");
        EditorGUILayout.LabelField("Instancing (materials)", io + " / " + it);
        EditorGUILayout.LabelField("Marked Batching Static", marked + " / " + total);
        EditorGUILayout.LabelField("PlayerSettings static batching", BatchingDemoApplier.PlayerStaticBatchingOn() ? "ON" : "off");
        EditorGUILayout.LabelField("Dynamic Batching", hud.DynamicBatchingOn ? "ON" : "off");
        EditorGUILayout.LabelField("GPU Resident Drawer", hud.ResidentDrawerOn ? "ON" : "off");

        if (playing)
        {
            hud.GetStaticBatchCounts(out var inBatch, out var batchTotal);
            EditorGUILayout.LabelField("In static batch (play)", inBatch + " / " + batchTotal);
        }

        if (detected != null)
            EditorGUILayout.HelpBox(detected.description, MessageType.None);
    }

    /// <summary>
    /// Repaint on a timer instead of every frame. Calling Repaint() from OnInspectorGUI forces a
    /// continuous rebuild loop, which is wasted work and one more thing happening during the Play
    /// transition. The live values still refresh often enough to read.
    /// </summary>
    public override bool RequiresConstantRepaint()
    {
        return !Application.isPlaying;
    }

    /// <summary>
    /// Wire the moto group once when the Inspector opens, rather than during OnInspectorGUI — assigning
    /// a reference while the GUI is drawing is how you get "the Inspector changed the scene by being
    /// looked at", and it fights Undo.
    /// </summary>
    void OnEnable()
    {
        var hud = target as BatchingDemoHUD;
        if (hud == null || hud.motoGroup != null || hud.testAssetsGroup == null || Application.isPlaying)
            return;

        var moto = hud.testAssetsGroup.transform.Find("moto");
        if (moto == null)
            return;

        hud.motoGroup = moto.gameObject;
        EditorUtility.SetDirty(hud);
    }

    /// <summary>
    /// How many moto_prefab instances are in the scene. A slider because the interesting question is
    /// "how does this curve behave as the count grows", which a slider answers and a number field does not.
    /// </summary>
    static void DrawMotoCount(BatchingDemoHUD hud)
    {
        EditorGUILayout.LabelField("moto_prefab count", EditorStyles.boldLabel);

        if (hud.motoGroup == null)
        {
            EditorGUILayout.HelpBox("No moto group wired — expected testAssets/moto. " +
                                    "Use Tools > Batching Demo > Set Up Scene.", MessageType.Warning);
            return;
        }

        var total = hud.MotoTotal;
        if (total == 0)
        {
            EditorGUILayout.HelpBox("The layout has not been captured yet — press Capture to record " +
                                    "where the instances currently sit, which is what lets the slider " +
                                    "put a destroyed one back in the right place.", MessageType.Warning);
            if (GUILayout.Button("Capture layout from the scene", GUILayout.Height(24f)))
                BatchingDemoApplier.CaptureMotoLayout(hud, true);
            return;
        }

        EditorGUI.BeginChangeCheck();
        var wanted = EditorGUILayout.IntSlider(hud.motoCount, 0, total);
        if (EditorGUI.EndChangeCheck())
            BatchingDemoApplier.SetMotoCount(hud, wanted);

        // Read back from the scene, never from the slider — the same rule as the mode readout below.
        int inScene, places, renderers;
        hud.GetMotoCounts(out inScene, out places, out renderers);
        EditorGUILayout.LabelField("In the scene", inScene + " / " + places + "   (" + renderers + " renderers)");

        if (inScene != hud.motoCount)
            EditorGUILayout.HelpBox("The scene holds " + inScene + " but the slider says " +
                                    hud.motoCount + " — an instance was added or deleted by hand. " +
                                    "Move the slider to reassert it.", MessageType.Info);
    }

    /// <summary>Green when this mode matches Unity's live state.</summary>
    static void DrawModeButton(BatchingDemoHUD hud, BatchingModeSpec spec, bool isActive, string label)
    {
        var previous = GUI.backgroundColor;

        if (isActive)
            GUI.backgroundColor = new Color(0.45f, 0.95f, 0.55f);

        if (GUILayout.Button(label + (isActive ? "     ✓ active" : ""), GUILayout.Height(28f)))
            BatchingDemoApplier.Apply(spec, hud);

        GUI.backgroundColor = previous;
    }

    static void DrawTargetButton(BatchingDemoHUD hud, BatchingDemoTarget t, string label)
    {
        var active = hud.target == t;
        var previous = GUI.backgroundColor;
        if (active)
            GUI.backgroundColor = new Color(0.45f, 0.95f, 0.55f);

        if (GUILayout.Button(active ? label + "  ✓" : label, GUILayout.Height(24f)) && !active)
            BatchingDemoApplier.SetTarget(hud, t);

        GUI.backgroundColor = previous;
    }

    static void CreateProfile(BatchingDemoHUD hud)
    {
        var path = EditorUtility.SaveFilePanelInProject(
            "Create Batching Demo Profile", "BatchingDemoProfile", "asset", "Where should the profile live?");

        if (string.IsNullOrEmpty(path))
            return;

        var profile = ScriptableObject.CreateInstance<BatchingDemoProfile>();
        profile.ResetToDefaults();
        AssetDatabase.CreateAsset(profile, path);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(hud, "Assign profile");
        hud.profile = profile;
        EditorUtility.SetDirty(hud);
    }
}

/// <summary>Scene setup and a state dump, under Tools > Batching Demo.</summary>
public static class BatchingDemoMenu
{
    const string k_Menu = "Tools/Batching Demo/";

    [MenuItem(k_Menu + "Set Up Scene", priority = 0)]
    public static void SetUpScene()
    {
        var hud = Object.FindAnyObjectByType<BatchingDemoHUD>();
        if (hud == null)
        {
            var host = new GameObject("BatchingDemo");
            Undo.RegisterCreatedObjectUndo(host, "Create BatchingDemo");
            hud = Undo.AddComponent<BatchingDemoHUD>(host);
        }

        if (hud.cubeGroup == null)
            hud.cubeGroup = FindRootIncludingInactive("cube_GRP");

        if (hud.testAssetsGroup == null)
            hud.testAssetsGroup = FindRootIncludingInactive("testAssets");

        if (hud.motoGroup == null && hud.testAssetsGroup != null)
        {
            var moto = hud.testAssetsGroup.transform.Find("moto");
            if (moto != null)
                hud.motoGroup = moto.gameObject;
        }

        if (hud.profile == null)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:BatchingDemoProfile"))
            {
                hud.profile = AssetDatabase.LoadAssetAtPath<BatchingDemoProfile>(AssetDatabase.GUIDToAssetPath(guid));
                break;
            }
        }

        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        Selection.activeGameObject = hud.gameObject;

        Debug.LogFormat("[BatchingDemo] Scene ready — cubes={0} testAssets={1} profile={2}",
            hud.cubeGroup != null, hud.testAssetsGroup != null, hud.profile != null ? hud.profile.name : "NONE");
    }

    /// <summary>GameObject.Find skips inactive objects, and the hidden group is always inactive.</summary>
    public static GameObject FindRootIncludingInactive(string name)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.IsValid())
            return null;

        foreach (var root in scene.GetRootGameObjects())
            if (root.name == name)
                return root;

        return null;
    }
}
