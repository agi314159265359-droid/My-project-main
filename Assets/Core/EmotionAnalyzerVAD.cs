using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using OpenAI;
using UnityEngine;



namespace Mikk.Avatar
{
    public class EmotionAnalyzerVAD : MonoBehaviour
    {
        [SerializeField] private string openAIApiKey;

        private OpenAIApi _openai;
        private Dictionary<string, EmotionResult> _cache = new Dictionary<string, EmotionResult>(200);
        private const int MAX_CACHE = 200;

        // ══════════════════════════════════════════════
        // RESULT STRUCT
        // ══════════════════════════════════════════════

        public struct EmotionResult
        {
            public EmotionVAD VAD;
            public GestureHint Hint;

            public override string ToString() =>
                $"{VAD} | Gesture:{Hint}";
        }

        private void Awake()
        {
            _openai = new OpenAIApi(openAIApiKey);
            InitCache();
        }

        // ══════════════════════════════════════════════
        // PUBLIC API
        // ══════════════════════════════════════════════

        public async UniTask<EmotionResult> AnalyzeAsync(
            string message,
            CancellationToken ct,
            bool hasLaugh = false,
            bool hasSad = false,
            bool hasAngry = false)
        {
            string key = message.ToLower().Trim();

            // Tier 1: Cache
            if (_cache.TryGetValue(key, out var cached))
            {
                Debug.Log($"[Emotion] Cache hit: {key} → {cached}");
                var varied = new EmotionResult
                {
                    VAD = ApplyEmojiBias(Vary(cached.VAD), hasLaugh, hasSad, hasAngry),
                    Hint = cached.Hint
                };
                return varied;
            }

            // Tier 2: Rules
            var rule = TryRules(key);
            if (rule.HasValue)
            {
                Debug.Log($"[Emotion] Rule match: {key} → {rule.Value}");
                Cache(key, rule.Value);
                var ruleResult = new EmotionResult
                {
                    VAD = ApplyEmojiBias(Vary(rule.Value.VAD), hasLaugh, hasSad, hasAngry),
                    Hint = rule.Value.Hint
                };
                return ruleResult;
            }

            // Tier 3: AI
            Debug.Log($"[Emotion] AI analyzing: {key}");
            var ai = await AIAnalyze(message, ct);
            Cache(key, ai);
            ai.VAD = ApplyEmojiBias(ai.VAD, hasLaugh, hasSad, hasAngry);
            return ai;
        }

        public static EmotionResult QuickEstimate(
            string message,
            bool hasLaugh = false,
            bool hasSad = false,
            bool hasAngry = false)
        {
            string lower = message.ToLower();

            float v = 0.05f;
            float a = 0.05f;
            float d = 0.05f;
            GestureHint hint = GestureHint.None;

            // ── Hint detection (lightweight fallback) ──
            if (ContainsAny(lower, "kaise ho", "kya haal", "hello", "namaste", "hey", "hi "))
                hint = GestureHint.Greeting;
            else if (lower.Contains("?") || ContainsAny(lower, "kya", "kaise", "kyun", "kab"))
                hint = GestureHint.Question;
            else if (ContainsAny(lower, "pata nahi", "shayad", "ho sakta", "kya pata"))
                hint = GestureHint.Uncertainty;
            else if (ContainsAny(lower, "nahi", "galat", "mat kar", "no no"))
                hint = GestureHint.Negation;
            else if (ContainsAny(lower, "haan", "bilkul", "sahi", "theek", "zaroor"))
                hint = GestureHint.Affirmation;
            else if (ContainsAny(lower, "bahut", "ekdum", "sacchi", "seriously"))
                hint = GestureHint.Emphasis;
            else if (ContainsAny(lower, "chodo", "jane do", "rehne do", "forget"))
                hint = GestureHint.Dismissal;
            else if (ContainsAny(lower, "dekho", "dekh", "look", "see this"))
                hint = GestureHint.Pointing;
            else if (ContainsAny(lower, "aram se", "tension mat", "relax", "chill"))
                hint = GestureHint.Calming;

            // ── VAD estimation (your existing logic) ──
            if (lower.Contains("?")) { a += 0.15f; d -= 0.1f; }
            if (lower.Contains("!")) { a += 0.25f; }

            if (lower.Contains("haha") || lower.Contains("lol")) { v += 0.45f; a += 0.3f; }
            if (lower.Contains("😂")) { v += 0.55f; a += 0.35f; }
            if (lower.Contains("nice") || lower.Contains("good") || lower.Contains("great")) { v += 0.3f; a += 0.1f; }
            if (lower.Contains("bhai") || lower.Contains("yaar") || lower.Contains("dost")) { v += 0.1f; }

            if (lower.Contains("😭")) { v -= 0.5f; a -= 0.2f; d -= 0.3f; }
            if (lower.Contains("sorry")) { v -= 0.15f; d -= 0.35f; }
            if (lower.Contains("nahi") || lower.Contains("no")) { v -= 0.15f; d += 0.1f; }
            if (lower.Contains("galat") || lower.Contains("wrong")) { v -= 0.3f; a += 0.2f; d += 0.2f; }

            bool allCaps = lower.Length > 2 && lower == lower.ToUpper() && lower.Any(char.IsLetter);
            if (allCaps) { a += 0.4f; }

            int wordCount = message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount >= 5) a += 0.1f;
            if (wordCount >= 10) a += 0.1f;

