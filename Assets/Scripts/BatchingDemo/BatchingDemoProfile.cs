using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One rendering mode, described as data rather than code. Every switch the demo can flip is a field
/// here, so the truth table is visible in the Inspector and on screen — nothing is hidden in a method.
/// </summary>
[Serializable]
public class BatchingModeSpec
{
    public string displayName = "New Mode";

    [TextArea(2, 4)]
    public string description = "";

    [Header("Switches")]
    [Tooltip("GraphicsSettings + the URP asset's SRP Batcher checkbox.")]
    public bool srpBatcher;

    [Tooltip("Enable GPU Instancing on the target group's materials.")]
    public bool gpuInstancing;

    [Tooltip("Mark every renderer in the target group as Batching Static.")]
    public bool batchingStatic;

    [Tooltip("Player Settings > Other Settings > Static Batching.")]
    public bool playerStaticBatching;

    [Tooltip("Dynamic batching. Under URP the URP asset's checkbox is the one that counts.")]
    public bool dynamicBatching;

    [Tooltip("URP GPU Resident Drawer (Instanced Drawing). Requires SRP Batcher on and a Forward+ renderer.")]
    public bool gpuResidentDrawer;

    [Tooltip("Turn the scene's rotators off for this mode. Static batching bakes geometry in place, so " +
             "leaving the animation 'on' would claim movement that cannot happen.")]
    public bool disableAnimation;

    /// <summary>
    /// Does the live project state match this mode exactly? Every switch the mode sets is compared,
    /// including the Player Settings one — a mode whose global half is applied and whose per-renderer
    /// half is not must report as CUSTOM rather than as the nearest mode.
    /// </summary>
    public bool Matches(bool srp, bool instancing, bool markedStatic, bool playerStatic,
                        bool dynamic, bool resident)
    {
        return srp == srpBatcher
            && instancing == gpuInstancing
            && markedStatic == batchingStatic
            && playerStatic == playerStaticBatching
            && dynamic == dynamicBatching
            && resident == gpuResidentDrawer;
    }
}

/// <summary>
/// The demo's mode list. Applied in EDIT mode, then you press Play — which is how Unity really does
/// static batching (at scene load, exactly like a build), so what the audience sees is the genuine
/// mechanism rather than a runtime approximation of it.
/// </summary>
[CreateAssetMenu(fileName = "BatchingDemoProfile", menuName = "Rendering Demo/Batching Demo Profile")]
public class BatchingDemoProfile : ScriptableObject
{
    public List<BatchingModeSpec> modes = new List<BatchingModeSpec>();

    /// <summary>The six modes the talk uses, in running order. Used to fill a fresh profile.</summary>
    public void ResetToDefaults()
    {
        modes = new List<BatchingModeSpec>
        {
            new BatchingModeSpec
            {
                displayName = "No Optimization",
                description = "Everything off — SRP Batcher, instancing, static and dynamic batching, " +
                              "GPU Resident Drawer. One draw call per renderer. This is the control " +
                              "every other mode is measured against.",
                // every switch deliberately false
            },
            new BatchingModeSpec
            {
                displayName = "SRP Batcher",
                description = "The scene default. SRP Batcher keeps shader and material data on the GPU " +
                              "between draws — it does NOT reduce the draw call count.",
                srpBatcher = true
            },
            new BatchingModeSpec
            {
                displayName = "GPU Instancing",
                description = "One mesh, one material, many instances submitted in a single instanced draw. " +
                              "SRP Batcher is off, because it takes priority and nothing would instance.",
                gpuInstancing = true
            },
            new BatchingModeSpec
            {
                displayName = "Static Batching",
                description = "Every renderer is marked Batching Static, so Unity merges them into one " +
                              "combined mesh at scene load. Moving geometry can never be static-batched.",
                batchingStatic = true,
                playerStaticBatching = true,
                disableAnimation = true
            },
            new BatchingModeSpec
            {
                displayName = "Dynamic Batching",
                description = "Unity merges small moving meshes on the CPU every frame. Only meshes under " +
                              "roughly 300 vertices qualify, which rules out most production art.",
                dynamicBatching = true
            },
            new BatchingModeSpec
            {
                displayName = "GPU Resident Drawer",
                description = "GPU-driven rendering on top of the SRP Batcher and material instancing. " +
                              "Not the same as GPU instancing — the Frame Debugger shows a Hybrid Batch Group.",
                srpBatcher = true,
                gpuInstancing = true,
                gpuResidentDrawer = true
            }
        };
    }
}
