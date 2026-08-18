using UnityEngine;

/// <summary>
/// Runtime on/off switch for every ContinuousRotator in the scene. Pure IMGUI — no Canvas,
/// no EventSystem, nothing to wire up. Sits bottom-left: the top-right is the Game view's own
/// Stats/info overlay, and the top-left is the batching panel.
/// </summary>
[AddComponentMenu("Demo/Rotation Toggle GUI")]
[DisallowMultipleComponent]
public class RotationToggleGUI : MonoBehaviour
{
    [Tooltip("Whether the rotators are running. Also the state the scene starts in.")]
    public bool animationOn = true;

    [Tooltip("Key that toggles the animation during a presentation.")]
    public KeyCode shortcut = KeyCode.T;

    [Tooltip("Design height the layout is authored against; 2160p output scales up.")]
    public float referenceHeight = 1080f;

    public float margin = 20f;

    static readonly Color k_PanelBg = new Color(0.055f, 0.062f, 0.074f, 0.93f);
    static readonly Color k_Border = new Color(1f, 1f, 1f, 0.10f);
    static readonly Color k_On = new Color(0.35f, 0.85f, 0.45f);
    static readonly Color k_Off = new Color(0.55f, 0.58f, 0.63f);
    static readonly Color k_Text = new Color(0.88f, 0.90f, 0.93f);

    Texture2D m_White;
    GUIStyle m_Label, m_Value, m_Hint;
    bool m_Built;

    void Start()
    {
        // Make the scene match the authored state rather than whatever the components happen to be.
        ApplyToRotators(animationOn);
    }

    void OnDestroy()
    {
        DemoUIBlocker.Clear(GetInstanceID());

        if (m_White != null)
            Destroy(m_White);
    }

    /// <summary>Enable or disable every rotator, including ones on objects enabled later.</summary>
    public void ApplyToRotators(bool on)
    {
        animationOn = on;

#if UNITY_2022_2_OR_NEWER
        var rotators = FindObjectsByType<ContinuousRotator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var rotators = FindObjectsOfType<ContinuousRotator>(true);
#endif
        foreach (var r in rotators)
            r.enabled = on;
    }

    public void Toggle()
    {
        ApplyToRotators(!animationOn);
    }

    void OnGUI()
    {
        var e = Event.current;
        if (e != null && e.type == EventType.KeyDown && e.keyCode == shortcut)
        {
            Toggle();
            e.Use();
        }

        BuildStyles();

        var scale = Mathf.Max(1f, Screen.height / referenceHeight);
        var previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        const float w = 210f;
        const float h = 66f;

        // Bottom-LEFT. The top-right corner is where the Game view's Stats/info overlay sits, which was
        // drawing on top of this, and the top-left is taken by the batching panel.
        var designHeight = Screen.height / scale;
        var panel = new Rect(margin, designHeight - h - margin, w, h);

        DrawBox(panel, k_PanelBg, k_Border);

        GUI.Label(new Rect(panel.x + 14f, panel.y + 8f, w - 28f, 18f), "ANIMATION", m_Label);

        var valueStyle = new GUIStyle(m_Value);
        valueStyle.normal.textColor = animationOn ? k_On : k_Off;

        // Whole panel is the hit area; the label just reports state.
        if (GUI.Button(new Rect(panel.x + 14f, panel.y + 26f, w - 28f, 24f),
                animationOn ? "ON" : "OFF", valueStyle))
            Toggle();

        GUI.Label(new Rect(panel.x + 14f, panel.y + 48f, w - 28f, 14f),
            "press " + shortcut + " to toggle", m_Hint);

        GUI.matrix = previous;

        // Stop clicks here from also reaching the orbit camera's click-to-focus.
        DemoUIBlocker.Set(GetInstanceID(),
            new Rect(panel.x * scale, panel.y * scale, panel.width * scale, panel.height * scale));
    }

    void DrawBox(Rect rect, Color fill, Color border)
    {
        var previous = GUI.color;

        GUI.color = fill;
        GUI.DrawTexture(rect, m_White);

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
        if (m_Built && m_White != null)
            return;

        m_White = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        m_White.SetPixel(0, 0, Color.white);
        m_White.Apply();
        m_White.hideFlags = HideFlags.HideAndDontSave;

        m_Label = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        m_Label.normal.textColor = k_Text;

        m_Value = new GUIStyle
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        m_Value.hover.textColor = k_Text;

        m_Hint = new GUIStyle { fontSize = 10, alignment = TextAnchor.MiddleLeft };
        m_Hint.normal.textColor = new Color(0.45f, 0.47f, 0.51f);

        m_Built = true;
    }
}
