using UnityEngine;
using System.Collections.Generic;

namespace Mikk.Avatar.Expression
{
    /// <summary>
    /// Drives blendshapes based on the current emotional state.
    /// Each emotion maps to a specific combination of blendshape targets.
    /// Uses smooth interpolation for natural transitions.
    /// </summary>
    public class EmotionBlendshapeLayer
    {
        private Dictionary<string, int> blendshapeMap;
        private int totalBlendshapes;

        // Emotion state tracking
        private float[] currentWeights;
        private float[] targetWeights;
        private string currentEmotion = "neutral";
        private float currentIntensity = 0f;
        private float targetIntensity = 0f;
        private float neutralFadeProgress = 0f;
        private bool isFadingToNeutral = false;

        // Emotion-to-blendshape recipes
        // Each emotion defines which blendshapes to activate and their relative weights
        private Dictionary<string, Dictionary<string, float>> emotionRecipes;

        public EmotionBlendshapeLayer(Dictionary<string, int> blendshapeMap, int totalBlendshapes)
        {
            this.blendshapeMap = blendshapeMap;
            this.totalBlendshapes = totalBlendshapes;
            currentWeights = new float[totalBlendshapes];
            targetWeights = new float[totalBlendshapes];

            BuildEmotionRecipes();
        }

