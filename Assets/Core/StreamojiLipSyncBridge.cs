using System.Collections.Generic;
using UnityEngine;



public class StreamojiLipSyncBridge : MonoBehaviour
{
    [Header("Setup")]
    public SkinnedMeshRenderer skinnedMeshRenderer;

    [Header("Smoothing")]
    [Range(1, 100)]
    public int smoothAmount = 70;

    // ── TTS state — set by VoicePipeline ─────────────────────────────
    private bool _ttsActive = false;

    public bool IsActive => _ttsActive;

    public void NotifyTTSStarted()
    {
        _ttsActive = true;
        Debug.Log("[LipSync] TTS started");
    }

    public void NotifyTTSStopped()
    {
        _ttsActive = false;
        ResetMouth();
        Debug.Log("[LipSync] TTS stopped");
    }

    // Blendshape names that lipsync controls
    // RealtimeFaceDriver uses this to avoid overwriting them during speech
    public static readonly HashSet<string> ControlledBlendshapes =
        new HashSet<string>
    {
        "jawOpen", "jawForward",
        "mouthPressLeft", "mouthPressRight",
        "mouthDimpleLeft", "mouthDimpleRight",
        "mouthPucker", "mouthFunnel",
        "mouthRollLower", "mouthRollUpper",
        "mouthUpperUpLeft", "mouthUpperUpRight",
        "mouthLowerDownLeft", "mouthLowerDownRight",
        "mouthShrugUpper", "tongueOut",
    };

    public void ResetMouth()
    {
        if (skinnedMeshRenderer == null) return;
        foreach (string shapeName in allUsedBlendShapes)
        {
            if (blendShapeIndexCache.TryGetValue(shapeName, out int idx))
                skinnedMeshRenderer.SetBlendShapeWeight(idx, 0f);
        }
    }

    // ── Everything below is your original code, unchanged ────────────

    [SerializeField] private OVRLipSyncContextBase lipsyncContext;
    private Dictionary<string, int> blendShapeIndexCache = new Dictionary<string, int>();
    private Dictionary<string, float> currentWeights = new Dictionary<string, float>();

    private static readonly Dictionary<string, float>[] visemeMixMap =
        new Dictionary<string, float>[]
    {
        new Dictionary<string, float> { },
        new Dictionary<string, float> { { "mouthRollLower", 0.3f }, { "mouthRollUpper", 0.3f }, { "mouthUpperUpLeft", 0.3f }, { "mouthUpperUpRight", 0.3f } },
        new Dictionary<string, float> { { "mouthPucker", 1.0f }, { "mouthShrugUpper", 1.0f }, { "mouthLowerDownLeft", 0.2f }, { "mouthLowerDownRight", 0.2f }, { "mouthDimpleLeft", 1.0f }, { "mouthDimpleRight", 1.0f }, { "mouthRollLower", 0.3f } },
        new Dictionary<string, float> { { "mouthRollUpper", 0.3f }, { "jawOpen", 0.2f }, { "tongueOut", 0.4f } },
        new Dictionary<string, float> { { "mouthPressLeft", 0.8f }, { "mouthPressRight", 0.8f }, { "mouthFunnel", 0.5f }, { "jawOpen", 0.2f } },
        new Dictionary<string, float> { { "mouthLowerDownLeft", 0.4f }, { "mouthLowerDownRight", 0.4f }, { "mouthDimpleLeft", 0.3f }, { "mouthDimpleRight", 0.3f }, { "mouthFunnel", 0.3f }, { "mouthPucker", 0.3f }, { "jawOpen", 0.15f } },
        new Dictionary<string, float> { { "mouthPucker", 0.5f }, { "jawOpen", 0.2f } },
        new Dictionary<string, float> { { "mouthPressLeft", 0.8f }, { "mouthPressRight", 0.8f }, { "mouthLowerDownLeft", 0.5f }, { "mouthLowerDownRight", 0.5f }, { "jawOpen", 0.1f } },
        new Dictionary<string, float> { { "mouthLowerDownLeft", 0.4f }, { "mouthLowerDownRight", 0.4f }, { "mouthDimpleLeft", 0.3f }, { "mouthDimpleRight", 0.3f }, { "mouthFunnel", 0.3f }, { "mouthPucker", 0.3f }, { "jawOpen", 0.15f }, { "tongueOut", 0.2f } },
        new Dictionary<string, float> { { "mouthPucker", 0.5f }, { "jawOpen", 0.2f } },
        new Dictionary<string, float> { { "jawOpen", 0.6f } },
        new Dictionary<string, float> { { "mouthPressLeft", 0.8f }, { "mouthPressRight", 0.8f }, { "mouthDimpleLeft", 1.0f }, { "mouthDimpleRight", 1.0f }, { "jawOpen", 0.3f } },
        new Dictionary<string, float> { { "mouthPressLeft", 0.6f }, { "mouthPressRight", 0.6f }, { "mouthDimpleLeft", 0.6f }, { "mouthDimpleRight", 0.6f }, { "jawOpen", 0.2f } },
        new Dictionary<string, float> { { "mouthPucker", 1.0f }, { "jawForward", 0.6f }, { "jawOpen", 0.2f } },
        new Dictionary<string, float> { { "mouthFunnel", 1.0f } },
    };

