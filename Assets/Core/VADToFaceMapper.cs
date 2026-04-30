using System.Collections.Generic;
using UnityEngine;

namespace Mikk.Avatar
{
    public class VADToFaceMapper : MonoBehaviour
    {
        [Header("Tuning")]
        [SerializeField, Range(0.3f, 1.2f)] private float expressionStrength = 0.85f;
        [SerializeField, Range(0f, 0.08f)] private float asymmetryAmount = 0.05f;
        [SerializeField, Range(0f, 0.05f)] private float microVariation = 0.03f;

        /// <summary>
        /// Convert VAD emotional coordinates to a unique facial pose.
        /// No two calls return exactly the same result.
        /// </summary>
        /// 
/*
        public FacialPose MapToFace(EmotionVAD vad)
        {
            var pose = new FacialPose();

            float v = vad.Valence;
            float a = vad.Arousal;
            float d = vad.Dominance;
            float str = expressionStrength;

            // ═══════ MOUTH ═══════

            // Smile
            if (v > 0)
            {
                float smile = v * str * Remap(a, -1, 1, 0.6f, 1.2f);
                smile = Mathf.Clamp01(smile);
                Set(pose, "mouthSmileLeft", smile);
                Set(pose, "mouthSmileRight", smile);

                // Duchenne markers
                if (v > 0.3f)
                {
                    float cheek = (v - 0.3f) / 0.7f * 0.6f * str;
                    Set(pose, "cheekSquintLeft", cheek);
                    Set(pose, "cheekSquintRight", cheek);
                }
                if (v > 0.4f)
                {
                    float dimple = (v - 0.4f) / 0.6f * 0.3f * str;
                    Set(pose, "mouthDimpleLeft", dimple);
                    Set(pose, "mouthDimpleRight", dimple);
                }
            }

            // Frown / Press
            if (v < 0)
            {
                float neg = -v * str;

                if (d < 0)
                {
                    // Sad: corners down
                    float frown = neg * Remap(d, 0, -1, 0.7f, 1.2f);
                    Set(pose, "mouthFrownLeft", Mathf.Clamp01(frown));
                    Set(pose, "mouthFrownRight", Mathf.Clamp01(frown));
                }
                else
                {
                    // Angry: press together
                    float press = neg * d * str;
                    Set(pose, "mouthPressLeft", Mathf.Clamp01(press));
                    Set(pose, "mouthPressRight", Mathf.Clamp01(press));
                    Set(pose, "mouthFrownLeft", Mathf.Clamp01(neg * 0.4f));
                    Set(pose, "mouthFrownRight", Mathf.Clamp01(neg * 0.4f));
                }
            }

            // Jaw open: high arousal
            if (a > 0.5f)
            {
                float jaw = (a - 0.5f) / 0.5f;
                if (v > 0.3f) jaw *= 0.3f;       // Less jaw for happy
                else if (v < -0.3f) jaw *= 0.5f;  // More for shock
                Set(pose, "jawOpen", Mathf.Clamp01(jaw * str));
            }

            // Lip pucker: thinking
            if (Mathf.Abs(v) < 0.3f && a < 0 && d < 0.2f)
            {
                float pucker = (-a) * (1f - Mathf.Abs(v) * 2f) * 0.3f;
                Set(pose, "mouthPucker", Mathf.Clamp01(pucker));
            }

            // Mouth shift: confusion
            if (d < -0.2f && Mathf.Abs(v) < 0.4f)
            {
                float shift = (-d - 0.2f) / 0.8f * 0.2f;
                Set(pose, "mouthLeft", Mathf.Clamp01(shift));
            }

            // Upper lip snarl: disgust
            if (v < -0.3f && d > 0.2f)
            {
                float snarl = (-v - 0.3f) * (d - 0.2f) * str;
                Set(pose, "mouthUpperUpLeft", Mathf.Clamp01(snarl * 0.6f));
                Set(pose, "mouthUpperUpRight", Mathf.Clamp01(snarl * 0.5f));
            }

            // Lip stretch: fear / nervousness
            if (v < 0 && a > 0.3f && d < -0.2f)
            {
                float stretch = (-v) * (a - 0.3f) * (-d - 0.2f) * str;
                Set(pose, "mouthStretchLeft", Mathf.Clamp01(stretch * 0.5f));
                Set(pose, "mouthStretchRight", Mathf.Clamp01(stretch * 0.5f));
            }

            // ═══════ EYEBROWS ═══════

            // Inner brow raise: surprise, worry, greeting
            {
                float browUp = 0;
                if (a > 0.3f) browUp += (a - 0.3f) / 0.7f * 0.6f;
                if (v < 0 && d < 0) browUp += (-v) * (-d) * 0.5f;
                if (v > 0.3f && a > 0.1f) browUp += (v - 0.3f) * 0.3f;
                Set(pose, "browInnerUp", Mathf.Clamp01(browUp * str));
            }

            // Outer brow raise: surprise, questioning
            if (a > 0.3f && d < 0.3f)
            {
                float outer = (a - 0.3f) / 0.7f * 0.5f * str;
                Set(pose, "browOuterUpLeft", Mathf.Clamp01(outer));
                Set(pose, "browOuterUpRight", Mathf.Clamp01(outer));
            }

            // Brow furrow: anger, concentration
            {
                float browDown = 0;
                if (v < 0 && d > 0) browDown += (-v) * d * 0.8f;
                if (Mathf.Abs(v) < 0.2f && a > 0.2f && d > 0) browDown += 0.2f;
                if (d < -0.2f && a > 0.1f) browDown += 0.15f;
                Set(pose, "browDownLeft", Mathf.Clamp01(browDown * str));
                Set(pose, "browDownRight", Mathf.Clamp01(browDown * str));
            }

            // ═══════ EYES ═══════

            // Eye widen: surprise, fear
            if (a > 0.3f && d < 0.3f)
            {
                float widen = (a - 0.3f) / 0.7f;
                if (v < 0) widen *= 1.2f;
                Set(pose, "eyeWideLeft", Mathf.Clamp01(widen * str));
                Set(pose, "eyeWideRight", Mathf.Clamp01(widen * str));
            }

            // Eye squint: happiness, anger, amusement
            {
                float squint = 0;
                if (v > 0.3f) squint += (v - 0.3f) / 0.7f * 0.5f;
                if (v < -0.2f && d > 0.2f) squint += (-v) * d * 0.4f;
                if (v > 0.2f && a > 0.2f) squint += 0.1f;
                Set(pose, "eyeSquintLeft", Mathf.Clamp01(squint * str));
                Set(pose, "eyeSquintRight", Mathf.Clamp01(squint * str));
            }

            // Eye direction
            {
                // Thinking: look up
                if (Mathf.Abs(v) < 0.3f && a < 0 && d < 0.3f)
                {
                    float up = (-a) * 0.4f;
                    Set(pose, "eyeLookUpLeft", Mathf.Clamp01(up));
                    Set(pose, "eyeLookUpRight", Mathf.Clamp01(up * 0.8f));
                }

                // Sad: look down
                if (v < -0.2f && d < -0.2f)
                {
                    float down = (-v) * (-d) * 0.4f;
                    Set(pose, "eyeLookDownLeft", Mathf.Clamp01(down));
                    Set(pose, "eyeLookDownRight", Mathf.Clamp01(down));
                }
            }

            // ═══════ NOSE ═══════

            if (v < -0.2f && d > 0)
            {
                float sneer = (-v - 0.2f) * (d + 0.5f) * 0.4f * str;
                Set(pose, "noseSneerLeft", Mathf.Clamp01(sneer));
                Set(pose, "noseSneerRight", Mathf.Clamp01(sneer * 0.85f));
            }

            // ═══════ CHEEK PUFF ═══════

            if (v < -0.2f && a < 0.2f && d > -0.3f)
            {
                float puff = (-v - 0.2f) * 0.3f;
                Set(pose, "cheekPuff", Mathf.Clamp01(puff));
            }

            // ═══════ FINAL TOUCHES ═══════
            ApplyAsymmetry(pose);

            return pose;
        }*/


