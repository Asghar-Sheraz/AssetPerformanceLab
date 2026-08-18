using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public struct MotoPose
{
    public Vector3 position;
    public Vector3 eulerAngles;
    public Vector3 scale;
}


[System.Serializable]
public class MotoRotatorSettings
{
    public float degreesPerSecond = 45f;
    public Vector3 axis = Vector3.up;
    public Space space = Space.Self;
    public bool useUnscaledTime;
    public bool randomiseStartAngle;

    public void CopyFrom(ContinuousRotator r)
    {
        if (r == null) { return; }
        degreesPerSecond = r.degreesPerSecond;
        axis = r.axis;
        space = r.space;
        useUnscaledTime = r.useUnscaledTime;
        randomiseStartAngle = r.randomiseStartAngle;
    }

    public void ApplyTo(ContinuousRotator r)
    {
        if (r == null) { return; }
        r.degreesPerSecond = degreesPerSecond;
        r.axis = axis;
        r.space = space;
        r.useUnscaledTime = useUnscaledTime;
        r.randomiseStartAngle = randomiseStartAngle;
    }
}


public enum BatchingDemoTarget
{

    Cubes = 0,

    TestAssets = 1
}


[DisallowMultipleComponent]
public class BatchingDemoHUD : MonoBehaviour
{
    [Header("Data")]
    [Tooltip("The mode list. Modes are applied from the profile's Inspector, in edit mode.")]
    public BatchingDemoProfile profile;

    [Header("Scene groups")]
    public GameObject cubeGroup;
    public GameObject testAssetsGroup;

    [Tooltip("The parent of the moto_prefab instances (testAssets/moto). The count slider CREATES and " +
             "DESTROYS its children — nothing is left in the scene disabled.")]
    public GameObject motoGroup;

    [Tooltip("The prefab the instances are built from. Assets/prefabs/moto_prefab.prefab.")]
    public GameObject motoPrefab;

    [Tooltip("How many moto_prefab instances exist. Applied in EDIT mode, so Unity does its real " +
             "load-time batching on exactly this many when Play starts. 76 renderers each.")]
    public int motoCount = 4;

    [Tooltip("Where each instance goes, captured from the original hand-placed layout. This is what " +
             "lets a destroyed instance come back in the right spot.")]
    public MotoPose[] motoPoses = new MotoPose[0];

    [Tooltip("The spin settings copied onto every instance the slider creates. Captured from the " +
             "original instances, because the PREFAB has no rotator — it was added per instance.")]
    public MotoRotatorSettings motoRotator = new MotoRotatorSettings();

    [Tooltip("Children switched OFF on every instance, by path inside the prefab. The scene's original " +
             "instances turned these off one by one; the prefab has them ON, so a rebuilt instance would " +
             "silently bring back nine real-time point lights and a shadow mesh.")]
    public string[] motoDisabledPaths =
    {
        "moto_bot/shdw",   
        "Point Light",
        "Point Light (1)",
        "Point Light (2)",
        "Point Light (3)",
        "Point Light (4)",
        "red_01",
        "red_02",
        "red_03",
        "red_04"
    };

    [Tooltip("Which group is on show. Set from the profile Inspector; the hidden group is deactivated.")]
    public BatchingDemoTarget target = BatchingDemoTarget.Cubes;

    [Header("Layout")]
    public float panelWidth = 430f;
    public float margin = 20f;
    public float referenceHeight = 1080f;

    [Tooltip("Hide the panel for a clean Frame Debugger capture. Toggle with H.")]
    public bool visible = true;

    static readonly Color k_PanelBg = new Color(0.055f, 0.062f, 0.074f, 0.93f);
    static readonly Color k_PanelBorder = new Color(1f, 1f, 1f, 0.10f);
    static readonly Color k_RowBg = new Color(1f, 1f, 1f, 0.035f);
    static readonly Color k_Accent = new Color(0.35f, 0.85f, 0.45f);
    static readonly Color k_Warn = new Color(0.95f, 0.72f, 0.30f);
    static readonly Color k_Subtitle = new Color(0.45f, 0.65f, 0.95f);
    static readonly Color k_Text = new Color(0.88f, 0.90f, 0.93f);
    static readonly Color k_Dim = new Color(0.55f, 0.58f, 0.63f);

    Texture2D m_White;
    GUIStyle m_Title, m_Subtitle, m_SectionLabel, m_ModeName, m_StatusLabel, m_StatusValue, m_Description;
    bool m_StylesBuilt;

    public GameObject ActiveGroup =>
        target == BatchingDemoTarget.TestAssets ? testAssetsGroup : cubeGroup;

    public int MotoTotal => motoPoses != null ? motoPoses.Length : 0;

