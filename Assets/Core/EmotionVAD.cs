using System;
using UnityEngine;


namespace Mikk.Avatar
{
    [Serializable]
    public struct EmotionVAD
    {
        [Range(-1f, 1f)] public float Valence;
        [Range(-1f, 1f)] public float Arousal;
        [Range(-1f, 1f)] public float Dominance;

        public float Magnitude => Mathf.Sqrt(
            Valence * Valence + Arousal * Arousal + Dominance * Dominance) / 1.732f;

        public bool IsNeutral => Magnitude < 0.05f;
        public bool IsIntense => Magnitude > 0.65f;

        /// <summary>Shake: active disagreement. Needs energy (A > 0).</summary>
        public bool ShouldShake => Valence < -0.25f && Arousal > 0f;

        /// <summary>Look down: passive sadness, shame, apology.</summary>
        public bool ShouldLookDown => Valence < -0.1f && Arousal < 0f && Dominance < -0.2f;

        /// <summary>Nod: agreement, positive acknowledgment.</summary>
        public bool ShouldNod => Valence > 0.15f;

        /// <summary>Tilt: curiosity, confusion, casual.</summary>
        public bool ShouldTilt => !IsNeutral && Mathf.Abs(Valence) < 0.4f;

        public float GetHoldDuration()
        {
            float base_duration = 4f + Magnitude * 4f;
            if (Arousal < -0.3f) base_duration += 2f;
            if (Arousal > 0.5f) base_duration -= 1f;
            return Mathf.Clamp(base_duration, 3f, 12f);
        }

        public static EmotionVAD Neutral => new EmotionVAD { Valence = 0, Arousal = 0, Dominance = 0 };

        public static EmotionVAD Lerp(EmotionVAD a, EmotionVAD b, float t)
        {
            return new EmotionVAD
            {
                Valence = Mathf.Lerp(a.Valence, b.Valence, t),
                Arousal = Mathf.Lerp(a.Arousal, b.Arousal, t),
                Dominance = Mathf.Lerp(a.Dominance, b.Dominance, t)
            };
        }

        public override string ToString() =>
            $"V:{Valence:+0.00;-0.00} A:{Arousal:+0.00;-0.00} D:{Dominance:+0.00;-0.00} (mag:{Magnitude:F2})";
    }
}