        public FacialPose MapToFace(EmotionVAD vad)
        {
            var pose = new FacialPose();

            float v = vad.Valence;
            float a = vad.Arousal;
            float d = vad.Dominance;
            float str = expressionStrength;

            // Boost expression strength for strong negative emotions
            // Angry/sad at full intensity can reach str * 1.3, capped at 1.1
            if (v < -0.3f)
                str = Mathf.Lerp(expressionStrength,
                    Mathf.Min(expressionStrength * 1.3f, 1.1f),
                    Mathf.InverseLerp(-0.3f, -1f, v));

            // ═══════ MOUTH ═══════

            // Smile — unchanged
            if (v > 0)
            {
                float smile = v * str * Remap(a, -1, 1, 0.6f, 1.2f);
                smile = Mathf.Clamp01(smile);
                Set(pose, "mouthSmileLeft", smile);
                Set(pose, "mouthSmileRight", smile);

                if (v > 0.3f)
                {
                    float cheek = (v - 0.3f) / 0.7f * 0.6f * str;
                    Set(pose, "cheekSquintLeft", cheek);
                    Set(pose, "cheekSquintRight", cheek);
                }
                if (v > 0.4f)
                {
                    float dimple = (v - 0.4f) / 0.6f * 0.3f * str;
                    Set(pose, "mouthDimpleLeft", dimple);
                    Set(pose, "mouthDimpleRight", dimple);
                }
            }

            // Frown / Press — FIXED
            if (v < 0)
            {
                float neg = -v * str;

                if (d < 0)
                {
                    // Sad: corners down — unchanged
                    float frown = neg * Remap(d, 0, -1, 0.7f, 1.2f);
                    Set(pose, "mouthFrownLeft", Mathf.Clamp01(frown));
                    Set(pose, "mouthFrownRight", Mathf.Clamp01(frown));
                }
                else
                {
                    // Angry: press + frown — FIXED: no longer gated by d magnitude
                    float angryBase = neg * Mathf.Lerp(0.6f, 1.0f,
                        Mathf.InverseLerp(0f, 1f, d));
                    Set(pose, "mouthPressLeft", Mathf.Clamp01(angryBase));
                    Set(pose, "mouthPressRight", Mathf.Clamp01(angryBase));

                    // Was 0.4f — now 0.75f
                    Set(pose, "mouthFrownLeft", Mathf.Clamp01(neg * 0.75f));
                    Set(pose, "mouthFrownRight", Mathf.Clamp01(neg * 0.75f));
                }
            }

            // Jaw open — unchanged
            if (a > 0.5f)
            {
                float jaw = (a - 0.5f) / 0.5f;
                if (v > 0.3f) jaw *= 0.3f;
                else if (v < -0.3f) jaw *= 0.5f;
                Set(pose, "jawOpen", Mathf.Clamp01(jaw * str));
            }

            // Lip pucker — unchanged
            if (Mathf.Abs(v) < 0.3f && a < 0 && d < 0.2f)
            {
                float pucker = (-a) * (1f - Mathf.Abs(v) * 2f) * 0.3f;
                Set(pose, "mouthPucker", Mathf.Clamp01(pucker));
            }

            // Mouth shift — unchanged
            if (d < -0.2f && Mathf.Abs(v) < 0.4f)
            {
                float shift = (-d - 0.2f) / 0.8f * 0.2f;
                Set(pose, "mouthLeft", Mathf.Clamp01(shift));
            }

            // Snarl — unchanged
            if (v < -0.3f && d > 0.2f)
            {
                float snarl = (-v - 0.3f) * (d - 0.2f) * str;
                Set(pose, "mouthUpperUpLeft", Mathf.Clamp01(snarl * 0.6f));
                Set(pose, "mouthUpperUpRight", Mathf.Clamp01(snarl * 0.5f));
            }

            // Lip stretch — unchanged
            if (v < 0 && a > 0.3f && d < -0.2f)
            {
                float stretch = (-v) * (a - 0.3f) * (-d - 0.2f) * str;
                Set(pose, "mouthStretchLeft", Mathf.Clamp01(stretch * 0.5f));
                Set(pose, "mouthStretchRight", Mathf.Clamp01(stretch * 0.5f));
            }

            // ═══════ EYEBROWS ═══════

            // Inner brow raise — unchanged
            {
                float browUp = 0;
                if (a > 0.3f) browUp += (a - 0.3f) / 0.7f * 0.6f;
                if (v < 0 && d < 0) browUp += (-v) * (-d) * 0.5f;
                if (v > 0.3f && a > 0.1f) browUp += (v - 0.3f) * 0.3f;
                Set(pose, "browInnerUp", Mathf.Clamp01(browUp * str));
            }

            // ADDED: Dedicated sad oblique brow
            // Fires for sad (v<0, a<0.1, d<0) — inner up + slight outer down
            if (v < -0.2f && a < 0.1f && d < 0f)
            {
                float sadBrow = (-v) * Mathf.Lerp(0.3f, 0.8f,
                    Mathf.InverseLerp(-0.2f, -1f, v));
                Set(pose, "browInnerUp", Mathf.Clamp01(sadBrow * 0.7f));
                Set(pose, "browDownLeft", Mathf.Clamp01(sadBrow * 0.4f));
                Set(pose, "browDownRight", Mathf.Clamp01(sadBrow * 0.4f));
            }

            // Outer brow raise — unchanged
            if (a > 0.3f && d < 0.3f)
            {
                float outer = (a - 0.3f) / 0.7f * 0.5f * str;
                Set(pose, "browOuterUpLeft", Mathf.Clamp01(outer));
                Set(pose, "browOuterUpRight", Mathf.Clamp01(outer));
            }

            // Brow furrow — unchanged
            {
                float browDown = 0;

                // Angry furrow — FIXED: neg drives base, d only adds extra
                // Before: 0.5 * 0.2 * 0.8 = 0.08 (invisible)
                // After:  0.5 * Lerp(0.5, 1.0, 0.2) = 0.5 * 0.6 = 0.30 (visible)
                if (v < 0 && d > 0)
                    browDown += (-v) * Mathf.Lerp(0.5f, 1.0f, Mathf.InverseLerp(0f, 1f, d));

                if (Mathf.Abs(v) < 0.2f && a > 0.2f && d > 0) browDown += 0.2f;
                if (d < -0.2f && a > 0.1f) browDown += 0.15f;

                Set(pose, "browDownLeft", Mathf.Clamp01(browDown * str));
                Set(pose, "browDownRight", Mathf.Clamp01(browDown * str));
            }

            // ═══════ EYES ═══════

            // Eye widen — unchanged
            if (a > 0.3f && d < 0.3f)
            {
                float widen = (a - 0.3f) / 0.7f;
                if (v < 0) widen *= 1.2f;
                Set(pose, "eyeWideLeft", Mathf.Clamp01(widen * str));
                Set(pose, "eyeWideRight", Mathf.Clamp01(widen * str));
            }

            // Eye squint — FIXED: added sad squint
            {
                float squint = 0;
                if (v > 0.3f) squint += (v - 0.3f) / 0.7f * 0.5f;
                if (v < -0.2f && d > 0.2f) squint += (-v) * d * 0.4f;        // angry
                if (v < -0.2f && d < 0f) squint += (-v) * (-d) * 0.25f;    // ADDED: sad
                if (v > 0.2f && a > 0.2f) squint += 0.1f;
                Set(pose, "eyeSquintLeft", Mathf.Clamp01(squint * str));
                Set(pose, "eyeSquintRight", Mathf.Clamp01(squint * str));
            }

            // Eye direction — unchanged
            {
                if (Mathf.Abs(v) < 0.3f && a < 0 && d < 0.3f)
                {
                    float up = (-a) * 0.4f;
                    Set(pose, "eyeLookUpLeft", Mathf.Clamp01(up));
                    Set(pose, "eyeLookUpRight", Mathf.Clamp01(up * 0.8f));
                }

                if (v < -0.2f && d < -0.2f)
                {
                    float down = (-v) * (-d) * 0.4f;
                    Set(pose, "eyeLookDownLeft", Mathf.Clamp01(down));
                    Set(pose, "eyeLookDownRight", Mathf.Clamp01(down));
                }
            }

            // ═══════ NOSE — unchanged ═══════

            if (v < -0.2f && d > 0)
            {
                float sneer = (-v - 0.2f) * (d + 0.5f) * 0.4f * str;
                Set(pose, "noseSneerLeft", Mathf.Clamp01(sneer));
                Set(pose, "noseSneerRight", Mathf.Clamp01(sneer * 0.85f));
            }

            // ═══════ CHEEK PUFF — unchanged ═══════

            if (v < -0.2f && a < 0.2f && d > -0.3f)
            {
                float puff = (-v - 0.2f) * 0.3f;
                Set(pose, "cheekPuff", Mathf.Clamp01(puff));
            }

            // ═══════ FINAL TOUCHES ═══════
            ApplyAsymmetry(pose);

            return pose;
        }