    public void GetMotoCounts(out int inScene, out int places, out int renderers)
    {
        inScene = 0;
        places = MotoTotal;
        renderers = 0;
        if (motoGroup == null) { return; }

        foreach (Transform child in motoGroup.transform)
        {
            inScene++;
            renderers += child.GetComponentsInChildren<MeshRenderer>(false).Length;
        }
    }


    public static UniversalRenderPipelineAsset UrpAsset =>
        GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

    public bool SrpBatcherOn => GraphicsSettings.useScriptableRenderPipelineBatching;
    public bool DynamicBatchingOn => UrpAsset != null && UrpAsset.supportsDynamicBatching;
    public bool ResidentDrawerOn => UrpAsset != null && UrpAsset.gpuResidentDrawerMode != GPUResidentDrawerMode.Disabled;

#if UNITY_EDITOR
    double m_PlayerStaticReadAt = -1d;
    bool m_PlayerStaticCached;

    /// <summary>
    /// Player Settings > Other Settings > Static Batching, read out of ProjectSettings.asset — the file
    /// is the only honest source, since the in-memory property returns a value that may never have been
    /// written. Cached briefly: this is reached once per OnGUI frame, and opening a SerializedObject on
    /// the settings asset that often would cost more than the thing the demo is measuring.
    /// </summary>
    public bool PlayerStaticBatchingOn
    {
        get
        {
            var now = UnityEditor.EditorApplication.timeSinceStartup;
            if (m_PlayerStaticReadAt >= 0d && now - m_PlayerStaticReadAt < 0.5d)
                return m_PlayerStaticCached;

            m_PlayerStaticReadAt = now;
            m_PlayerStaticCached = false;

            var settings = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settings == null || settings.Length == 0)
                return m_PlayerStaticCached;

            var batching = new UnityEditor.SerializedObject(settings[0]).FindProperty("m_BuildTargetBatching");
            if (batching == null || batching.arraySize == 0)
                return m_PlayerStaticCached;

            var entry = batching.GetArrayElementAtIndex(0).FindPropertyRelative("m_StaticBatching");
            m_PlayerStaticCached = entry != null && entry.boolValue;
            return m_PlayerStaticCached;
        }
    }