            if (ContainsAny(lower, "kar", "karo", "do this", "chalo", "chal")) { d += 0.15f; }
            if (ContainsAny(lower, "kya", "kaise", "kyun", "what", "how", "why")) { d -= 0.1f; a += 0.1f; }

            var vad = new EmotionVAD
            {
                Valence = Mathf.Clamp(v, -1f, 1f),
                Arousal = Mathf.Clamp(a, -1f, 1f),
                Dominance = Mathf.Clamp(d, -1f, 1f)
            };

            return new EmotionResult
            {
                VAD = ApplyEmojiBias(vad, hasLaugh, hasSad, hasAngry),
                Hint = hint
            };
        }

        // ══════════════════════════════════════════════
        // EMOJI BIAS — unchanged
        // ══════════════════════════════════════════════

        private static EmotionVAD ApplyEmojiBias(
            EmotionVAD e,
            bool hasLaugh,
            bool hasSad,
            bool hasAngry)
        {
            float v = e.Valence;
            float a = e.Arousal;
            float d = e.Dominance;

            if (hasLaugh)
            {
                v = Mathf.Clamp(v + 0.25f, -1f, 1f);
                a = Mathf.Clamp(a + 0.20f, -1f, 1f);
            }

            if (hasSad)
            {
                v = Mathf.Clamp(v - 0.20f, -1f, 1f);
                a = Mathf.Clamp(a - 0.10f, -1f, 1f);
            }

            if (hasAngry)
            {
                v = Mathf.Clamp(v - 0.20f, -1f, 1f);
                a = Mathf.Clamp(a + 0.20f, -1f, 1f);
                d = Mathf.Clamp(d + 0.15f, -1f, 1f);
            }

            return new EmotionVAD { Valence = v, Arousal = a, Dominance = d };
        }

        // ══════════════════════════════════════════════
        // TIER 2: RULES — now returns EmotionResult?
        // ══════════════════════════════════════════════

        private EmotionResult? TryRules(string key)
        {
            // Laughter
            if (ContainsAny(key, "haha", "hehe", "lol", "lmao", "rofl", "xd"))
            {
                float len = Mathf.Min(key.Length / 15f, 1f);
                return new EmotionResult
                {
                    VAD = new EmotionVAD
                    {
                        Valence = 0.5f + len * 0.3f,
                        Arousal = 0.3f + len * 0.4f,
                        Dominance = 0.1f
                    },
                    Hint = GestureHint.Celebrating
                };
            }

            // ALL CAPS
            if (key.Length > 3 && key == key.ToUpper() && key.Any(char.IsLetter))
            {
                return new EmotionResult
                {
                    VAD = new EmotionVAD { Valence = 0.1f, Arousal = 0.7f, Dominance = 0.3f },
                    Hint = GestureHint.Emphasis
                };
            }

            // Repeated chars
            if (HasRepeats(key))
            {
                bool positive = ContainsAny(key, "yes", "yay", "nice", "wow");
                return new EmotionResult
                {
                    VAD = new EmotionVAD
                    {
                        Valence = positive ? 0.5f : -0.3f,
                        Arousal = 0.5f,
                        Dominance = 0.1f
                    },
                    Hint = positive ? GestureHint.Celebrating : GestureHint.Emphasis
                };
            }

            // Short questions
            string[] qWords = { "kya", "kyun", "kyu", "kaise", "kab", "kahan",
                                 "what", "why", "how", "when", "where" };
            if (key.Split(' ').Length <= 4 && qWords.Any(q => key.Contains(q)))
            {
                return new EmotionResult
                {
                    VAD = new EmotionVAD { Valence = 0.05f, Arousal = 0.25f, Dominance = -0.1f },
                    Hint = GestureHint.Question
                };
            }

            return null;
        }