        /// <summary>
        /// Determines what rare body animation to trigger, if any.
        /// Returns null if no body animation needed.
        /// </summary>
        public string GetBodyAnimation(EmotionVAD vad)
        {

         


            if (!vad.IsIntense) return null;

            // Big laugh
            if (vad.Valence > 0.7f && vad.Arousal > 0.6f)
                return "laugh_full";

            /* // Greeting
             if (vad.Valence > 0.5f && vad.Arousal > 0.2f && vad.Dominance < 0.2f && vad.Magnitude > 0.5f)
                 return "greeting_wave";*/

            // Angry arms cross
            if (vad.Valence < -0.6f && vad.Dominance > 0.4f)
                return "angry_arms_cross";

            // Celebration
            if (vad.Valence > 0.8f && vad.Arousal > 0.7f && vad.Dominance > 0.3f)
                return "celebration";

            return null;
        }

        // ═══════ Helpers ═══════

        private void Set(FacialPose pose, string name, float value)
        {
            if (value > 0.005f)
            {
                // Add micro-variation
                value += Random.Range(-microVariation, microVariation);
                pose.weights[name] = Mathf.Clamp01(value);
            }
        }

        private void ApplyAsymmetry(FacialPose pose)
        {
            var pairs = new[]
            {
                ("mouthSmileLeft", "mouthSmileRight"),
                ("eyeSquintLeft", "eyeSquintRight"),
                ("browDownLeft", "browDownRight"),
                ("browOuterUpLeft", "browOuterUpRight"),
                ("cheekSquintLeft", "cheekSquintRight"),
                ("noseSneerLeft", "noseSneerRight"),
                ("mouthFrownLeft", "mouthFrownRight"),
                ("mouthPressLeft", "mouthPressRight"),
                ("eyeWideLeft", "eyeWideRight"),
                ("mouthDimpleLeft", "mouthDimpleRight"),
                ("mouthStretchLeft", "mouthStretchRight"),
            };

            foreach (var (left, right) in pairs)
            {
                bool hasL = pose.weights.ContainsKey(left);
                bool hasR = pose.weights.ContainsKey(right);

                if (hasL || hasR)
                {
                    float var = Random.Range(-asymmetryAmount, asymmetryAmount);
                    if (hasL) pose.weights[left] = Mathf.Clamp01(pose.weights[left] + var);
                    if (hasR) pose.weights[right] = Mathf.Clamp01(pose.weights[right] - var);
                }
            }
        }

        private static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
        {
            float t = Mathf.InverseLerp(fromMin, fromMax, value);
            return Mathf.Lerp(toMin, toMax, t);
        }
    }
}