        private void BuildEmotionRecipes()
        {
            // Based on ARKit/Apple blendshape standard (commonly used in Ready Player Me, etc.)
            // Adjust these names to match YOUR avatar's actual blendshape names
            emotionRecipes = new Dictionary<string, Dictionary<string, float>>
            {
                ["happy"] = new Dictionary<string, float>
                {
                    ["mouthsmileright"] = 0.8f,
                    ["mouthsmileleft"] = 0.8f,
                    ["cheeksquintleft"] = 0.4f,
                    ["cheeksquintright"] = 0.4f,
                    ["eyesquintleft"] = 0.3f,  // Duchenne smile - eyes squint
                    ["eyesquintright"] = 0.3f,
                    ["browouterupleft"] = 0.1f, // Slight brow lift
                    ["browouterupright"] = 0.1f,
                    ["noseSneerLeft"] = 0.1f,   // Nose crinkle
                    ["noseSneerRight"] = 0.1f,
                },
                ["sad"] = new Dictionary<string, float>
                {
                    ["browinnerup"] = 0.7f,     // Inner brows up (worry/sadness)
                    ["browdownleft"] = 0.2f,
                    ["browdownright"] = 0.2f,
                    ["mouthfrownleft"] = 0.5f,
                    ["mouthfrownright"] = 0.5f,
                    ["jawopen"] = 0.05f,
                    ["mouthlowerdownleft"] = 0.2f,
                    ["mouthlowerdownright"] = 0.2f,
                    ["eyesquintleft"] = 0.15f,  // Slightly squinted from tension
                    ["eyesquintright"] = 0.15f,
                },
                ["angry"] = new Dictionary<string, float>
                {
                    ["browdownleft"] = 0.8f,     // Furrowed brows
                    ["browdownright"] = 0.8f,
                    ["browinnerup"] = 0.3f,      // Inner brow tension
                    ["nosesneerleft"] = 0.5f,    // Nose flare
                    ["nosesneerright"] = 0.5f,
                    ["jawforward"] = 0.2f,       // Jaw pushed forward
                    ["mouthlowerdownleft"] = 0.1f,
                    ["mouthlowerdownright"] = 0.1f,
                    ["eyeSquintLeft"] = 0.4f,    // Intense stare
                    ["eyeSquintRight"] = 0.4f,
                    ["mouthpressleft"] = 0.3f,   // Pressed lips
                    ["mouthpressright"] = 0.3f,
                },
                ["surprised"] = new Dictionary<string, float>
                {
                    ["browouterupleft"] = 0.8f,  // Raised eyebrows
                    ["browouterupright"] = 0.8f,
                    ["browinnerup"] = 0.8f,
                    ["eyewideleft"] = 0.6f,      // Wide eyes
                    ["eyewideright"] = 0.6f,
                    ["jawopen"] = 0.4f,          // Open mouth
                    ["mouthfunnel"] = 0.2f,      // O-shape mouth
                },
                ["fearful"] = new Dictionary<string, float>
                {
                    ["browinnerup"] = 0.9f,
                    ["browouterupleft"] = 0.5f,
                    ["browouterupright"] = 0.5f,
                    ["eyewideleft"] = 0.7f,
                    ["eyewideright"] = 0.7f,
                    ["jawopen"] = 0.3f,
                    ["mouthstretchleft"] = 0.3f,
                    ["mouthstretchright"] = 0.3f,
                    ["mouthlowerdownleft"] = 0.2f,
                    ["mouthlowerdownright"] = 0.2f,
                },
                ["disgusted"] = new Dictionary<string, float>
                {
                    ["nosesneerleft"] = 0.7f,
                    ["nosesneerright"] = 0.7f,
                    ["mouthupperupleft"] = 0.4f,  // Upper lip raised
                    ["mouthupperupright"] = 0.4f,
                    ["browdownleft"] = 0.3f,
                    ["browdownright"] = 0.3f,
                    ["cheeksquintleft"] = 0.3f,
                    ["cheeksquintright"] = 0.3f,
                    ["mouthfrownleft"] = 0.2f,
                    ["mouthfrownright"] = 0.2f,
                },
                ["thinking"] = new Dictionary<string, float>
                {
                    ["browinnerup"] = 0.3f,
                    ["browdownleft"] = 0.15f,     // Slight asymmetric furrow
                    ["eyesquintleft"] = 0.2f,
                    ["mouthpressleft"] = 0.15f,
                    ["mouthright"] = 0.1f,        // Slight mouth to one side
                    ["eyelookupleft"] = 0.15f,    // Looking up while thinking
                    ["eyelookupright"] = 0.15f,
                },
                ["confused"] = new Dictionary<string, float>
                {
                    ["browinnerup"] = 0.5f,
                    ["browdownleft"] = 0.3f,      // One brow up, one down
                    ["browouterupright"] = 0.4f,
                    ["eyesquintleft"] = 0.25f,
                    ["mouthfrownleft"] = 0.15f,
                    ["mouthfrownright"] = 0.15f,
                    ["jawopen"] = 0.08f,
                },
                ["contempt"] = new Dictionary<string, float>
                {
                    ["mouthsmileright"] = 0.3f,   // One-sided smirk
                    ["browdownleft"] = 0.2f,
                    ["eyesquintright"] = 0.15f,
                    ["nosesneerright"] = 0.2f,
                    ["mouthright"] = 0.15f,
                },
                ["confident"] = new Dictionary<string, float>
                {
                    ["mouthsmileleft"] = 0.3f,
                    ["mouthsmileright"] = 0.3f,
                    ["jawforward"] = 0.1f,
                    ["browouterupleft"] = 0.15f,
                    ["browouterupright"] = 0.15f,
                    ["cheeksquintleft"] = 0.1f,
                    ["cheeksquintright"] = 0.1f,
                },
                ["exhausted"] = new Dictionary<string, float>
                {
                    ["eyeblinkleft"] = 0.3f,      // Half-closed eyes
                    ["eyeblinkright"] = 0.3f,
                    ["browdownleft"] = 0.2f,
                    ["browdownright"] = 0.2f,
                    ["mouthfrownleft"] = 0.2f,
                    ["mouthfrownright"] = 0.2f,
                    ["jawopen"] = 0.15f,
                    ["mouthlowerdownleft"] = 0.1f,
                    ["mouthlowerdownright"] = 0.1f,
                },
                ["sleepy"] = new Dictionary<string, float>
                {
                    ["eyeblinkleft"] = 0.5f,      // Heavy eyelids
                    ["eyeblinkright"] = 0.5f,
                    ["browdownleft"] = 0.15f,
                    ["browdownright"] = 0.15f,
                    ["jawopen"] = 0.2f,            // Yawn-like
                    ["mouthlowerdownleft"] = 0.1f,
                    ["mouthlowerdownright"] = 0.1f,
                },
                ["greeting"] = new Dictionary<string, float>
                {
                    ["mouthsmileleft"] = 0.6f,
                    ["mouthsmileright"] = 0.6f,
                    ["browouterupleft"] = 0.3f,
                    ["browouterupright"] = 0.3f,
                    ["browinnerup"] = 0.2f,
                    ["eyewideleft"] = 0.15f,       // Eyes open wider in greeting
                    ["eyewideright"] = 0.15f,
                    ["cheeksquintleft"] = 0.2f,
                    ["cheeksquintright"] = 0.2f,
                },
                ["agree"] = new Dictionary<string, float>
                {
                    ["mouthsmileleft"] = 0.2f,
                    ["mouthsmileright"] = 0.2f,
                    ["browouterupleft"] = 0.15f,
                    ["browouterupright"] = 0.15f,
                    ["mouthpressleft"] = 0.1f,     // Slight lip press (affirming nod-like)
                    ["mouthpressright"] = 0.1f,
                },
                ["disagree"] = new Dictionary<string, float>
                {
                    ["browdownleft"] = 0.3f,
                    ["browdownright"] = 0.3f,
                    ["mouthfrownleft"] = 0.2f,
                    ["mouthfrownright"] = 0.2f,
                    ["nosesneerleft"] = 0.15f,
                    ["nosesneerright"] = 0.15f,
                    ["mouthpressleft"] = 0.2f,     // Tight lips
                    ["mouthpressright"] = 0.2f,
                },
                ["teasing"] = new Dictionary<string, float>
                {
                    ["mouthsmileright"] = 0.5f,    // Asymmetric grin
                    ["mouthsmileleft"] = 0.2f,
                    ["browouterupright"] = 0.3f,   // One brow raised
                    ["eyesquintleft"] = 0.2f,
                    ["cheeksquintright"] = 0.2f,
                    ["tongueout"] = 0.1f,          // Slight tongue
                },
                ["flirty"] = new Dictionary<string, float>
                {
                    ["mouthsmileleft"] = 0.4f,
                    ["mouthsmileright"] = 0.4f,
                    ["eyeblinkleft"] = 0.15f,      // Slightly lowered lids
                    ["eyeblinkright"] = 0.15f,
                    ["browouterupleft"] = 0.2f,
                    ["cheeksquintleft"] = 0.15f,
                    ["cheeksquintright"] = 0.15f,
                    ["mouthpuckerleft"] = 0.1f,    // Slight pucker
                    ["mouthpuckerright"] = 0.1f,
                },
                ["apologetic"] = new Dictionary<string, float>
                {
                    ["browinnerup"] = 0.5f,
                    ["browouterupleft"] = 0.2f,
                    ["browouterupright"] = 0.2f,
                    ["mouthfrownleft"] = 0.2f,
                    ["mouthfrownright"] = 0.2f,
                    ["mouthpressleft"] = 0.15f,    // Pressed lips (sheepish)
                    ["mouthpressright"] = 0.15f,
                    ["eyesquintleft"] = 0.1f,
                    ["eyesquintright"] = 0.1f,
                },
                ["astounded"] = new Dictionary<string, float>
                {
                    ["browouterupleft"] = 0.9f,
                    ["browouterupright"] = 0.9f,
                    ["browinnerup"] = 0.9f,
                    ["eyewideleft"] = 0.8f,
                    ["eyewideright"] = 0.8f,
                    ["jawopen"] = 0.6f,
                    ["mouthfunnel"] = 0.3f,
                },
                ["neutral"] = new Dictionary<string, float>
                {
                    // Everything at zero — the natural resting face
                },
                ["cool"] = new Dictionary<string, float>
                {
                    ["mouthsmileright"] = 0.2f,
                    ["mouthsmileleft"] = 0.2f,
                    ["eyeblinkleft"] = 0.15f,      // Relaxed eyes
                    ["eyeblinkright"] = 0.15f,
                    ["browouterupleft"] = 0.1f,
                    ["browouterupright"] = 0.1f,
                    ["jawforward"] = 0.05f,
                },
            };
        }