        private bool HasRepeats(string text)
        {
            for (int i = 2; i < text.Length; i++)
                if (text[i] == text[i - 1] && text[i] == text[i - 2]) return true;
            return false;
        }

        // ══════════════════════════════════════════════
        // TIER 3: AI — UPDATED PROMPT
        // ══════════════════════════════════════════════

        private async UniTask<EmotionResult> AIAnalyze(string message, CancellationToken ct)
        {
            const string prompt = @"You analyze chat messages for a 3D avatar's body animation. You determine TWO things:

1. EMOTIONAL STATE (VAD) — How the speaker FEELS
2. GESTURE HINT (g) — What their HANDS should do

═══ VAD (unchanged) ═══

valence (v): How positive/negative (-1.0 to +1.0)
  -1.0 = furious, devastated  |  0.0 = robotic (very rare)
  +0.1 = casual friendly      |  +1.0 = ecstatic

arousal (a): Energy level (-1.0 to +1.0)  
  -0.3 = calm, chill  |  +0.1 = normal conversation
  +0.5 = animated     |  +1.0 = shouting

dominance (d): How assertive (-1.0 to +1.0)
  -0.3 = unsure, apologetic  |  +0.1 = casual confidence
  +0.5 = commanding          |  +1.0 = aggressive

═══ GESTURE HINT (g) ═══

Pick the ONE gesture that best matches what a real person's hands would naturally do:

""none""         — No particular gesture needed, just ambient movement
""greeting""     — Waving, acknowledging, welcoming someone
""question""     — Palms up asking, curious open hands
""affirmation""  — Small confirming push, agreeing nod-hands
""negation""     — Waving off, blocking, refusing with hands
""emphasis""     — Beating/chopping the air, stressing a point
""uncertainty""  — Shrug, palms up ""I don't know"", open confusion
""calming""      — Pressing palms down, ""take it easy"" gesture
""pointing""     — Directing attention somewhere, deictic gesture
""listing""      — Counting off points, sequential hand positions
""offering""     — Extending hand to give/show something
""dismissal""    — Flicking away, ""forget it"" wave-off
""explaining""   — Hands shaping ideas, illustrating concepts
""celebrating""  — Excited fist pump, clapping, triumph gesture
""requesting""   — Gentle beckoning, asking for something
""storytelling"" — Animated descriptive gestures, painting a scene
""thinking""     — Hand on chin area, contemplative stillness

═══ RULES ═══

1. NEVER return v:0, a:0, d:0. Real people always have SOME tone.
2. Casual messages = mild positive values (v:0.05-0.15, a:0.05-0.15)
3. ""bhai"", ""yaar"" = friendly warmth = slight positive valence
4. The gesture should match WHAT they're DOING with the sentence, not just how they feel
5. A happy person asking a question → g:""question"" (not ""celebrating"")
6. An angry person refusing → g:""negation"" (the action) not just anger
7. Think: ""If I were saying this to a friend, what would my hands do?""

═══ EXAMPLES ═══

""aur kaise ho bhai"" → {""v"":0.20,""a"":0.15,""d"":-0.05,""g"":""greeting""}
""ye dekho kya mila"" → {""v"":0.35,""a"":0.40,""d"":0.15,""g"":""pointing""}
""nahi nahi ye galat hai"" → {""v"":-0.50,""a"":0.40,""d"":0.30,""g"":""negation""}
""pata nahi yaar"" → {""v"":-0.05,""a"":-0.10,""d"":-0.25,""g"":""uncertainty""}
""bahut important hai ye"" → {""v"":0.10,""a"":0.35,""d"":0.30,""g"":""emphasis""}
""pehle ye karo phir wo"" → {""v"":0.10,""a"":0.15,""d"":0.30,""g"":""listing""}
""tension mat le bhai"" → {""v"":0.15,""a"":-0.10,""d"":0.15,""g"":""calming""}
""chodo yaar rehne do"" → {""v"":-0.15,""a"":-0.05,""d"":0.10,""g"":""dismissal""}
""matlab ye hota hai ki"" → {""v"":0.05,""a"":0.15,""d"":0.20,""g"":""explaining""}
""BHAI KYA KAR DIYA"" → {""v"":0.30,""a"":0.70,""d"":0.10,""g"":""celebrating""}
""hmm sochta hu"" → {""v"":0.0,""a"":-0.15,""d"":-0.10,""g"":""thinking""}
""ye lo try karo"" → {""v"":0.20,""a"":0.15,""d"":0.15,""g"":""offering""}
""haan bilkul sahi kaha"" → {""v"":0.25,""a"":0.15,""d"":0.10,""g"":""affirmation""}
""sorry yaar galti ho gayi"" → {""v"":-0.20,""a"":-0.10,""d"":-0.45,""g"":""requesting""}
""ek baar ki baat hai na"" → {""v"":0.10,""a"":0.20,""d"":0.10,""g"":""storytelling""}
""kar lo tum bhai"" → {""v"":0.15,""a"":0.10,""d"":0.25,""g"":""affirmation""}
""mai aata hu"" → {""v"":0.10,""a"":0.15,""d"":0.10,""g"":""none""}
""theek hai chalo"" → {""v"":0.10,""a"":0.10,""d"":0.15,""g"":""affirmation""}

Return ONLY: {""v"":0.0,""a"":0.0,""d"":0.0,""g"":""none""}";

            try
            {
                var msgs = new List<ChatMessage>
                {
                    new ChatMessage { Role = "system", Content = prompt },
                    new ChatMessage { Role = "user",   Content = message }
                };

                var response = await _openai.CreateChatCompletion(new CreateChatCompletionRequest
                {
                    Model = "gpt-4o-mini",
                    Messages = msgs,
                    Temperature = 0.15f
                });

                ct.ThrowIfCancellationRequested();

                if (response.Choices?.Count > 0)
                    return ParseResult(response.Choices[0].Message.Content);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Debug.LogError($"[Emotion] AI error: {ex.Message}"); }

            return QuickEstimate(message);
        }

