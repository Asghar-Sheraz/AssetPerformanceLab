using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Play-mode viewport camera with Maya navigation:
///
///   Alt + left-drag    tumble (orbit)
///   Alt + middle-drag  track (pan)
///   Alt + right-drag   dolly — drag right/up to move in, slow and fine
///   wheel              zoom, towards the cursor
///   left-click         select — the tumble pivot moves to that object
///   right-click        pivot on that exact spot on the surface
///   F                  frame the selection
///   A                  frame everything
///   Shift              faster zoom and track
///
/// Every gesture eases towards a target rather than moving the transform directly, so tumbling,
/// tracking, dollying and framing all glide to a stop. Set any *SmoothTime to 0 for a hard 1:1 feel.
///
/// Works with either the legacy Input class or the new Input System package.
/// </summary>
[AddComponentMenu("Camera/Orbit Camera Controller")]
[DisallowMultipleComponent]
public class OrbitCameraController : MonoBehaviour
{
    [Header("Pivot")]
    [Tooltip("What the camera orbits around. Leave empty to use Focus Point / the scene's renderer bounds.")]
    public Transform target;

    [Tooltip("Pivot used when Target is empty. Auto-filled from the scene bounds if Auto Frame On Start is on.")]
    public Vector3 focusPoint = Vector3.zero;

    [Tooltip("On start, centre the pivot on every renderer in the scene and back off far enough to see them all.")]
    public bool autoFrameOnStart = true;

    [Header("Maya navigation")]
    [Tooltip("On: Maya's scheme, Alt + drag to tumble/track/dolly. " +
             "Off: bare left-drag orbits, right/middle-drag pans — click a spot, then drag to orbit it.")]
    public bool requireAltForNavigation = true;

    [Tooltip("Degrees of tumble per pixel of mouse movement.")]
    public float tumbleSpeed = 0.25f;
    public float minPitch = -85f;
    public float maxPitch = 85f;

    [Tooltip("Track distance per pixel, as a fraction of the pivot distance.")]
    public float trackSpeed = 0.002f;

    [Tooltip("Alt + right-drag dolly: fraction of the distance closed per pixel. Small = slow and fine.")]
    public float dollySpeed = 0.0025f;

    [Tooltip("Seconds of easing on tumble. Higher = heavier, glides further after you let go.")]
    public float tumbleSmoothTime = 0.08f;

    [Tooltip("Seconds of easing on track. Keep low — a laggy pan feels like treacle.")]
    public float trackSmoothTime = 0.06f;

    [Header("Zoom")]
    [Tooltip("Fraction of the current distance closed per scroll notch.")]
    public float zoomSpeed = 0.5f;

    [Tooltip("Fraction of the distance closed per second while +/- is held.")]
    public float zoomKeySpeed = 3f;

    [Tooltip("Hold Shift to zoom and track this much faster.")]
    public float fastMultiplier = 3f;

    [Tooltip("Zoom towards whatever is under the cursor, like Maya's 'zoom towards centre of interest' off.")]
    public bool zoomTowardsCursor = true;

    [Tooltip("Seconds of easing on zoom and dolly. 0 = instant snap.")]
    public float zoomSmoothTime = 0.12f;
    public float minDistance = 0.1f;
    public float maxDistance = 2000f;

    [Header("Selection")]
    [Tooltip("Left-click selects and re-pivots (Maya's 'tumble about selection'); right-click pivots on a point.")]
    public bool clickToSelect = true;

    [Tooltip("Left-click orbits around the exact spot you clicked rather than the object's centre. " +
             "Pair with Require Alt For Navigation off for click-anywhere-then-drag orbiting.")]
    public bool pivotAtClickPoint = true;

    [Tooltip("How far back F sits from the selection, as a multiple of its size.")]
    public float framePadding = 2f;

    [Header("Debug")]
    [Tooltip("Log what the camera is receiving each time a mouse button goes down. For diagnosing 'it does nothing'.")]
    public bool logInput;

