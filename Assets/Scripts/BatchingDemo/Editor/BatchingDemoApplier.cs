using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Applies a mode to the project, in EDIT mode only.
///
/// This is the whole point of the redesign: set the switches with Play stopped, watch them tick in the
/// Inspector, then press Play. Unity then performs its real load-time static batching, exactly as a
/// build would — no runtime combine, no un-combine trickery, nothing that can drift from the truth.
///
/// Everything here writes the ACTUAL objects, materials and project settings. Nothing is cloned,
/// hidden or swapped.
/// </summary>
public static class BatchingDemoApplier
{
    public static void Apply(BatchingModeSpec spec, BatchingDemoHUD hud)
    {
        if (spec == null || hud == null)
            return;

        if (Application.isPlaying)
        {
            Debug.LogWarning("[BatchingDemo] Stop Play before applying a mode — modes are an edit-time setup.");
            return;
        }

        var active = hud.ActiveGroup;
        var inactive = active == hud.cubeGroup ? hud.testAssetsGroup : hud.cubeGroup;

        SetSrpBatcher(spec.srpBatcher);
        SetResidentDrawer(spec.gpuResidentDrawer);
        SetDynamicBatching(spec.dynamicBatching);
        SetPlayerStaticBatching(spec.playerStaticBatching);

        SetInstancing(active, spec.gpuInstancing);
        SetBatchingStatic(active, spec.batchingStatic);

        // The group not on show goes back to plain, so a leftover flag cannot skew a later comparison.
        SetInstancing(inactive, false);
        SetBatchingStatic(inactive, false);

        SetAnimation(!spec.disableAnimation);

        AssetDatabase.SaveAssets();

        if (active != null)
            EditorSceneManager.MarkSceneDirty(active.scene);

        Report(spec, hud);
    }

    const string k_MotoPrefabPath = "Assets/prefabs/moto_prefab.prefab";

    /// <summary>
    /// Records where the instances currently sit, and how they spin, so the slider can destroy one and
    /// still put it back exactly as the artist placed it. Runs once — a later capture would record a
    /// layout the slider had already thinned, losing the empty places for good.
    /// </summary>
    public static void CaptureMotoLayout(BatchingDemoHUD hud, bool force)
    {
        if (hud == null || hud.motoGroup == null)
            return;

        if (!force && hud.motoPoses != null && hud.motoPoses.Length > 0)
            return;

        var t = hud.motoGroup.transform;
        if (t.childCount == 0)
            return;

        var poses = new MotoPose[t.childCount];
        for (var i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            poses[i] = new MotoPose
            {
                position = c.localPosition,
                eulerAngles = c.localEulerAngles,
                scale = c.localScale
            };
        }

        Undo.RecordObject(hud, "Capture moto layout");
        hud.motoPoses = poses;
        hud.motoRotator.CopyFrom(t.GetChild(0).GetComponent<ContinuousRotator>());

        // Which children the reference instance has switched off, recorded by path so a rebuilt
        // instance matches it. Only overwritten when something is actually found, so a capture taken
        // against a stripped instance cannot erase the list.
        var off = CollectDisabledPaths(t.GetChild(0));
        if (off.Count > 0)
            hud.motoDisabledPaths = off.ToArray();

        if (hud.motoPrefab == null)
            hud.motoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_MotoPrefabPath);