        // ══════════════════════════════════════════════
        // PARSING — UPDATED
        // ══════════════════════════════════════════════

        private EmotionResult ParseResult(string raw)
        {
            try
            {
                raw = raw.Replace("```json", "").Replace("```", "").Trim();
                int s = raw.IndexOf('{'), e = raw.LastIndexOf('}');
                if (s >= 0 && e > s) raw = raw.Substring(s, e - s + 1);

                var d = JsonConvert.DeserializeObject<VADGData>(raw);
                return new EmotionResult
                {
                    VAD = new EmotionVAD
                    {
                        Valence = Mathf.Clamp(d.v, -1f, 1f),
                        Arousal = Mathf.Clamp(d.a, -1f, 1f),
                        Dominance = Mathf.Clamp(d.d, -1f, 1f)
                    },
                    Hint = ParseGestureHint(d.g)
                };
            }
            catch
            {
                return new EmotionResult
                {
                    VAD = EmotionVAD.Neutral,
                    Hint = GestureHint.None
                };
            }
        }

        [Serializable]
        private class VADGData
        {
            public float v, a, d;
            public string g;
        }

        private static GestureHint ParseGestureHint(string g)
        {
            if (string.IsNullOrEmpty(g)) return GestureHint.None;

            switch (g.ToLower().Trim())
            {
                case "greeting": return GestureHint.Greeting;
                case "question": return GestureHint.Question;
                case "affirmation": return GestureHint.Affirmation;
                case "negation": return GestureHint.Negation;
                case "emphasis": return GestureHint.Emphasis;
                case "uncertainty": return GestureHint.Uncertainty;
                case "calming": return GestureHint.Calming;
                case "pointing": return GestureHint.Pointing;
                case "listing": return GestureHint.Listing;
                case "offering": return GestureHint.Offering;
                case "dismissal": return GestureHint.Dismissal;
                case "explaining": return GestureHint.Explaining;
                case "celebrating": return GestureHint.Celebrating;
                case "requesting": return GestureHint.Requesting;
                case "storytelling": return GestureHint.Storytelling;
                case "thinking": return GestureHint.Thinking;
                case "none": return GestureHint.None;
                default:
                    Debug.LogWarning($"[Emotion] Unknown gesture hint: {g}");
                    return GestureHint.None;
            }
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            foreach (var kw in keywords)
                if (text.Contains(kw)) return true;
            return false;
        }

        // ══════════════════════════════════════════════
        // CACHE — UPDATED for EmotionResult
        // ══════════════════════════════════════════════

        private EmotionVAD Vary(EmotionVAD e, float amount = 0.02f)
        {
            return new EmotionVAD
            {
                Valence = Mathf.Clamp(e.Valence + UnityEngine.Random.Range(-amount, amount), -1f, 1f),
                Arousal = Mathf.Clamp(e.Arousal + UnityEngine.Random.Range(-amount, amount), -1f, 1f),
                Dominance = Mathf.Clamp(e.Dominance + UnityEngine.Random.Range(-amount, amount), -1f, 1f)
            };
        }