    float m_Yaw;
    float m_Pitch;
    float m_TargetYaw;
    float m_TargetPitch;
    float m_YawVelocity;
    float m_PitchVelocity;
    float m_Distance;
    float m_TargetDistance;
    float m_DistanceVelocity;
    Vector3 m_Pivot;
    Vector3 m_TargetPivot;
    Vector3 m_PivotVelocity;
    Vector2 m_LastMousePos;
    Vector2 m_PressPos0;
    Vector2 m_PressPos1;
    bool m_HasLastMousePos;
    Renderer m_Selection;

    const float k_ClickSlopSqr = 16f; // 4 px of wobble still counts as a click, not a drag.

    void Start()
    {
        m_Pivot = target != null ? target.position : focusPoint;
        m_Distance = Vector3.Distance(transform.position, m_Pivot);

        if (autoFrameOnStart && target == null && TryGetSceneBounds(out var bounds))
        {
            m_Pivot = bounds.center;
            focusPoint = m_Pivot;
            m_Distance = DistanceForRadius(bounds.extents.magnitude);
        }

        m_Distance = Mathf.Clamp(Mathf.Max(m_Distance, 0.01f), minDistance, maxDistance);
        m_TargetDistance = m_Distance;
        m_TargetPivot = m_Pivot;

        // Start from whatever angle the camera was authored at, so entering play mode isn't a jump cut.
        var toCamera = transform.position - m_Pivot;
        if (toCamera.sqrMagnitude > 0.0001f)
        {
            var euler = Quaternion.LookRotation(-toCamera).eulerAngles;
            m_Pitch = NormalizeAngle(euler.x);
            m_Yaw = euler.y;
        }
        else
        {
            m_Pitch = 20f;
            m_Yaw = 0f;
        }

        m_Pitch = Mathf.Clamp(m_Pitch, minPitch, maxPitch);
        m_TargetYaw = m_Yaw;
        m_TargetPitch = m_Pitch;
        ApplyTransform();
    }

