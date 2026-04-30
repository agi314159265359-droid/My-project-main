using System.Linq;
using UnityEngine;

namespace Mikk.Avatar
{
    public class LipSyncDriver : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer faceMesh;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private OVRLipSyncContextBase lipsyncContext;

        [Header("Settings — Match Your Old Working Values")]
        //  [SerializeField, Range(0f, 3f)] private float visemeIntensityMultiplier = 1.8f;
        [SerializeField, Range(1, 100)] private int smoothAmount = 70;

        [Header("Laughter")]
        [SerializeField] private int laughterBlendTarget = -1;
        [SerializeField, Range(0f, 1f)] private float laughterThreshold = 0.5f;
        [SerializeField, Range(0f, 3f)] private float laughterMultiplier = 1.5f;

        [Header("Viseme Mapping")]
        [Tooltip("Maps OVR viseme index → blendshape index on mesh. " +
                 "Default uses indices 0-14 matching your old working setup.")]
        [SerializeField] private int[] visemeToBlendTargets;

        private const float UPDATE_INTERVAL = 1f / 30f;
        private float _lastUpdate;
        private OVRLipSync.Frame _frame;

        public bool IsActive => audioSource != null && audioSource.isPlaying;

        public static readonly System.Collections.Generic.HashSet<string> ControlledBlendshapes = new()
        {
            "mouthOpen",
            "viseme_sil", "viseme_PP", "viseme_FF", "viseme_TH",
            "viseme_DD", "viseme_kk", "viseme_CH", "viseme_SS",
            "viseme_nn", "viseme_RR", "viseme_aa", "viseme_E",
            "viseme_I", "viseme_O", "viseme_U",
            "mouthSmile",
            "jawOpen", "mouthClose", "mouthFunnel", "mouthPucker",
            "mouthLeft", "mouthRight", "mouthSmileLeft", "mouthSmileRight",
            "mouthFrownLeft", "mouthFrownRight", "mouthStretchLeft", "mouthStretchRight",
            "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
            "mouthPressLeft", "mouthPressRight", "mouthLowerDownLeft", "mouthLowerDownRight",
            "mouthUpperUpLeft", "mouthUpperUpRight", "mouthDimpleLeft", "mouthDimpleRight",
        };

        private void Start()
        {
            if (lipsyncContext != null)
                lipsyncContext.Smoothing = smoothAmount;

            // ═══ DEFAULT MAPPING — EXACTLY LIKE YOUR OLD CODE ═══
            // Your old code: Enumerable.Range(0, OVRLipSync.VisemeCount).ToArray()
            // This maps viseme[0] → blendshape 0, viseme[1] → blendshape 1, etc.
            if (visemeToBlendTargets == null || visemeToBlendTargets.Length == 0)
            {
                visemeToBlendTargets = Enumerable.Range(0, OVRLipSync.VisemeCount).ToArray();
                Debug.Log($"[LipSync] Using default mapping: {string.Join(",", visemeToBlendTargets)}");
            }
        }

        /// <summary>
        /// Apply lipsync — EXACTLY matching your old Playaudio.cs logic
        /// </summary>
        public void ApplyLipSync()
        {
            if (!IsActive || lipsyncContext == null || faceMesh == null) return;
            if (Time.time - _lastUpdate < UPDATE_INTERVAL) return;
            _lastUpdate = Time.time;

            _frame = lipsyncContext.GetCurrentPhonemeFrame();
            if (_frame == null) return;

            // ═══ VISEMES — Same as your old SetVisemeToMorphTarget ═══
            for (int i = 0; i < visemeToBlendTargets.Length; i++)
            {
                if (visemeToBlendTargets[i] != -1 && i < _frame.Visemes.Length)
                {
                    float weight = _frame.Visemes[i];

                    // Apply intensity multiplier — SAME as your old code
                    //   weight *= visemeIntensityMultiplier;

                    // Clamp to 0-1 for GLB
                    weight = Mathf.Clamp(weight, 0f, 1f);

                    faceMesh.SetBlendShapeWeight(visemeToBlendTargets[i], weight);
                }
            }

            // ═══ LAUGHTER — Same as your old SetLaughterToMorphTarget ═══
            if (laughterBlendTarget != -1)
            {
                float laughterScore = _frame.laughterScore;
                laughterScore = laughterScore < laughterThreshold ? 0f : laughterScore - laughterThreshold;
                laughterScore = Mathf.Min(laughterScore * laughterMultiplier, 1f);
                laughterScore *= 1f / laughterThreshold;

                faceMesh.SetBlendShapeWeight(laughterBlendTarget, laughterScore);
            }
        }

        public void ResetMouth()
        {
            if (faceMesh == null) return;

            for (int i = 0; i < visemeToBlendTargets.Length; i++)
            {
                if (visemeToBlendTargets[i] != -1)
                    faceMesh.SetBlendShapeWeight(visemeToBlendTargets[i], 0f);
            }

            if (laughterBlendTarget != -1)
                faceMesh.SetBlendShapeWeight(laughterBlendTarget, 0f);
        }
    }
}