        private void Cache(string key, EmotionResult value)
        {
            if (_cache.Count >= MAX_CACHE)
                _cache.Remove(_cache.Keys.First());
            _cache[key] = value;
        }

        private void InitCache()
        {
            // Helper to reduce boilerplate
            void C(string key, float v, float a, float d, GestureHint g)
            {
                _cache[key] = new EmotionResult
                {
                    VAD = new EmotionVAD { Valence = v, Arousal = a, Dominance = d },
                    Hint = g
                };
            }

            // Greetings
            C("hi", 0.4f, 0.25f, 0.05f, GestureHint.Greeting);
            C("hello", 0.5f, 0.3f, 0.05f, GestureHint.Greeting);
            C("hey", 0.3f, 0.2f, 0.1f, GestureHint.Greeting);
            C("namaste", 0.5f, 0.25f, -0.05f, GestureHint.Greeting);

            // Affirmations
            C("ok", 0.05f, 0.05f, 0.05f, GestureHint.Affirmation);
            C("okay", 0.1f, 0.05f, 0.05f, GestureHint.Affirmation);
            C("yes", 0.3f, 0.15f, 0.1f, GestureHint.Affirmation);
            C("haan", 0.2f, 0.1f, 0.05f, GestureHint.Affirmation);
            C("haan bhai", 0.2f, 0.1f, 0.1f, GestureHint.Affirmation);
            C("sahi hai", 0.25f, 0.1f, 0.15f, GestureHint.Affirmation);
            C("theek hai", 0.05f, 0.05f, 0.05f, GestureHint.Affirmation);
            C("achha", 0.15f, 0.1f, 0.05f, GestureHint.Affirmation);
            C("accha", 0.15f, 0.1f, 0.05f, GestureHint.Affirmation);
            C("samajh gaya", 0.15f, 0.1f, 0.1f, GestureHint.Affirmation);
            C("mast", 0.4f, 0.2f, 0.1f, GestureHint.Affirmation);
            C("badhiya", 0.4f, 0.2f, 0.1f, GestureHint.Affirmation);

            // Negation
            C("no", -0.2f, 0.1f, 0.2f, GestureHint.Negation);
            C("nahi", -0.2f, 0.1f, 0.15f, GestureHint.Negation);
            C("k", -0.05f, 0.05f, 0.1f, GestureHint.Dismissal);

            // Uncertainty / Thinking
            C("hmm", 0.0f, 0.05f, -0.1f, GestureHint.Thinking);

            // Questions
            C("kya", 0.0f, 0.25f, -0.05f, GestureHint.Question);

            // Social
            C("bye", 0.15f, 0.1f, 0.0f, GestureHint.Greeting);
            C("thanks", 0.5f, 0.15f, -0.15f, GestureHint.Offering);
            C("sorry", -0.15f, -0.05f, -0.4f, GestureHint.Requesting);

            // Expressive
            C("wow", 0.6f, 0.7f, -0.1f, GestureHint.Celebrating);
            C("bruh", -0.15f, 0.2f, 0.15f, GestureHint.Dismissal);
            C("bhai", 0.15f, 0.1f, 0.05f, GestureHint.Greeting);
            C("are yaar", -0.05f, 0.2f, 0.05f, GestureHint.Emphasis);
            C("lol", 0.5f, 0.4f, 0.1f, GestureHint.Celebrating);
            C("sheesh", 0.4f, 0.7f, -0.1f, GestureHint.Celebrating);
            C("slay", 0.7f, 0.6f, 0.6f, GestureHint.Celebrating);

            // Directional
            C("chalo", 0.1f, 0.15f, 0.2f, GestureHint.Affirmation);
            C("chal", 0.1f, 0.15f, 0.2f, GestureHint.Affirmation);
            C("pata hai", 0.05f, 0.1f, 0.2f, GestureHint.Explaining);
            C("bol", 0.05f, 0.15f, 0.2f, GestureHint.Requesting);
            C("sun", 0.0f, 0.2f, 0.25f, GestureHint.Requesting);
            C("dekh", 0.1f, 0.2f, 0.2f, GestureHint.Pointing);
            C("ruk", -0.05f, 0.15f, 0.3f, GestureHint.Calming);
            C("bas", -0.1f, 0.1f, 0.2f, GestureHint.Dismissal);

            Debug.Log($"[Emotion] Cache initialized: {_cache.Count} entries");
        }
    }
}