    void Update()
    {
        if (target != null)
            m_TargetPivot = target.position;

        var mousePos = ReadMousePosition();
        var delta = m_HasLastMousePos ? mousePos - m_LastMousePos : Vector2.zero;
        m_LastMousePos = mousePos;
        m_HasLastMousePos = true;

        if (WasMousePressed(0))
            m_PressPos0 = mousePos;
        if (WasMousePressed(1))
            m_PressPos1 = mousePos;

        var alt = IsAltHeld();
        var navigating = alt || !requireAltForNavigation;
        var fast = IsFastModifierHeld() ? Mathf.Max(fastMultiplier, 1f) : 1f;

        if (logInput && (WasMousePressed(0) || WasMousePressed(1) || WasMousePressed(2)))
        {
            Debug.LogFormat(
                "[OrbitCamera] buttons L={0} R={1} M={2} | alt={3} navigating={4} | mouse={5} | pivot={6} distance={7:0.##}",
                IsMouseHeld(0), IsMouseHeld(1), IsMouseHeld(2), alt, navigating, mousePos, m_TargetPivot, m_TargetDistance);
        }

        // Alt + LMB tumbles. Drag feeds the target angles; the camera eases towards them below.
        if (navigating && IsMouseHeld(0))
        {
            m_TargetYaw += delta.x * tumbleSpeed;
            m_TargetPitch = Mathf.Clamp(m_TargetPitch - delta.y * tumbleSpeed, minPitch, maxPitch);
        }

        // Alt + MMB tracks. Detaches from a followed target so the pivot can move freely.
        var tracking = navigating && IsMouseHeld(2);
        if (tracking)
        {
            if (target != null)
            {
                m_TargetPivot = target.position;
                target = null;
            }

            var scale = trackSpeed * m_Distance * fast;
            m_TargetPivot -= transform.right * (delta.x * scale) + transform.up * (delta.y * scale);
            focusPoint = m_TargetPivot;
        }

        // Alt + RMB dollies.
        var dollying = navigating && IsMouseHeld(1);

        // Zoom is proportional to the current distance, so it stays usable at any scale.
        var zoom = ReadScroll() * zoomSpeed * fast
            + ReadZoomAxis() * zoomKeySpeed * fast * Time.unscaledDeltaTime;

        // Maya dollies in when you drag right or up. Deliberately ignores Shift — this is the fine path.
        if (dollying)
            zoom += (delta.x + delta.y) * dollySpeed;

        if (Mathf.Abs(zoom) > 0.0001f)
        {
            zoom = Mathf.Clamp(zoom, -0.9f, 0.9f);

            // Slide the pivot towards the point under the cursor so zooming closes on what you're looking at.
            if (zoomTowardsCursor && zoom > 0f && !dollying && target == null)
            {
                var cam = GetComponent<Camera>();
                if (cam != null)
                {
                    var ray = cam.ScreenPointToRay(mousePos);
                    m_TargetPivot = Vector3.Lerp(m_TargetPivot, ray.GetPoint(m_Distance), zoom);
                    focusPoint = m_TargetPivot;
                }
            }

            m_TargetDistance = Mathf.Clamp(m_TargetDistance * (1f - zoom), minDistance, maxDistance);
        }

        if (clickToSelect)
        {
            // A click that didn't drag is a pick, not a navigation gesture.
            if (WasMouseReleased(0) && !alt && (mousePos - m_PressPos0).sqrMagnitude < k_ClickSlopSqr)
                SelectUnderMouse(mousePos);

            if (WasMouseReleased(1) && !alt && (mousePos - m_PressPos1).sqrMagnitude < k_ClickSlopSqr)
                SetPivotUnderMouse(mousePos);
        }

        if (WasKeyPressed(NavKey.FrameSelection))
            FrameSelection();
        if (WasKeyPressed(NavKey.FrameAll))
            FrameAll();

        // Everything eases towards its target, so each gesture glides to a stop instead of cutting dead.
        var dt = Time.unscaledDeltaTime;

        // While tracking, the pivot follows the mouse on its own (shorter) time constant.
        var pivotSmoothTime = tracking ? trackSmoothTime : zoomSmoothTime;

        m_Yaw = tumbleSmoothTime > 0f
            ? Mathf.SmoothDampAngle(m_Yaw, m_TargetYaw, ref m_YawVelocity, tumbleSmoothTime, Mathf.Infinity, dt)
            : m_TargetYaw;

        m_Pitch = tumbleSmoothTime > 0f
            ? Mathf.SmoothDamp(m_Pitch, m_TargetPitch, ref m_PitchVelocity, tumbleSmoothTime, Mathf.Infinity, dt)
            : m_TargetPitch;

        m_Distance = zoomSmoothTime > 0f
            ? Mathf.SmoothDamp(m_Distance, m_TargetDistance, ref m_DistanceVelocity, zoomSmoothTime, Mathf.Infinity, dt)
            : m_TargetDistance;

        m_Pivot = pivotSmoothTime > 0f
            ? Vector3.SmoothDamp(m_Pivot, m_TargetPivot, ref m_PivotVelocity, pivotSmoothTime, Mathf.Infinity, dt)
            : m_TargetPivot;

        ApplyTransform();
    }

    void ApplyTransform()
    {
        var rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        transform.SetPositionAndRotation(m_Pivot + rotation * (Vector3.back * m_Distance), rotation);
    }

    /// <summary>Pick the renderer under the cursor and tumble about it, without moving the camera.</summary>
    void SelectUnderMouse(Vector2 screenPos)
    {
        var hit = RaycastRenderer(screenPos, out _);
        if (hit == null)
            return;

        m_Selection = hit;

        // Orbiting the exact spot clicked is what "rotate around this point" means in practice;
        // the object's centre can sit far behind the surface on a large mesh.
        if (pivotAtClickPoint)
        {
            SetPivotUnderMouse(screenPos);
            return;
        }

        SetPivotPreservingCamera(hit.bounds.center);
    }

