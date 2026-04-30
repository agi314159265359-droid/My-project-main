using UnityEngine;
using System.Collections.Generic;

namespace Mikk.Avatar.Expression
{
    /// <summary>
    /// Handles idle micro-movements that make the avatar look alive:
    /// - Natural eye blinking (with variation)
    /// - Subtle eyebrow movements
    /// - Micro jaw movements (breathing)
    /// - Occasional eye squints/widening
    /// - Head-related blendshapes (subtle nostril flare, lip compression)
    /// </summary>
    public class IdleMicroMovementLayer
    {
        private Dictionary<string, int> blendshapeMap;
        private int totalBlendshapes;

        // Blink system
        private float nextBlinkTime;
        private float blinkProgress;
        private float blinkDuration = 0.15f;
        private float blinkCloseDuration = 0.06f;
        private bool isBlinking;
        private bool isDoubleBlink;
        private int doubleBlinkCount;
        private float blinkBaseInterval = 3.5f; // Average 3.5 seconds between blinks

        // Eyebrow micro-movement
        private float browNoisePhase;
        private float browNoiseSpeed = 0.3f;
        private float browIntensity = 0.08f;

        // Breathing simulation
        private float breathPhase;
        private float breathSpeed = 0.2f; // ~12 breaths per minute
        private float breathIntensity = 0.03f;

        // Eye micro-saccades (tiny eye movements)
        private float eyeNoisePhaseX;
        private float eyeNoisePhaseY;
        private float eyeNoiseSpeed = 0.5f;

        // Occasional micro-expressions
        private float nextMicroExpressionTime;
        private float microExpressionProgress;
        private string currentMicroExpression;
        private float microExpressionDuration;

        // Emotional context modifiers
        private string currentEmotionalContext = "neutral";
        private float emotionalIntensity = 0f;

        // Noise seeds for natural variation
        private float noiseSeed1, noiseSeed2, noiseSeed3, noiseSeed4;

        public IdleMicroMovementLayer(Dictionary<string, int> blendshapeMap, int totalBlendshapes)
        {
            this.blendshapeMap = blendshapeMap;
            this.totalBlendshapes = totalBlendshapes;

            // Randomize seeds so each avatar is unique
            noiseSeed1 = Random.Range(0f, 1000f);
            noiseSeed2 = Random.Range(0f, 1000f);
            noiseSeed3 = Random.Range(0f, 1000f);
            noiseSeed4 = Random.Range(0f, 1000f);

            ScheduleNextBlink();
            ScheduleNextMicroExpression();
        }

        public void SetEmotionalContext(string emotion, float intensity)
        {
            currentEmotionalContext = emotion;
            emotionalIntensity = intensity;

            // Emotional states affect idle behavior
            switch (emotion)
            {
                case "sad":
                    blinkBaseInterval = 2.5f; // Sad people blink more
                    browIntensity = 0.04f;
                    break;
                case "angry":
                    blinkBaseInterval = 5f; // Angry people blink less (more staring)
                    browIntensity = 0.02f;
                    break;
                case "surprised":
                case "fearful":
                    blinkBaseInterval = 6f; // Wide-eyed, less blinking
                    browIntensity = 0.12f;
                    break;
                case "happy":
                    blinkBaseInterval = 3f;
                    browIntensity = 0.1f;
                    break;
                default:
                    blinkBaseInterval = 3.5f;
                    browIntensity = 0.08f;
                    break;
            }
        }

        public void Update(float dt, float[] weights)
        {
            // Reset weights
            System.Array.Clear(weights, 0, weights.Length);

            UpdateBlinking(dt, weights);
            UpdateEyebrowMicroMovement(dt, weights);
            UpdateBreathing(dt, weights);
            UpdateOccasionalMicroExpressions(dt, weights);
            UpdateEyeMicroSaccades(dt, weights);
        }

        #region Blinking

