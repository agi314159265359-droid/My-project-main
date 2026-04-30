using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using OpenAI;

namespace Mikk.Avatar.Expression
{
    /// <summary>
    /// Analyzes chat messages using AI and produces rich expression data
    /// including primary emotion, micro-expression sequences, and body animation hints.
    /// </summary>
    public class ExpressionAIAnalyzer
    {
        private OpenAIApi openai = new OpenAIApi("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");

        // Cache for repeated messages
        private Dictionary<string, FullExpressionData> responseCache = new Dictionary<string, FullExpressionData>();
        private const int MAX_CACHE_SIZE = 200;

        // Instant cache for common single-word inputs (no API call needed)
        private Dictionary<string, FullExpressionData> instantCache;

        // FIX: Thread-safe random for background threads
        private static readonly System.Random threadSafeRandom = new System.Random();
        private static readonly object randomLock = new object();

        public ExpressionAIAnalyzer()
        {
            BuildInstantCache();
        }

        /// <summary>
        /// Thread-safe random float between min and max.
        /// Unity's Random.Range() can only be called from main thread,
        /// so we use System.Random with locking for background thread safety.
        /// </summary>
        private static float ThreadSafeRandomRange(float min, float max)
        {
            lock (randomLock)
            {
                return (float)(threadSafeRandom.NextDouble() * (max - min) + min);
            }
        }

        /// <summary>
        /// Thread-safe random float between 0 and 1
        /// </summary>
        private static float ThreadSafeRandomValue()
        {
            lock (randomLock)
            {
                return (float)threadSafeRandom.NextDouble();
            }
        }

        public FullExpressionData TryGetCached(string message)
        {
            string key = message.ToLower().Trim();

            // Check instant cache first
            if (instantCache.TryGetValue(key, out var instant))
                return instant.Clone();

            // Check AI response cache
            if (responseCache.TryGetValue(key, out var cached))
                return cached.Clone();

            return null;
        }

        public async UniTask<FullExpressionData> AnalyzeMessage(string message, CancellationToken ct)
        {
            string key = message.ToLower().Trim();

            string systemPrompt = BuildSystemPrompt();

            var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = message }
            };

            try
            {
                var request = new CreateChatCompletionRequest
                {
                    Model = "gpt-4o-mini",
                    Messages = messages,
                    Temperature = 0.3f
                };

                var response = await openai.CreateChatCompletion(request);
                ct.ThrowIfCancellationRequested();

                if (response.Choices == null || response.Choices.Count == 0)
                {
                    Debug.LogWarning("[ExpressionAI] No choices received");
                    return FullExpressionData.CreateDefault();
                }

                string replyContent = response.Choices[0].Message.Content;
                Debug.Log($"[ExpressionAI] Raw response:\n{replyContent}");

                // FIX: Only do JSON parsing on background thread, NOT the conversion
                // that uses Random. Split into two steps.
                AIExpressionResponse aiResponse = null;

                // Step 1: Parse JSON on background thread (safe - no Unity API calls)
                aiResponse = await UniTask.RunOnThreadPool(() =>
                {
                    try
                    {
                        string cleanJson = CleanGPTJson(replyContent);
                        return JsonConvert.DeserializeObject<AIExpressionResponse>(cleanJson);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ExpressionAI] JSON parse failed: {ex.Message}");
                        return null;
                    }
                }, cancellationToken: ct);

                // Step 2: Convert to expression data on MAIN THREAD 
                // (safe to use Unity Random.Range here)
                if (aiResponse != null)
                {
                    // FIX: This now runs on main thread — Unity API calls are safe
                    var parsed = ConvertToExpressionData(aiResponse);
                    if (parsed != null)
                    {
                        CacheResult(key, parsed);
                        return parsed;
                    }
                }