    /// <summary>Move the pivot to the exact point clicked. The camera itself does not move.</summary>
    void SetPivotUnderMouse(Vector2 screenPos)
    {
        var cam = GetComponent<Camera>();
        if (cam == null)
            return;

        // A collider gives the true surface point; renderer bounds cover collider-less imported art.
        var ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out var physicsHit, maxDistance))
        {
            SetPivotPreservingCamera(physicsHit.point);
            return;
        }

        if (RaycastRenderer(screenPos, out var distance) == null)
            return;

        SetPivotPreservingCamera(ray.GetPoint(distance));
    }

    /// <summary>
    /// Re-pivot with NO visible change: same position, same rotation, same view. Only the depth the
    /// camera orbits around changes.
    ///
    /// ApplyTransform derives the camera position from pivot + angles + distance. Assigning the clicked
    /// point directly as the pivot therefore slides the camera across the scene, and even re-deriving
    /// the angles to hold the position still swings the view round to face the new pivot — both read as
    /// the camera jumping.
    ///
    /// So the pivot is placed on the camera's own view axis, at the depth of whatever was clicked:
    /// yaw, pitch and position are all untouched, and the only thing that changes is how far away the
    /// point you tumble around sits. Click a near object and it orbits tightly; click something distant
    /// and it swings wide.
    /// </summary>
    public void SetPivotPreservingCamera(Vector3 clickedPoint)
    {
        var cameraPosition = transform.position;
        var forward = transform.forward;

        // Depth of the clicked point along the view axis — never behind the camera.
        var depth = Vector3.Dot(clickedPoint - cameraPosition, forward);
        if (depth < minDistance)
            return;

        target = null;

        var pivot = cameraPosition + forward * depth;
        focusPoint = pivot;
        m_Pivot = m_TargetPivot = pivot;
        m_Distance = m_TargetDistance = Mathf.Clamp(depth, minDistance, maxDistance);

        // Angles are deliberately left alone — they already describe this exact view.
        m_TargetYaw = m_Yaw;
        m_TargetPitch = m_Pitch;

        // Drop any easing in flight, or the view would drift after the click.
        m_YawVelocity = 0f;
        m_PitchVelocity = 0f;
        m_DistanceVelocity = 0f;
        m_PivotVelocity = Vector3.zero;
    }

    /// <summary>F — frame the last-clicked object, or everything if nothing is selected.</summary>
    public void FrameSelection()
    {
        if (m_Selection == null)
        {
            FrameAll();
            return;
        }

        FrameBounds(m_Selection.bounds);
    }

    /// <summary>A — pull back to see every renderer in the scene.</summary>
    public void FrameAll()
    {
        if (TryGetSceneBounds(out var bounds))
            FrameBounds(bounds);
    }

    void FrameBounds(Bounds bounds)
    {
        target = null;
        m_TargetPivot = bounds.center;
        focusPoint = m_TargetPivot;
        m_TargetDistance = Mathf.Clamp(DistanceForRadius(bounds.extents.magnitude), minDistance, maxDistance);
    }

    /// <summary>Nearest renderer whose bounds the cursor ray crosses. No colliders required.</summary>
    Renderer RaycastRenderer(Vector2 screenPos, out float distance)
    {
        distance = 0f;

        var cam = GetComponent<Camera>();
        if (cam == null)
            return null;

        var ray = cam.ScreenPointToRay(screenPos);
        Renderer nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var r in FindRenderers())
        {
            if (r.enabled && r.bounds.IntersectRay(ray, out var d) && d < nearestDistance)
            {
                nearestDistance = d;
                nearest = r;
            }
        }

        if (nearest != null)
            distance = nearestDistance;

        return nearest;
    }

    /// <summary>Distance at which a sphere of this radius fills the frame, plus padding.</summary>
    float DistanceForRadius(float radius)
    {
        radius = Mathf.Max(radius, 0.1f);

        var cam = GetComponent<Camera>();
        var fov = cam != null && !cam.orthographic ? cam.fieldOfView : 60f;
        return radius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * Mathf.Max(framePadding, 1f) * 0.6f;
    }

    static Renderer[] FindRenderers()
    {
#if UNITY_2022_2_OR_NEWER
        return Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<Renderer>();
#endif
    }

    static bool TryGetSceneBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        var found = false;

        foreach (var r in FindRenderers())
        {
            if (!r.enabled)
                continue;

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return found;
    }

    static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        return degrees > 180f ? degrees - 360f : degrees;
    }

    enum NavKey
    {
        FrameSelection,
        FrameAll
    }

    static bool WasKeyPressed(NavKey key)
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null)
            return false;

        return key == NavKey.FrameSelection ? k.fKey.wasPressedThisFrame : k.aKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(key == NavKey.FrameSelection ? KeyCode.F : KeyCode.A);
