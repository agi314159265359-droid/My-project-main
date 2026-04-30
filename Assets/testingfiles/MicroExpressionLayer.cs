using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mikk.Avatar.Expression
{
    /// <summary>
    /// Handles sequenced micro-expressions that play during an emotion.
    /// For example, when someone says "haha that was so funny":
    /// 1. Quick eyebrow raise (surprise reaction)
    /// 2. Build into smile
    /// 3. Eye squint intensifies
    /// 4. Slight nose crinkle
    /// These happen in sequence, overlaying the base emotion.
    /// </summary>
    public class MicroExpressionLayer
    {
        private Dictionary<string, int> blendshapeMap;
        private int totalBlendshapes;

        // Sequence playback
        private List<MicroExpressionEvent> currentSequence;
        private int currentEventIndex;
        private float sequenceTime;
        private bool isPlaying;

        // Per-blendshape current values with smoothing
        private float[] currentWeights;
        private float[] targetWeights;
        private float smoothSpeed = 10f;

        public MicroExpressionLayer(Dictionary<string, int> blendshapeMap, int totalBlendshapes)
        {
            this.blendshapeMap = blendshapeMap;
            this.totalBlendshapes = totalBlendshapes;
            currentWeights = new float[totalBlendshapes];
            targetWeights = new float[totalBlendshapes];
        }

        public void PlaySequence(List<MicroExpressionEvent> sequence)
        {
            currentSequence = sequence;
            currentEventIndex = 0;
            sequenceTime = 0f;
            isPlaying = true;
            Array.Clear(targetWeights, 0, targetWeights.Length);
        }

        public void Stop()
        {
            isPlaying = false;
            Array.Clear(targetWeights, 0, targetWeights.Length);
        }

        public void Update(float dt, float[] outputWeights)
        {
            Array.Clear(outputWeights, 0, outputWeights.Length);

            if (!isPlaying || currentSequence == null || currentSequence.Count == 0)
            {
                // Fade out any remaining weights
                for (int i = 0; i < totalBlendshapes; i++)
                {
                    currentWeights[i] = Mathf.Lerp(currentWeights[i], 0f, dt * smoothSpeed);
                    outputWeights[i] = currentWeights[i];
                }
                return;
            }

            sequenceTime += dt;

            // Reset target weights for this frame
            Array.Clear(targetWeights, 0, targetWeights.Length);

            // Process all active events in the sequence
            for (int i = 0; i < currentSequence.Count; i++)
            {
                var evt = currentSequence[i];

                // Skip events that haven't started yet
                if (sequenceTime < evt.startTime) continue;

                // Skip events that have already finished
                if (sequenceTime > evt.startTime + evt.duration) continue;

                // Event is active — calculate its intensity using the curve
                float eventProgress = (sequenceTime - evt.startTime) / evt.duration;
                float intensity = CalculateEventCurve(eventProgress, evt.curveType);

                // Apply this event's blendshape changes using parallel arrays
                if (evt.blendshapeNames != null && evt.blendshapeValues != null)
                {
                    int count = Mathf.Min(evt.blendshapeNames.Length, evt.blendshapeValues.Length);
                    for (int j = 0; j < count; j++)
                    {
                        string blendName = evt.blendshapeNames[j];
                        float blendValue = evt.blendshapeValues[j];

                        if (blendshapeMap.TryGetValue(blendName.ToLower(), out int index))
                        {
                            // Additive: accumulate from multiple overlapping events
                            targetWeights[index] += blendValue * intensity * evt.intensity;
                        }
                    }
                }
            }

            // Check if the entire sequence is complete
            var lastEvent = currentSequence[currentSequence.Count - 1];
            if (sequenceTime > lastEvent.startTime + lastEvent.duration)
            {
                isPlaying = false;
            }

            // Smooth interpolation toward target weights
            for (int i = 0; i < totalBlendshapes; i++)
            {
                float clampedTarget = Mathf.Clamp01(targetWeights[i]);
                currentWeights[i] = Mathf.Lerp(currentWeights[i], clampedTarget, dt * smoothSpeed);

                // Dead zone to prevent floating point jitter
                if (Mathf.Abs(currentWeights[i]) < 0.001f)
                    currentWeights[i] = 0f;

                outputWeights[i] = Mathf.Clamp01(currentWeights[i]);
            }
        }

        private float CalculateEventCurve(float t, MicroExpressionCurve curveType)
        {
            // Clamp to prevent issues at boundaries
            t = Mathf.Clamp01(t);

            switch (curveType)
            {
                case MicroExpressionCurve.Bell:
                    // Smooth bell: rises and falls symmetrically
                    return Mathf.Sin(t * Mathf.PI);

                case MicroExpressionCurve.RiseAndHold:
                    // Quick rise, sustained hold, quick fall
                    if (t < 0.2f) return t / 0.2f;
                    if (t > 0.8f) return (1f - t) / 0.2f;
                    return 1f;

                case MicroExpressionCurve.QuickFlash:
                    // Very fast rise, slow exponential decay
                    if (t < 0.1f) return t / 0.1f;
                    return Mathf.Pow(1f - ((t - 0.1f) / 0.9f), 2f);

                case MicroExpressionCurve.SlowBuild:
                    // Gradual build with smooth curve, then quick drop
                    if (t < 0.7f)
                    {
                        float buildT = t / 0.7f;
                        return buildT * buildT; // Quadratic ease-in
                    }
                    float dropT = (t - 0.7f) / 0.3f;
                    return 1f - (dropT * dropT); // Quadratic ease-out

                case MicroExpressionCurve.Pulse:
                    // Two quick pulses (like a double-take)
                    return Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f));

                default:
                    return Mathf.Sin(t * Mathf.PI);
            }
        }

        /// <summary>
        /// Returns whether a micro-expression sequence is currently playing
        /// </summary>
        public bool IsPlaying => isPlaying;

        /// <summary>
        /// Returns current playback time in the sequence
        /// </summary>
        public float CurrentTime => sequenceTime;

        /// <summary>
        /// Returns total duration of the current sequence
        /// </summary>
        public float TotalDuration
        {
            get
            {
                if (currentSequence == null || currentSequence.Count == 0) return 0f;
                var lastEvent = currentSequence[currentSequence.Count - 1];
                return lastEvent.startTime + lastEvent.duration;
            }
        }
    }

    [System.Serializable]
    public class MicroExpressionEvent
    {
        public float startTime;      // When in the sequence this event starts
        public float duration;        // How long it lasts
        public float intensity;       // Overall intensity multiplier (0-1)
        public MicroExpressionCurve curveType;  // Shape of the intensity curve

        // FishNet-compatible parallel arrays instead of Dictionary
        public string[] blendshapeNames;
        public float[] blendshapeValues;

        /// <summary>
        /// Default parameterless constructor — required by FishNet for network serialization
        /// </summary>
        public MicroExpressionEvent()
        {
            startTime = 0f;
            duration = 0.5f;
            intensity = 0f;
            curveType = MicroExpressionCurve.Bell;
            blendshapeNames = Array.Empty<string>();
            blendshapeValues = Array.Empty<float>();
        }

        /// <summary>
        /// Full constructor with Dictionary input — converts to parallel arrays automatically
        /// </summary>
        public MicroExpressionEvent(float start, float dur, float intense,
            MicroExpressionCurve curve, Dictionary<string, float> changes)
        {
            startTime = start;
            duration = dur;
            intensity = intense;
            curveType = curve;

            if (changes != null && changes.Count > 0)
            {
                blendshapeNames = changes.Keys.ToArray();
                blendshapeValues = changes.Values.ToArray();
            }
            else
            {
                blendshapeNames = Array.Empty<string>();
                blendshapeValues = Array.Empty<float>();
            }
        }

        /// <summary>
        /// Constructor with pre-built arrays — for direct use without Dictionary overhead
        /// </summary>
        public MicroExpressionEvent(float start, float dur, float intense,
            MicroExpressionCurve curve, string[] names, float[] values)
        {
            startTime = start;
            duration = dur;
            intensity = intense;
            curveType = curve;
            blendshapeNames = names ?? Array.Empty<string>();
            blendshapeValues = values ?? Array.Empty<float>();
        }

        /// <summary>
        /// Helper method to get blendshape data as a Dictionary when needed
        /// (for code that still expects Dictionary format)
        /// </summary>
        public Dictionary<string, float> GetBlendshapeChanges()
        {
            var dict = new Dictionary<string, float>();
            if (blendshapeNames == null || blendshapeValues == null) return dict;

            int count = Mathf.Min(blendshapeNames.Length, blendshapeValues.Length);
            for (int i = 0; i < count; i++)
            {
                if (!string.IsNullOrEmpty(blendshapeNames[i]))
                {
                    dict[blendshapeNames[i]] = blendshapeValues[i];
                }
            }
            return dict;
        }

        /// <summary>
        /// Validates that this event has usable data
        /// </summary>
        public bool IsValid()
        {
            return duration > 0f &&
                   blendshapeNames != null &&
                   blendshapeValues != null &&
                   blendshapeNames.Length > 0 &&
                   blendshapeNames.Length == blendshapeValues.Length;
        }

        public override string ToString()
        {
            int blendCount = blendshapeNames?.Length ?? 0;
            return $"MicroExpr[t={startTime:F2}, dur={duration:F2}, " +
                   $"int={intensity:F2}, curve={curveType}, blends={blendCount}]";
        }
    }

    public enum MicroExpressionCurve
    {
        Bell,           // Smooth rise and fall (most natural for fleeting expressions)
        RiseAndHold,    // Rise, sustain, fall (for sustained reactions like thinking)
        QuickFlash,     // Fast on, slow off (for surprise, shock)
        SlowBuild,      // Slow on, fast off (for building realization)
        Pulse           // Double pulse (for double-take reactions)
    }
}