                return FullExpressionData.CreateDefault();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ExpressionAI] API error: {ex.Message}");
                return FullExpressionData.CreateDefault();
            }
        }

        private string BuildSystemPrompt()
        {
            return @"
You are a facial expression analysis engine for a 3D avatar chat application. 
Analyze the user's chat message and output a JSON response that drives realistic facial expressions.

The avatar uses ARKit-compatible blendshapes. Your response drives BOTH body animation AND facial micro-expressions.

OUTPUT FORMAT (strict JSON):
{
  ""primary_emotion"": ""happy"",
  ""emotion_intensity"": 0.7,
  ""is_long_message"": false,
  ""animation_variant"": ""0"",
  ""micro_expression_sequence"": [
    {
      ""timing"": ""early"",
      ""type"": ""brow_raise"",
      ""intensity"": 0.5,
      ""description"": ""Eyebrows raise in recognition""
    },
    {
      ""timing"": ""peak"",
      ""type"": ""smile_build"",
      ""intensity"": 0.8,
      ""description"": ""Full smile builds with eye squint""
    }
  ],
  ""face_dynamics"": {
    ""brow_movement"": ""raise"",
    ""eye_behavior"": ""squint"",
    ""mouth_shape"": ""smile"",
    ""asymmetry"": 0.1,
    ""tension_level"": 0.3
  }
}

EMOTION TYPES (choose one):
neutral, happy, sad, angry, surprised, fearful, astounded, thinking, confused, 
contempt, disgusted, exhausted, greeting, confident, cool, agree, disagree, 
sleepy, teasing, apologetic, flirty

MICRO-EXPRESSION TYPES:
- brow_raise: Quick eyebrow lift (surprise, recognition, emphasis)
- brow_furrow: Eyebrows come together (concentration, concern, anger)
- eye_widen: Eyes open wider (surprise, fear, excitement)
- eye_squint: Eyes narrow (happiness, suspicion, thinking)
- nose_scrunch: Nose wrinkles (disgust, amusement, distaste)
- lip_press: Lips press together (determination, holding back, thinking)
- lip_corner_pull: Corner of mouth pulls (contempt, smirk, suppressed smile)
- jaw_drop: Jaw opens (shock, amazement)
- cheek_raise: Cheeks lift (genuine smile, amusement)
- nostril_flare: Nostrils widen (anger, exertion, intensity)

TIMING: ""early"" (0-30% of expression), ""peak"" (30-70%), ""late"" (70-100%), ""throughout""

FACE DYNAMICS:
- brow_movement: ""raise"", ""furrow"", ""asymmetric_raise"", ""neutral""
- eye_behavior: ""wide"", ""squint"", ""half_closed"", ""darting"", ""steady"", ""neutral""
- mouth_shape: ""smile"", ""frown"", ""open"", ""pressed"", ""pursed"", ""smirk"", ""neutral""
- asymmetry: 0.0 (perfectly symmetric) to 1.0 (very asymmetric) — higher for sarcasm, smirks, confusion
- tension_level: 0.0 (relaxed) to 1.0 (very tense) — affects how tight/held the expression feels

RULES:
1. Analyze the SENDER'S emotion, not the topic discussed
2. ""kya kar raha hai?"" → neutral/curious (they're asking, not expressing deep emotion)
3. ""HAHAHA OMG 😂😂"" → happy, intensity 0.9, multiple micro-expressions
4. Short casual messages (""ok"", ""hi"", ""hmm"") → minimal micro-expressions, low intensity
5. Sarcasm/irony → add asymmetry, contempt micro-expressions
6. ALL CAPS / repeated letters → increase intensity
7. Emojis modify the emotion intensity and type
8. Generate 1-4 micro-expressions depending on message richness
9. Simple messages like ""ok"" need only 0-1 micro-expressions
10. animation_variant should be ""0"" or ""1"" (randomly), or ""none"" for neutral

Handle Hindi, English, and Hinglish naturally.

Process the following message:
";
        }

        private FullExpressionData ConvertToExpressionData(AIExpressionResponse aiResponse)
        {
            if (aiResponse == null) return FullExpressionData.CreateDefault();

            var data = new FullExpressionData
            {
                primaryEmotion = aiResponse.primary_emotion ?? "neutral",
                emotionIntensity = Mathf.Clamp01(aiResponse.emotion_intensity),
                isLongMessage = aiResponse.is_long_message,
                animationVariant = aiResponse.animation_variant ?? "0",
                microExpressions = new List<MicroExpressionEvent>()
            };

            // Convert AI micro-expression descriptions to actual blendshape events
            if (aiResponse.micro_expression_sequence != null)
            {
                float messageDisplayDuration = data.isLongMessage ? 4f : 2.5f;

                foreach (var micro in aiResponse.micro_expression_sequence)
                {
                    if (micro == null) continue;

                    var evt = ConvertMicroExpression(micro, messageDisplayDuration, data.emotionIntensity);
                    if (evt != null)
                        data.microExpressions.Add(evt);
                }
            }

            // Apply face dynamics
            if (aiResponse.face_dynamics != null)
            {
                ApplyFaceDynamics(data, aiResponse.face_dynamics);
            }

            return data;
        }

        private MicroExpressionEvent ConvertMicroExpression(AIMicroExpression micro, float totalDuration, float emotionIntensity)
        {
            if (micro == null) return null;

            // Calculate timing based on position
            // FIX: Using UnityEngine.Random.Range which is safe on main thread
            float startTime;
            float duration;

            switch (micro.timing ?? "peak")
            {
                case "early":
                    startTime = UnityEngine.Random.Range(0f, totalDuration * 0.2f);
                    duration = UnityEngine.Random.Range(0.3f, 0.6f);
                    break;
                case "peak":
                    startTime = totalDuration * 0.3f + UnityEngine.Random.Range(0f, totalDuration * 0.2f);
                    duration = UnityEngine.Random.Range(0.4f, 0.8f);
                    break;
                case "late":
                    startTime = totalDuration * 0.6f + UnityEngine.Random.Range(0f, totalDuration * 0.2f);
                    duration = UnityEngine.Random.Range(0.3f, 0.5f);
                    break;
                case "throughout":
                    startTime = 0.1f;
                    duration = totalDuration * 0.8f;
                    break;
                default:
                    startTime = totalDuration * 0.3f;
                    duration = 0.5f;
                    break;
            }

            float intensity = Mathf.Clamp01((micro.intensity > 0 ? micro.intensity : 0.5f) * emotionIntensity);

            // Map micro-expression type to blendshape changes
            var blendshapes = GetMicroExpressionBlendshapes(micro.type);
            if (blendshapes == null || blendshapes.Count == 0) return null;

            MicroExpressionCurve curve = (micro.timing ?? "peak") == "throughout"
                ? MicroExpressionCurve.RiseAndHold
                : MicroExpressionCurve.Bell;

            if (micro.type == "jaw_drop" || micro.type == "eye_widen")
                curve = MicroExpressionCurve.QuickFlash;

            return new MicroExpressionEvent(startTime, duration, intensity, curve, blendshapes);
        }

        private Dictionary<string, float> GetMicroExpressionBlendshapes(string type)
        {
            switch (type?.ToLower())
            {
                case "brow_raise":
                    return new Dictionary<string, float>
                    {
                        ["browouterupleft"] = 0.6f,
                        ["browouterupright"] = 0.6f,
                        ["browinnerup"] = 0.4f,
                    };

                case "brow_furrow":
                    return new Dictionary<string, float>
                    {
                        ["browdownleft"] = 0.5f,
                        ["browdownright"] = 0.5f,
                        ["browinnerup"] = 0.3f,
                    };

                case "eye_widen":
                    return new Dictionary<string, float>
                    {
                        ["eyewideleft"] = 0.6f,
                        ["eyewideright"] = 0.6f,
                        ["browouterupleft"] = 0.3f,
                        ["browouterupright"] = 0.3f,
                    };

                case "eye_squint":
                    return new Dictionary<string, float>
                    {
                        ["eyesquintleft"] = 0.5f,
                        ["eyesquintright"] = 0.5f,
                        ["cheeksquintleft"] = 0.3f,
                        ["cheeksquintright"] = 0.3f,
                    };

                case "nose_scrunch":
                    return new Dictionary<string, float>
                    {
                        ["nosesneerleft"] = 0.6f,
                        ["nosesneerright"] = 0.6f,
                        ["browdownleft"] = 0.15f,
                        ["browdownright"] = 0.15f,
                    };

                case "lip_press":
                    return new Dictionary<string, float>
                    {
                        ["mouthpressleft"] = 0.5f,
                        ["mouthpressright"] = 0.5f,
                        ["mouthclose"] = 0.3f,
                    };

                case "lip_corner_pull":
                    return new Dictionary<string, float>
                    {
                        ["mouthsmileright"] = 0.4f,
                        ["mouthsmileleft"] = 0.1f,
                        ["nosesneerright"] = 0.15f,
                    };

                case "jaw_drop":
                    return new Dictionary<string, float>
                    {
                        ["jawopen"] = 0.5f,
                        ["mouthfunnel"] = 0.2f,
                    };

                case "cheek_raise":
                    return new Dictionary<string, float>
                    {
                        ["cheeksquintleft"] = 0.5f,
                        ["cheeksquintright"] = 0.5f,
                        ["eyesquintleft"] = 0.2f,
                        ["eyesquintright"] = 0.2f,
                    };

                case "nostril_flare":
                    return new Dictionary<string, float>
                    {
                        ["nosesneerleft"] = 0.4f,
                        ["nosesneerright"] = 0.4f,
                    };

                case "smile_build":
                    return new Dictionary<string, float>
                    {
                        ["mouthsmileleft"] = 0.6f,
                        ["mouthsmileright"] = 0.6f,
                        ["cheeksquintleft"] = 0.4f,
                        ["cheeksquintright"] = 0.4f,
                        ["eyesquintleft"] = 0.3f,
                        ["eyesquintright"] = 0.3f,
                    };

                default:
                    Debug.LogWarning($"[ExpressionAI] Unknown micro-expression type: {type}");
                    return null;
            }
        }

        private void ApplyFaceDynamics(FullExpressionData data, AIFaceDynamics dynamics)
        {
            if (dynamics == null) return;
            data.facialAsymmetry = dynamics.asymmetry;
            data.facialTension = dynamics.tension_level;
        }

        private void CacheResult(string key, FullExpressionData data)
        {
            if (responseCache.Count >= MAX_CACHE_SIZE)
            {
                var firstKey = responseCache.Keys.First();
                responseCache.Remove(firstKey);
            }
            responseCache[key] = data;
        }

        private string CleanGPTJson(string raw)
        {
            raw = raw.Replace("```json", "").Replace("```", "").Trim();
            int startIndex = raw.IndexOf('{');
            int endIndex = raw.LastIndexOf('}');
            if (startIndex >= 0 && endIndex > startIndex)
            {
                raw = raw.Substring(startIndex, endIndex - startIndex + 1);
            }
            return raw;
        }

        #region Instant Cache (No API calls needed)

        private void BuildInstantCache()
        {
            instantCache = new Dictionary<string, FullExpressionData>();

            // FIX: BuildInstantCache is called from constructor, 
            // which might run on a loading thread. Use System.Random instead of Unity Random.
            var rng = new System.Random();

            // Greetings
            AddInstant(rng, "hi", "greeting", 0.6f, false,
                ("brow_raise", "early", 0.4f),
                ("smile_build", "peak", 0.5f));
            AddInstant(rng, "hello", "greeting", 0.7f, false,
                ("brow_raise", "early", 0.5f),
                ("smile_build", "peak", 0.6f));
            AddInstant(rng, "hey", "greeting", 0.5f, false,
                ("brow_raise", "early", 0.3f));
            AddInstant(rng, "namaste", "greeting", 0.8f, false,
                ("brow_raise", "early", 0.5f),
                ("smile_build", "peak", 0.6f));
            AddInstant(rng, "hii", "greeting", 0.6f, false,
                ("brow_raise", "early", 0.4f),
                ("smile_build", "peak", 0.5f));
            AddInstant(rng, "helo", "greeting", 0.5f, false,
                ("brow_raise", "early", 0.3f));

            // Positive responses
            AddInstant(rng, "ok", "neutral", 0.1f, false);
            AddInstant(rng, "k", "neutral", 0.05f, false);
            AddInstant(rng, "kk", "agree", 0.15f, false);
            AddInstant(rng, "okay", "agree", 0.3f, false,
                ("lip_press", "peak", 0.2f));
            AddInstant(rng, "yes", "agree", 0.5f, false,
                ("brow_raise", "early", 0.3f));
            AddInstant(rng, "haan", "agree", 0.4f, false,
                ("brow_raise", "early", 0.2f));
            AddInstant(rng, "han", "agree", 0.4f, false,
                ("brow_raise", "early", 0.2f));
            AddInstant(rng, "theek hai", "agree", 0.3f, false,
                ("lip_press", "peak", 0.2f));
            AddInstant(rng, "thik hai", "agree", 0.3f, false,
                ("lip_press", "peak", 0.2f));
            AddInstant(rng, "achha", "thinking", 0.3f, false,
                ("brow_raise", "early", 0.3f));
            AddInstant(rng, "acha", "thinking", 0.3f, false,
                ("brow_raise", "early", 0.3f));

            // Negative responses
            AddInstant(rng, "no", "disagree", 0.4f, false,
                ("brow_furrow", "peak", 0.3f));
            AddInstant(rng, "nahi", "disagree", 0.4f, false,
                ("brow_furrow", "peak", 0.3f));
            AddInstant(rng, "nahin", "disagree", 0.4f, false,
                ("brow_furrow", "peak", 0.3f));
            AddInstant(rng, "nope", "disagree", 0.3f, false);
            AddInstant(rng, "nah", "disagree", 0.2f, false);

            // Laughter
            AddInstant(rng, "lol", "happy", 0.6f, false,
                ("smile_build", "early", 0.6f),
                ("eye_squint", "peak", 0.4f),
                ("cheek_raise", "peak", 0.5f));
            AddInstant(rng, "haha", "happy", 0.6f, false,
                ("smile_build", "early", 0.5f),
                ("eye_squint", "peak", 0.4f));
            AddInstant(rng, "hahaha", "happy", 0.8f, false,
                ("smile_build", "early", 0.7f),
                ("eye_squint", "peak", 0.5f),
                ("cheek_raise", "peak", 0.6f));
            AddInstant(rng, "hehe", "happy", 0.5f, false,
                ("smile_build", "early", 0.4f),
                ("eye_squint", "peak", 0.3f));

            // Surprise
            AddInstant(rng, "wow", "surprised", 0.8f, false,
                ("brow_raise", "early", 0.7f),
                ("eye_widen", "early", 0.6f),
                ("jaw_drop", "peak", 0.4f));
            AddInstant(rng, "omg", "astounded", 0.9f, false,
                ("eye_widen", "early", 0.8f),
                ("brow_raise", "early", 0.8f),
                ("jaw_drop", "peak", 0.6f));
            AddInstant(rng, "sheesh", "astounded", 0.8f, false,
                ("brow_raise", "early", 0.7f),
                ("eye_widen", "peak", 0.5f));
            AddInstant(rng, "wah", "astounded", 0.8f, false,
                ("brow_raise", "early", 0.6f),
                ("eye_widen", "peak", 0.5f));

            // Thinking
            AddInstant(rng, "hmm", "thinking", 0.4f, false,
                ("brow_furrow", "peak", 0.3f),
                ("lip_press", "throughout", 0.2f));
            AddInstant(rng, "hmmm", "thinking", 0.5f, false,
                ("brow_furrow", "peak", 0.35f),
                ("lip_press", "throughout", 0.25f));
            AddInstant(rng, "hmmmm", "thinking", 0.6f, false,
                ("brow_furrow", "peak", 0.4f),
                ("lip_press", "throughout", 0.3f),
                ("eye_squint", "late", 0.2f));

            // Questions
            AddInstant(rng, "kya", "confused", 0.4f, false,
                ("brow_raise", "early", 0.4f));
            AddInstant(rng, "what", "confused", 0.4f, false,
                ("brow_raise", "early", 0.4f));
            AddInstant(rng, "why", "confused", 0.5f, false,
                ("brow_furrow", "peak", 0.4f));
            AddInstant(rng, "how", "thinking", 0.4f, false,
                ("brow_furrow", "peak", 0.3f));
            AddInstant(rng, "kaise", "thinking", 0.4f, false,
                ("brow_furrow", "peak", 0.3f));
            AddInstant(rng, "kyun", "confused", 0.5f, false,
                ("brow_furrow", "peak", 0.4f));
            AddInstant(rng, "kab", "thinking", 0.3f, false);
            AddInstant(rng, "when", "thinking", 0.3f, false);

            // Slang
            AddInstant(rng, "bruh", "confused", 0.4f, false,
                ("brow_raise", "early", 0.3f),
                ("lip_corner_pull", "peak", 0.2f));
            AddInstant(rng, "slay", "confident", 0.8f, false,
                ("smile_build", "peak", 0.6f),
                ("brow_raise", "early", 0.4f));
            AddInstant(rng, "bet", "confident", 0.7f, false,
                ("lip_press", "early", 0.3f),
                ("brow_raise", "peak", 0.3f));
            AddInstant(rng, "fr", "agree", 0.6f, false,
                ("brow_raise", "peak", 0.3f));
            AddInstant(rng, "no cap", "confident", 0.8f, false,
                ("brow_raise", "early", 0.5f));
            AddInstant(rng, "vibes", "happy", 0.6f, false,
                ("smile_build", "peak", 0.4f));
            AddInstant(rng, "mood", "agree", 0.5f, false);

            // Hinglish expressions
            AddInstant(rng, "are yaar", "confused", 0.5f, false,
                ("brow_furrow", "peak", 0.4f));
            AddInstant(rng, "yaar", "neutral", 0.2f, false);
            AddInstant(rng, "bhai", "neutral", 0.3f, false);
            AddInstant(rng, "dude", "neutral", 0.2f, false);
            AddInstant(rng, "kya baat", "astounded", 0.7f, false,
                ("brow_raise", "early", 0.5f),
                ("smile_build", "peak", 0.5f));

            // Exhaustion / frustration
            AddInstant(rng, "uff", "exhausted", 0.6f, false,
                ("brow_furrow", "peak", 0.4f));
            AddInstant(rng, "argh", "angry", 0.7f, false,
                ("brow_furrow", "early", 0.6f),
                ("nostril_flare", "peak", 0.4f));

            // Farewell
            AddInstant(rng, "bye", "neutral", 0.3f, false,
                ("smile_build", "peak", 0.2f));
        }

        // FIX: Accept System.Random instead of using UnityEngine.Random
        private void AddInstant(System.Random rng, string key, string emotion, float intensity, bool isLong,
            params (string type, string timing, float microIntensity)[] micros)
        {
            var data = new FullExpressionData
            {
                primaryEmotion = emotion,
                emotionIntensity = intensity,
                isLongMessage = isLong,
                // FIX: Use System.Random instead of UnityEngine.Random
                animationVariant = rng.NextDouble() > 0.5 ? "0" : "1",
                microExpressions = new List<MicroExpressionEvent>()
            };

            float totalDuration = isLong ? 4f : 2.5f;

            foreach (var micro in micros)
            {
                var blendshapes = GetMicroExpressionBlendshapes(micro.type);
                if (blendshapes == null) continue;

                float startTime;
                float duration;
                switch (micro.timing)
                {
                    case "early": startTime = 0.1f; duration = 0.4f; break;
                    case "peak": startTime = totalDuration * 0.3f; duration = 0.5f; break;
                    case "late": startTime = totalDuration * 0.6f; duration = 0.4f; break;
                    case "throughout": startTime = 0.1f; duration = totalDuration * 0.7f; break;
                    default: startTime = 0.3f; duration = 0.5f; break;
                }

                data.microExpressions.Add(new MicroExpressionEvent(
                    startTime, duration, micro.microIntensity,
                    micro.timing == "throughout" ? MicroExpressionCurve.RiseAndHold : MicroExpressionCurve.Bell,
                    blendshapes
                ));
            }

            instantCache[key.ToLower().Trim()] = data;
        }

        #endregion
    }

    #region AI Response Data Classes

    [System.Serializable]
    public class AIExpressionResponse
    {
        public string primary_emotion;
        public float emotion_intensity;
        public bool is_long_message;
        public string animation_variant;
        public List<AIMicroExpression> micro_expression_sequence;
        public AIFaceDynamics face_dynamics;

        // FIX: Default constructor for safety
        public AIExpressionResponse()
        {
            primary_emotion = "neutral";
            emotion_intensity = 0f;
            is_long_message = false;
            animation_variant = "0";
            micro_expression_sequence = new List<AIMicroExpression>();
            face_dynamics = new AIFaceDynamics();
        }
    }

    [System.Serializable]
    public class AIMicroExpression
    {
        public string timing;
        public string type;
        public float intensity;
        public string description;

        // FIX: Default constructor
        public AIMicroExpression()
        {
            timing = "peak";
            type = "";
            intensity = 0.5f;
            description = "";
        }
    }

    [System.Serializable]
    public class AIFaceDynamics
    {
        public string brow_movement;
        public string eye_behavior;
        public string mouth_shape;
        public float asymmetry;
        public float tension_level;

        // FIX: Default constructor
        public AIFaceDynamics()
        {
            brow_movement = "neutral";
            eye_behavior = "neutral";
            mouth_shape = "neutral";
            asymmetry = 0f;
            tension_level = 0f;
        }
    }

    #endregion

    #region Expression Data

    [System.Serializable]
    public class FullExpressionData
    {
        public string primaryEmotion;
        public float emotionIntensity;
        public bool isLongMessage;
        public string animationVariant;
        public List<MicroExpressionEvent> microExpressions;
        public float facialAsymmetry;
        public float facialTension;

        // FIX: Default constructor for FishNet serialization
        public FullExpressionData()
        {
            primaryEmotion = "neutral";
            emotionIntensity = 0f;
            isLongMessage = false;
            animationVariant = "none";
            microExpressions = new List<MicroExpressionEvent>();
            facialAsymmetry = 0f;
            facialTension = 0f;
        }

        public static FullExpressionData CreateDefault()
        {
            return new FullExpressionData
            {
                primaryEmotion = "neutral",
                emotionIntensity = 0f,
                isLongMessage = false,
                animationVariant = "none",
                microExpressions = new List<MicroExpressionEvent>(),
                facialAsymmetry = 0f,
                facialTension = 0f
            };
        }

        public FullExpressionData Clone()
        {
            var clone = new FullExpressionData
            {
                primaryEmotion = this.primaryEmotion,
                emotionIntensity = this.emotionIntensity,
                isLongMessage = this.isLongMessage,
                animationVariant = this.animationVariant,
                facialAsymmetry = this.facialAsymmetry,
                facialTension = this.facialTension,
                microExpressions = new List<MicroExpressionEvent>()
            };

            // Deep copy micro-expressions
            if (this.microExpressions != null)
            {
                foreach (var micro in this.microExpressions)
                {
                    clone.microExpressions.Add(new MicroExpressionEvent(
                        micro.startTime,
                        micro.duration,
                        micro.intensity,
                        micro.curveType,
                        micro.blendshapeNames?.ToArray() ?? Array.Empty<string>(),
                        micro.blendshapeValues?.ToArray() ?? Array.Empty<float>()
                    ));
                }
            }

            return clone;
        }
    }

    #endregion
}