        private void UpdateBlinking(float dt, float[] weights)
        {
            float time = Time.time;

            if (!isBlinking && time >= nextBlinkTime)
            {
                StartBlink();
            }

            if (isBlinking)
            {
                blinkProgress += dt;

                float blinkWeight = CalculateBlinkCurve(blinkProgress, blinkDuration);

                SetWeight(weights, "eyelookdownleft", blinkWeight * 0.3f); // Slight downward look during blink
                SetWeight(weights, "eyelookdownright", blinkWeight * 0.3f);
                SetWeight(weights, "eyeblinkleft", blinkWeight);
                SetWeight(weights, "eyeblinkright", blinkWeight);

                // Add slight cheek raise during blink (natural)
                SetWeight(weights, "cheeksquintleft", blinkWeight * 0.05f);
                SetWeight(weights, "cheeksquintright", blinkWeight * 0.05f);

                if (blinkProgress >= blinkDuration)
                {
                    if (isDoubleBlink && doubleBlinkCount < 1)
                    {
                        // Quick second blink
                        doubleBlinkCount++;
                        blinkProgress = 0f;
                        blinkDuration = 0.12f; // Second blink is faster
                    }
                    else
                    {
                        isBlinking = false;
                        ScheduleNextBlink();
                    }
                }
            }
        }

        private float CalculateBlinkCurve(float progress, float duration)
        {
            // Asymmetric blink: fast close, slower open
            float normalizedProgress = progress / duration;

            if (normalizedProgress < 0.3f)
            {
                // Fast close phase (0 to 1 in first 30% of duration)
                float t = normalizedProgress / 0.3f;
                return t * t * t; // Cubic ease-in for snappy close
            }
            else if (normalizedProgress < 0.5f)
            {
                // Hold closed briefly
                return 1f;
            }
            else
            {
                // Slower open phase (1 to 0 in last 50% of duration)
                float t = (normalizedProgress - 0.5f) / 0.5f;
                return 1f - (t * t); // Quadratic ease-out for smooth open
            }
        }

        private void StartBlink()
        {
            isBlinking = true;
            blinkProgress = 0f;
            blinkDuration = Random.Range(0.12f, 0.18f);

            // 15% chance of double blink (very natural)
            isDoubleBlink = Random.value < 0.15f;
            doubleBlinkCount = 0;
        }

        private void ScheduleNextBlink()
        {
            // Natural blink interval with variation
            // Humans blink every 2-10 seconds, averaging around 3-4
            float variation = Random.Range(-1.5f, 2f);
            float emotionModifier = currentEmotionalContext == "surprised" ? 2f : 0f;
            nextBlinkTime = Time.time + blinkBaseInterval + variation + emotionModifier;
        }

        #endregion

        #region Eyebrow Micro-Movement

        private void UpdateEyebrowMicroMovement(float dt, float[] weights)
        {
            browNoisePhase += dt * browNoiseSpeed;

            // Use Perlin noise for organic movement
            float leftBrowUp = Mathf.PerlinNoise(noiseSeed1 + browNoisePhase, 0f) * 2f - 1f;
            float rightBrowUp = Mathf.PerlinNoise(noiseSeed2 + browNoisePhase, 0f) * 2f - 1f;
            float innerBrow = Mathf.PerlinNoise(noiseSeed3 + browNoisePhase * 0.7f, 0f) * 2f - 1f;

            // Slight asymmetry (one brow moves slightly more than other)
            float asymmetry = Mathf.PerlinNoise(noiseSeed4 + browNoisePhase * 0.1f, 0f) * 0.3f;

            float leftIntensity = browIntensity * (1f + asymmetry);
            float rightIntensity = browIntensity * (1f - asymmetry);

            if (leftBrowUp > 0)
                SetWeight(weights, "browinnerup", leftBrowUp * leftIntensity * 0.5f);

            if (rightBrowUp > 0)
                SetWeight(weights, "browouterupleft", leftBrowUp * leftIntensity);

            if (leftBrowUp < 0)
                SetWeight(weights, "browdownleft", -leftBrowUp * leftIntensity * 0.3f);

            if (rightBrowUp > 0)
                SetWeight(weights, "browouterupright", rightBrowUp * rightIntensity);

            if (rightBrowUp < 0)
                SetWeight(weights, "browdownright", -rightBrowUp * rightIntensity * 0.3f);
        }

        #endregion

        #region Breathing