    private static readonly string[] allUsedBlendShapes = new string[]
    {
        "jawOpen", "jawForward",
        "mouthPressLeft", "mouthPressRight",
        "mouthDimpleLeft", "mouthDimpleRight",
        "mouthPucker", "mouthFunnel",
        "mouthRollLower", "mouthRollUpper",
        "mouthUpperUpLeft", "mouthUpperUpRight",
        "mouthLowerDownLeft", "mouthLowerDownRight",
        "mouthShrugUpper", "tongueOut",
    };

    void Start()
    {

        lipsyncContext = transform.parent.GetComponentInChildren<OVRLipSyncContext>();

        if (lipsyncContext == null)
        {
            Debug.LogError("StreamojiLipSyncBridge: No OVRLipSyncContext found!");
            return;
        }

        lipsyncContext.Smoothing = smoothAmount;
        CacheBlendShapeIndices();
    }




  public void CacheBlendShapeIndices()
    {
        Mesh mesh = skinnedMeshRenderer.sharedMesh;
        foreach (string shapeName in allUsedBlendShapes)
        {
            int index = mesh.GetBlendShapeIndex(shapeName);
            if (index >= 0)
            {
                blendShapeIndexCache[shapeName] = index;
                currentWeights[shapeName] = 0f;
                Debug.Log($"Cached blend shape: {shapeName} at index {index}");
            }
            else
            {
                Debug.LogWarning($"Blend shape NOT FOUND: {shapeName}");
            }
        }
        Debug.Log($"Cached {blendShapeIndexCache.Count}/{allUsedBlendShapes.Length} blend shapes");
    }

    void Update()
    {
        // Only run viseme processing when TTS is actually playing
        if (!_ttsActive) return;

        if (lipsyncContext == null || skinnedMeshRenderer == null) return;

        OVRLipSync.Frame frame = lipsyncContext.GetCurrentPhonemeFrame();
        if (frame == null) return;

        if (smoothAmount != lipsyncContext.Smoothing)
            lipsyncContext.Smoothing = smoothAmount;

        var targetWeights = new Dictionary<string, float>();
        foreach (string shapeName in allUsedBlendShapes)
            targetWeights[shapeName] = 0f;

        for (int i = 0; i < frame.Visemes.Length && i < visemeMixMap.Length; i++)
        {
            float visemeWeight = frame.Visemes[i];
            if (visemeWeight < 0.01f) continue;

            foreach (var pair in visemeMixMap[i])
            {
                if (targetWeights.ContainsKey(pair.Key))
                    targetWeights[pair.Key] += visemeWeight * pair.Value;
            }
        }

        foreach (var pair in targetWeights)
        {
            if (!blendShapeIndexCache.ContainsKey(pair.Key)) continue;
            skinnedMeshRenderer.SetBlendShapeWeight(
                blendShapeIndexCache[pair.Key],
                Mathf.Clamp01(pair.Value));
        }
    }

    void LateUpdate() { }
}