        public void SetTargetEmotion(string emotion, float intensity)
        {
            currentEmotion = emotion;
            targetIntensity = Mathf.Clamp01(intensity);
            isFadingToNeutral = false;

            // Build target weights from recipe
            System.Array.Clear(targetWeights, 0, targetWeights.Length);

            if (emotionRecipes.TryGetValue(emotion.ToLower(), out var recipe))
            {
                foreach (var kvp in recipe)
                {
                    if (blendshapeMap.TryGetValue(kvp.Key.ToLower(), out int index))
                    {
                        targetWeights[index] = kvp.Value * targetIntensity;
                    }
                }
            }
        }

        public void FadeToNeutral(float progress)
        {
            isFadingToNeutral = true;
            neutralFadeProgress = progress;
        }

        public void Update(float dt, float transitionSpeed, float[] outputWeights)
        {
            System.Array.Clear(outputWeights, 0, outputWeights.Length);

            for (int i = 0; i < totalBlendshapes; i++)
            {
                float target = targetWeights[i];

                if (isFadingToNeutral)
                {
                    target = Mathf.Lerp(targetWeights[i], 0f, neutralFadeProgress);
                }

                // Smooth interpolation — NOT instant
                currentWeights[i] = Mathf.Lerp(currentWeights[i], target, dt * transitionSpeed);

                // Dead zone to prevent floating point jitter
                if (Mathf.Abs(currentWeights[i]) < 0.001f)
                    currentWeights[i] = 0f;

                outputWeights[i] = currentWeights[i];
            }
        }

        /// <summary>
        /// Gets the current blendshape recipe for an emotion.
        /// Useful for debugging or UI display.
        /// </summary>
        public Dictionary<string, float> GetEmotionRecipe(string emotion)
        {
            return emotionRecipes.TryGetValue(emotion.ToLower(), out var recipe) ? recipe : null;
        }
    }
}