        EditorUtility.SetDirty(hud);
        Debug.LogFormat("[BatchingDemo] Captured {0} moto places, spin {1}°/s.",
            poses.Length, hud.motoRotator.degreesPerSecond);
    }

    /// <summary>
    /// Builds the scene so it holds EXACTLY count moto_prefab instances — creating what is missing and
    /// DESTROYING what is spare. Nothing is left behind disabled: at count 1 the Hierarchy holds one.
    ///
    /// Destroying rather than deactivating is Asghar's call, and it is the right one. A disabled instance
    /// still costs what a disabled instance costs — it is loaded, serialized into the scene file, walked
    /// by every GetComponentsInChildren, and drawn in the Hierarchy. Fifteen of these is 1,140 renderer
    /// objects present whatever the slider says.
    ///
    /// EDIT MODE ONLY, for the same reason every other switch here is: static batching happens at SCENE
    /// LOAD. An instance created during Play was not there when Unity built the combined mesh, so it
    /// could never join it and the Static Batching mode would quietly under-report.
    /// </summary>
    public static void SetMotoCount(BatchingDemoHUD hud, int count)
    {
        if (hud == null || hud.motoGroup == null)
            return;

        if (Application.isPlaying)
        {
            Debug.LogWarning("[BatchingDemo] Stop Play before changing the moto count — static batching " +
                             "is decided at scene load, so a count changed mid-Play cannot be honest.");
            return;
        }

        CaptureMotoLayout(hud, false);

        if (hud.motoPrefab == null)
            hud.motoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_MotoPrefabPath);

        if (hud.motoPrefab == null)
        {
            Debug.LogError("[BatchingDemo] No moto prefab at " + k_MotoPrefabPath + " — cannot rebuild.");
            return;
        }

        var t = hud.motoGroup.transform;
        count = Mathf.Clamp(count, 0, hud.MotoTotal);

        if (hud.motoCount != count)
        {
            Undo.RecordObject(hud, "Moto count");
            hud.motoCount = count;
            EditorUtility.SetDirty(hud);
        }

        // Spare instances go, highest index first — removing from the end keeps every surviving
        // instance on the pose index it was built from.
        for (var i = t.childCount - 1; i >= count; i--)
            Undo.DestroyObjectImmediate(t.GetChild(i).gameObject);

        // An instance left DISABLED by the earlier activate/deactivate version of this control would
        // otherwise survive as an invisible member of the count. Everything present is now present for real.
        for (var i = 0; i < t.childCount; i++)
        {
            var go = t.GetChild(i).gameObject;
            if (go.activeSelf)
                continue;

            Undo.RecordObject(go, "Moto count");
            go.SetActive(true);
            EditorUtility.SetDirty(go);
        }

        // A surviving instance is the reference for the Batching Static flags, so a count raised after a
        // mode was applied does not produce instances that quietly sit outside that mode.
        var flags = t.childCount > 0
            ? GameObjectUtility.GetStaticEditorFlags(t.GetChild(0).gameObject)
            : (StaticEditorFlags) 0;

        for (var i = t.childCount; i < count; i++)
        {
            var go = (GameObject) PrefabUtility.InstantiatePrefab(hud.motoPrefab, t);
            if (go == null)
                continue;

            Undo.RegisterCreatedObjectUndo(go, "Moto count");

            go.name = i == 0 ? "moto_prefab" : "moto_prefab (" + i + ")";
            go.transform.localPosition = hud.motoPoses[i].position;
            go.transform.localEulerAngles = hud.motoPoses[i].eulerAngles;
            go.transform.localScale = hud.motoPoses[i].scale == Vector3.zero
                ? Vector3.one
                : hud.motoPoses[i].scale;
            go.transform.SetSiblingIndex(i);

            // The prefab carries no rotator, so a rebuilt instance would not spin without this.
            hud.motoRotator.ApplyTo(Undo.AddComponent<ContinuousRotator>(go));

            ApplyDisabledChildren(hud, go);
            SetStaticFlagsRecursive(go, flags);
        }

        EditorSceneManager.MarkSceneDirty(hud.motoGroup.scene);

        int inScene, places, renderers;
        hud.GetMotoCounts(out inScene, out places, out renderers);
        Debug.LogFormat("[BatchingDemo] moto_prefab {0}/{1} in the scene — {2} renderers. Press Play.",
            inScene, places, renderers);
    }

    /// <summary>
    /// Every inactive descendant of an instance, as a path relative to it. Stops descending at an
    /// inactive node — its children are already off by inheritance, and listing them would just be noise.
    /// </summary>
    static System.Collections.Generic.List<string> CollectDisabledPaths(Transform instance)
    {
        var found = new System.Collections.Generic.List<string>();
        Walk(instance, instance, "", found);
        return found;
    }

    static void Walk(Transform root, Transform node, string prefix, System.Collections.Generic.List<string> found)
    {
        foreach (Transform child in node)
        {
            var path = prefix.Length == 0 ? child.name : prefix + "/" + child.name;
            if (!child.gameObject.activeSelf)
            {
                found.Add(path);
                continue;
            }
            Walk(root, child, path, found);
        }
    }

    /// <summary>
    /// Switches off the children the scene's original instances had off.
    ///
    /// This is not cosmetic. The prefab ships nine point lights and a blob-shadow mesh ENABLED, and the
    /// hand-placed instances had every one of them off. Rebuilding without this puts 9 real-time lights
    /// back on each motorcycle — at 15 instances that is 135 lights, which changes light culling, adds
    /// passes, and would make every batching measurement in the demo wrong.
    /// </summary>
    static void ApplyDisabledChildren(BatchingDemoHUD hud, GameObject instance)
    {
        if (hud.motoDisabledPaths == null)
            return;

        foreach (var path in hud.motoDisabledPaths)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            var child = instance.transform.Find(path);
            if (child == null)
            {
                Debug.LogWarningFormat("[BatchingDemo] moto instance has no child '{0}' to switch off — " +
                                       "the prefab changed shape. Check motoDisabledPaths.", path);
                continue;
            }

            child.gameObject.SetActive(false);
        }
    }

    static void SetStaticFlagsRecursive(GameObject root, StaticEditorFlags flags)
    {
        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(tr.gameObject, flags);
    }

    public static void SetTarget(BatchingDemoHUD hud, BatchingDemoTarget target)
    {
        if (hud == null || Application.isPlaying)
            return;

        Undo.RecordObject(hud, "Batching demo target");
        hud.target = target;

        if (hud.cubeGroup != null)
            hud.cubeGroup.SetActive(target == BatchingDemoTarget.Cubes);

        if (hud.testAssetsGroup != null)
            hud.testAssetsGroup.SetActive(target == BatchingDemoTarget.TestAssets);

        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
    }

    // ------------------------------------------------------------------ individual switches

    static void SetSrpBatcher(bool on)
    {
        GraphicsSettings.useScriptableRenderPipelineBatching = on;

        var urp = BatchingDemoHUD.UrpAsset;
        if (urp == null || urp.useSRPBatcher == on)
            return;

        Undo.RecordObject(urp, "SRP Batcher");
        urp.useSRPBatcher = on;
        EditorUtility.SetDirty(urp);
    }

    static void SetResidentDrawer(bool on)
    {
        var urp = BatchingDemoHUD.UrpAsset;
        if (urp == null)
            return;

        var wanted = on ? GPUResidentDrawerMode.InstancedDrawing : GPUResidentDrawerMode.Disabled;
        if (urp.gpuResidentDrawerMode == wanted)
            return;

        Undo.RecordObject(urp, "GPU Resident Drawer");
        urp.gpuResidentDrawerMode = wanted;
        EditorUtility.SetDirty(urp);
    }

    static void SetDynamicBatching(bool on)
    {
        SetProjectBatchingFlag("m_DynamicBatching", on);

        var urp = BatchingDemoHUD.UrpAsset;
        if (urp == null || urp.supportsDynamicBatching == on)
            return;

        Undo.RecordObject(urp, "Dynamic batching");
        urp.supportsDynamicBatching = on;
        EditorUtility.SetDirty(urp);
    }

    static void SetPlayerStaticBatching(bool on)
    {
        SetProjectBatchingFlag("m_StaticBatching", on);
    }

    /// <summary>
    /// These are Booleans in ProjectSettings.asset — writing intValue throws. ApplyModifiedProperties
    /// alone never reaches the file, so the checkbox would not move and the setting would be gone next
    /// session; SaveAssets (called once by Apply) is what commits it.
    /// </summary>
    static void SetProjectBatchingFlag(string relativeName, bool on)
    {
        var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (settings == null || settings.Length == 0)
            return;

        var so = new SerializedObject(settings[0]);
        var batching = so.FindProperty("m_BuildTargetBatching");
        if (batching == null)
            return;

        for (var i = 0; i < batching.arraySize; i++)
        {
            var entry = batching.GetArrayElementAtIndex(i).FindPropertyRelative(relativeName);
            if (entry != null)
                entry.boolValue = on;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings[0]);
    }

    /// <summary>
    /// Rotators on or off for the whole scene, written into the scene so Play starts that way. Static
    /// batching bakes geometry in place — a mode that batches must not also claim to animate.
    /// </summary>
    static void SetAnimation(bool on)
    {
        foreach (var r in Object.FindObjectsByType<ContinuousRotator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r.enabled == on)
                continue;

            Undo.RecordObject(r, "Demo animation");
            r.enabled = on;
            EditorUtility.SetDirty(r);
        }

        var toggle = Object.FindAnyObjectByType<RotationToggleGUI>();
        if (toggle != null && toggle.animationOn != on)
        {
            Undo.RecordObject(toggle, "Demo animation");
            toggle.animationOn = on;
            EditorUtility.SetDirty(toggle);
        }
    }

    static void SetInstancing(GameObject group, bool on)
    {
        if (group == null)
            return;

        foreach (var m in CollectSharedMaterials(group))
        {
            if (m.enableInstancing == on)
                continue;

            Undo.RecordObject(m, "GPU instancing");
            m.enableInstancing = on;
            EditorUtility.SetDirty(m);
        }
    }

    /// <summary>Marks the group AND every child, so any object can be selected on stage and verified.</summary>
    static void SetBatchingStatic(GameObject group, bool on)
    {
        if (group == null)
            return;

        foreach (var t in group.GetComponentsInChildren<Transform>(true))
        {
            var go = t.gameObject;
            var flags = GameObjectUtility.GetStaticEditorFlags(go);
            var updated = on
                ? flags | StaticEditorFlags.BatchingStatic
                : flags & ~StaticEditorFlags.BatchingStatic;

            if (updated == flags)
                continue;

            Undo.RecordObject(go, "Batching Static");
            GameObjectUtility.SetStaticEditorFlags(go, updated);
            EditorUtility.SetDirty(go);
        }
    }

    /// <summary>Shared materials only, and never a package asset — those are immutable and shared project-wide.</summary>
    public static System.Collections.Generic.List<Material> CollectSharedMaterials(GameObject root)
    {
        var list = new System.Collections.Generic.List<Material>();
        if (root == null)
            return list;

        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || list.Contains(m))
                    continue;

                var path = AssetDatabase.GetAssetPath(m);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Packages/"))
                    continue;

                list.Add(m);
            }

        return list;
    }

    public static void CountBatchingStatic(GameObject group, out int marked, out int total)
    {
        marked = 0;
        total = 0;
        if (group == null)
            return;

        foreach (var r in group.GetComponentsInChildren<MeshRenderer>(false))
        {
            total++;
            if ((GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                marked++;
        }
    }

    public static bool PlayerStaticBatchingOn()
    {
        var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (settings == null || settings.Length == 0)
            return false;

        var so = new SerializedObject(settings[0]);
        var batching = so.FindProperty("m_BuildTargetBatching");
        if (batching == null || batching.arraySize == 0)
            return false;

        var entry = batching.GetArrayElementAtIndex(0).FindPropertyRelative("m_StaticBatching");
        return entry != null && entry.boolValue;
    }

    static void Report(BatchingModeSpec spec, BatchingDemoHUD hud)
    {
        CountBatchingStatic(hud.ActiveGroup, out var marked, out var total);
        hud.GetInstancingCounts(out var io, out var it);

        Debug.LogFormat("[BatchingDemo] {0} applied to {1} — SRP {2}, instancing {3}/{4} mats, " +
                        "Batching Static {5}/{6}, PlayerSettings static {7}, dynamic {8}, GRD {9}.  Press Play.",
            spec.displayName, hud.target,
            GraphicsSettings.useScriptableRenderPipelineBatching ? "ON" : "off",
            io, it, marked, total,
            PlayerStaticBatchingOn() ? "ON" : "off",
            hud.DynamicBatchingOn ? "ON" : "off",
            hud.ResidentDrawerOn ? "ON" : "off");
    }
}