#endif

    public List<Material> Materials
    {
        get
        {
            var list = new List<Material>();
            var group = ActiveGroup;
            if (group == null)
                return list;

            foreach (var r in group.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && !list.Contains(m))
                        list.Add(m);

            return list;
        }
    }

    public void GetInstancingCounts(out int on, out int total)
    {
        on = 0;
        var mats = Materials;
        total = mats.Count;
        foreach (var m in mats)
            if (m.enableInstancing)
                on++;
    }

    public void GetMarkedStaticCounts(out int marked, out int total)
    {
        marked = 0;
        total = 0;

        var group = ActiveGroup;
        if (group == null)
            return;

        foreach (var r in group.GetComponentsInChildren<MeshRenderer>(false))
        {
            total++;
            if (r.gameObject.isStatic)
                marked++;
        }
    }

    public void GetStaticBatchCounts(out int inBatch, out int total)
    {
        inBatch = 0;
        total = 0;

        var group = ActiveGroup;
        if (group == null)
            return;

        foreach (var r in group.GetComponentsInChildren<MeshRenderer>(false))
        {
            total++;
            if (r.isPartOfStaticBatch)
                inBatch++;
        }
    }

    public BatchingModeSpec DetectActiveMode()
    {
        if (profile == null || profile.modes == null)
            return null;

        GetInstancingCounts(out var instOn, out var instTotal);
        GetMarkedStaticCounts(out var marked, out var markedTotal);

        var instancing = instTotal > 0 && instOn == instTotal;
        var markedStatic = markedTotal > 0 && marked == markedTotal;

        foreach (var m in profile.modes)
        {
            // Outside the Editor the Player Settings flag cannot be read back, so it is taken as
            // agreeing rather than allowed to veto a mode it cannot actually check.
#if UNITY_EDITOR
            var playerStatic = PlayerStaticBatchingOn;
#else
            var playerStatic = m.playerStaticBatching;
#endif
            if (m.Matches(SrpBatcherOn, instancing, markedStatic, playerStatic,
                          DynamicBatchingOn, ResidentDrawerOn))
                return m;
        }

        return null;
    }


    void OnDestroy()
    {
        DemoUIBlocker.Clear(GetInstanceID());

        if (m_White != null)
            Destroy(m_White);
    }

    void OnGUI()
    {
        var e = Event.current;
        if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.H)
        {
            visible = !visible;
            e.Use();
        }

        if (!visible)
        {
            DemoUIBlocker.Clear(GetInstanceID());
            return;
        }

        BuildStyles();

        var scale = Mathf.Max(1f, Screen.height / referenceHeight);
        var previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        var panel = DrawPanel();

        GUI.matrix = previous;

        DemoUIBlocker.Set(GetInstanceID(),
            new Rect(panel.x * scale, panel.y * scale, panel.width * scale, panel.height * scale));
    }

    Rect DrawPanel()
    {
        const float pad = 16f;
        var active = DetectActiveMode();

        var panel = new Rect(margin, margin, panelWidth, 316f);
        DrawBox(panel, k_PanelBg, k_PanelBorder);

        var x = panel.x + pad;
        var w = panel.width - pad * 2f;
        var y = panel.y + pad;

        GUI.Label(new Rect(x, y, w, 26f), "RENDERING OPTIMIZATION", m_Title);
        y += 26f;
        GUI.Label(new Rect(x, y, w, 18f),
            "Unity URP  ·  " + (target == BatchingDemoTarget.TestAssets ? "Test Assets" : "Cubes"), m_Subtitle);
        y += 30f;

        GUI.Label(new Rect(x, y, w, 16f), "ACTIVE MODE", m_SectionLabel);
        y += 18f;

        var nameStyle = new GUIStyle(m_ModeName);
        nameStyle.normal.textColor = active != null ? k_Accent : k_Warn;
        GUI.Label(new Rect(x, y, w, 30f),
            active != null ? active.displayName.ToUpperInvariant() : "CUSTOM", nameStyle);
        y += 36f;

        GetInstancingCounts(out var instOn, out var instTotal);
        y = DrawRow(x, y, w, "SRP Batcher", SrpBatcherOn ? "ON" : "off", SrpBatcherOn);
        y = DrawRow(x, y, w, "GPU Instancing", instOn + " / " + instTotal + " mats", instOn > 0 && instOn == instTotal);
        y = DrawRow(x, y, w, "Dynamic Batching", DynamicBatchingOn ? "ON" : "off", DynamicBatchingOn);
        y = DrawRow(x, y, w, "GPU Resident Drawer", ResidentDrawerOn ? "ON" : "off", ResidentDrawerOn);

        GetMarkedStaticCounts(out var marked, out var markedTotal);
        y = DrawRow(x, y, w, "Marked Batching Static", marked + " / " + markedTotal, marked > 0);

        GetStaticBatchCounts(out var inBatch, out var batchTotal);
        y = DrawRow(x, y, w, "In static batch", inBatch + " / " + batchTotal, inBatch > 0);

        y += 6f;
        var desc = new Rect(x, y, w, 52f);
        DrawBox(desc, k_RowBg, Color.clear);
        GUI.Label(new Rect(desc.x + 10f, desc.y + 4f, desc.width - 20f, desc.height - 8f),
            active != null
                ? active.description
                : "The live settings do not match any mode in the profile. Apply one from the profile Inspector.",
            m_Description);

        return panel;
    }

    float DrawRow(float x, float y, float w, string label, string value, bool on)
    {
        var row = new Rect(x, y, w, 26f);
        DrawBox(row, k_RowBg, Color.clear);

        GUI.Label(new Rect(row.x + 10f, row.y, row.width - 110f, row.height), label, m_StatusLabel);

        var dot = new Rect(row.xMax - 100f, row.y + 10f, 7f, 7f);
        DrawBox(dot, on ? k_Accent : new Color(0.35f, 0.37f, 0.40f), Color.clear);

        var style = new GUIStyle(m_StatusValue);
        style.normal.textColor = on ? k_Accent : k_Dim;
        GUI.Label(new Rect(row.xMax - 86f, row.y, 80f, row.height), value, style);

        return y + 28f;
    }

    void DrawBox(Rect rect, Color fill, Color border)
    {
        var previous = GUI.color;

        if (fill.a > 0f)
        {
            GUI.color = fill;
            GUI.DrawTexture(rect, m_White);
        }

        if (border.a > 0f)
        {
            GUI.color = border;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), m_White);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), m_White);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), m_White);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), m_White);
        }

        GUI.color = previous;
    }

    void BuildStyles()
    {
        if (m_StylesBuilt && m_White != null)
            return;

        m_White = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        m_White.SetPixel(0, 0, Color.white);
        m_White.Apply();
        m_White.hideFlags = HideFlags.HideAndDontSave;

        m_Title = new GUIStyle { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        m_Title.normal.textColor = k_Text;

        m_Subtitle = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        m_Subtitle.normal.textColor = k_Subtitle;

        m_SectionLabel = new GUIStyle { fontSize = 11, alignment = TextAnchor.MiddleCenter };
        m_SectionLabel.normal.textColor = k_Dim;

        m_ModeName = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

        m_StatusLabel = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        m_StatusLabel.normal.textColor = k_Text;

        m_StatusValue = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };

        m_Description = new GUIStyle { fontSize = 12, alignment = TextAnchor.UpperLeft, wordWrap = true };
        m_Description.normal.textColor = k_Dim;

        m_StylesBuilt = true;
    }
}