        private void UpdateBreathing(float dt, float[] weights)
        {
            breathPhase += dt * breathSpeed * Mathf.PI * 2f;

            // Smooth sine wave for breathing
            float breathCycle = (Mathf.Sin(breathPhase) + 1f) * 0.5f;

            // Jaw opens very slightly on inhale
            SetWeight(weights, "jawopen", breathCycle * breathIntensity);

            // Nostrils flare slightly
            SetWeight(weights, "nosesneerleft", breathCycle * breathIntensity * 0.5f);
            SetWeight(weights, "nosesneerright", breathCycle * breathIntensity * 0.5f);
        }

        #endregion

        #region Occasional Micro-Expressions

        private void UpdateOccasionalMicroExpressions(float dt, float[] weights)
        {
            float time = Time.time;

            if (time >= nextMicroExpressionTime && currentMicroExpression == null)
            {
                StartRandomMicroExpression();
            }

            if (currentMicroExpression != null)
            {
                microExpressionProgress += dt;
                float t = microExpressionProgress / microExpressionDuration;

                if (t >= 1f)
                {
                    currentMicroExpression = null;
                    ScheduleNextMicroExpression();
                    return;
                }

                // Bell curve intensity: rises and falls smoothly
                float intensity = Mathf.Sin(t * Mathf.PI) * 0.15f;

                switch (currentMicroExpression)
                {
                    case "lip_compress":
                        SetWeight(weights, "mouthpressleft", intensity);
                        SetWeight(weights, "mouthpressright", intensity);
                        break;
                    case "slight_squint":
                        SetWeight(weights, "eyesquintleft", intensity);
                        SetWeight(weights, "eyesquintright", intensity);
                        break;
                    case "lip_corner":
                        SetWeight(weights, "mouthsmileright", intensity * 0.5f);
                        break;
                    case "nostril":
                        SetWeight(weights, "nosesneerleft", intensity * 0.7f);
                        SetWeight(weights, "nosesneerright", intensity * 0.7f);
                        break;
                    case "brow_furrow":
                        SetWeight(weights, "browdownleft", intensity * 0.5f);
                        SetWeight(weights, "browdownright", intensity * 0.5f);
                        break;
                }
            }
        }

        private void StartRandomMicroExpression()
        {
            var expressions = new[] { "lip_compress", "slight_squint", "lip_corner", "nostril", "brow_furrow" };
            currentMicroExpression = expressions[Random.Range(0, expressions.Length)];
            microExpressionProgress = 0f;
            microExpressionDuration = Random.Range(0.3f, 0.8f);
        }

        private void ScheduleNextMicroExpression()
        {
            nextMicroExpressionTime = Time.time + Random.Range(4f, 10f);
        }

        #endregion

        #region Eye Micro-Saccades

        private void UpdateEyeMicroSaccades(float dt, float[] weights)
        {
            eyeNoisePhaseX += dt * eyeNoiseSpeed;
            eyeNoisePhaseY += dt * eyeNoiseSpeed * 0.7f; // Different speed for Y

            float lookX = (Mathf.PerlinNoise(noiseSeed1 + eyeNoisePhaseX, noiseSeed2) * 2f - 1f) * 0.04f;
            float lookY = (Mathf.PerlinNoise(noiseSeed3 + eyeNoisePhaseY, noiseSeed4) * 2f - 1f) * 0.03f;

            if (lookX > 0)
            {
                SetWeight(weights, "eyelookoutleft", lookX);
                SetWeight(weights, "eyelookinright", lookX);
            }
            else
            {
                SetWeight(weights, "eyelookinleft", -lookX);
                SetWeight(weights, "eyelookoutright", -lookX);
            }

            if (lookY > 0)
            {
                SetWeight(weights, "eyelookupleft", lookY);
                SetWeight(weights, "eyelookupright", lookY);
            }
            else
            {
                SetWeight(weights, "eyelookdownleft", -lookY);
                SetWeight(weights, "eyelookdownright", -lookY);
            }
        }

        #endregion

        #region Utility

        private void SetWeight(float[] weights, string blendshapeName, float value)
        {
            if (blendshapeMap.TryGetValue(blendshapeName.ToLower(), out int index))
            {
                // Additive: accumulate weights
                weights[index] = Mathf.Max(weights[index], Mathf.Clamp01(value));
            }
        }

        #endregion
    }
}