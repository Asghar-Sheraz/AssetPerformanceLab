using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Screen areas owned by the demo's IMGUI panels, so the orbit camera can ignore clicks that land on
/// them.
///
/// Without this, pressing a mode button also reaches OrbitCameraController, which click-to-focuses on
/// whatever is behind the panel and swings the view to a new pivot — the camera "jumping from one point
/// to the next" every time a button is pressed.
///
/// Rects are stored in SCREEN space with y measured from the TOP (IMGUI convention). Each panel keeps
/// its own slot, so panels can register and unregister independently without stepping on each other.
/// </summary>
public static class DemoUIBlocker
{
    static readonly Dictionary<int, Rect> s_Rects = new Dictionary<int, Rect>();

    /// <summary>Claim a screen-space rect (y from the top). Call every frame the panel is drawn.</summary>
    public static void Set(int id, Rect screenRect)
    {
        s_Rects[id] = screenRect;
    }

    /// <summary>Release a rect — call when the panel is hidden or destroyed.</summary>
    public static void Clear(int id)
    {
        s_Rects.Remove(id);
    }

    /// <summary>True when the given pointer position (y from the BOTTOM, as Input reports it) is over a panel.</summary>
    public static bool PointerOver(Vector2 pointerFromBottom)
    {
        if (s_Rects.Count == 0)
            return false;

        var p = new Vector2(pointerFromBottom.x, Screen.height - pointerFromBottom.y);

        foreach (var rect in s_Rects.Values)
            if (rect.Contains(p))
                return true;

        return false;
    }
}