#endif
    }

    static Vector2 ReadMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    static bool IsMouseHeld(int button)
    {
#if ENABLE_INPUT_SYSTEM
        var b = GetButton(button);
        return b != null && b.isPressed;
#else
        return Input.GetMouseButton(button);
#endif
    }

    static bool WasMousePressed(int button)
    {
        // A press that lands on a demo panel belongs to that panel. Without this, clicking a mode
        // button also click-to-focuses the camera and the view jumps to a new pivot every press.
        // Only the initial press is blocked, so a drag started over the scene still works normally.
        if (DemoUIBlocker.PointerOver(ReadMousePosition()))
            return false;

#if ENABLE_INPUT_SYSTEM
        var b = GetButton(button);
        return b != null && b.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(button);
#endif
    }

    static bool WasMouseReleased(int button)
    {
        // Click-to-pivot fires on RELEASE, so this needs the same UI guard as the press — otherwise
        // clicking a demo panel button still re-pivots the camera behind the panel.
        if (DemoUIBlocker.PointerOver(ReadMousePosition()))
            return false;

#if ENABLE_INPUT_SYSTEM
        var b = GetButton(button);
        return b != null && b.wasReleasedThisFrame;
#else
        return Input.GetMouseButtonUp(button);
#endif
    }

#if ENABLE_INPUT_SYSTEM
    static UnityEngine.InputSystem.Controls.ButtonControl GetButton(int button)
    {
        var mouse = Mouse.current;
        if (mouse == null)
            return null;

        switch (button)
        {
            case 0: return mouse.leftButton;
            case 1: return mouse.rightButton;
            case 2: return mouse.middleButton;
            default: return null;
        }
    }
#endif

    /// <summary>+1 zooms in, -1 zooms out. Keyboard fallback for when the wheel isn't handy.</summary>
    static float ReadZoomAxis()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null)
            return 0f;

        var inHeld = k.equalsKey.isPressed || k.numpadPlusKey.isPressed || k.upArrowKey.isPressed;
        var outHeld = k.minusKey.isPressed || k.numpadMinusKey.isPressed || k.downArrowKey.isPressed;
#else
        var inHeld = Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.Plus)
            || Input.GetKey(KeyCode.KeypadPlus) || Input.GetKey(KeyCode.UpArrow);
        var outHeld = Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.Underscore)
            || Input.GetKey(KeyCode.KeypadMinus) || Input.GetKey(KeyCode.DownArrow);
#endif
        return (inHeld ? 1f : 0f) - (outHeld ? 1f : 0f);
    }

    static bool IsAltHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && (k.leftAltKey.isPressed || k.rightAltKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#endif
    }

    static bool IsFastModifierHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }

    static float ReadScroll()
    {
#if ENABLE_INPUT_SYSTEM
        // The new Input System reports scroll in 120-unit notches; the legacy class reports 1 per notch.
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
        return Input.mouseScrollDelta.y;
#endif
    }
}
