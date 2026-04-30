using Cysharp.Threading.Tasks;
using FishNet.Object;
using Newtonsoft.Json;
using OpenAI;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

namespace Mikk.Avatar.Expression
{
    public class Playaudio : NetworkBehaviour
    {
        //  public UnityEngine.UI.Button sendButton;
        //  public InputField nputField;
        //  public ChatGPT tts;

        [Tooltip("Skinned Mesh Rendered target to be driven by Oculus Lipsync")]
        public SkinnedMeshRenderer skinnedMeshRenderer = null;

        [Tooltip("Blendshape index to trigger for each viseme.")]
        public int[] visemeToBlendTargets = Enumerable.Range(0, OVRLipSync.VisemeCount).ToArray();

        [Tooltip("Enable using the test keys defined below to manually trigger each viseme.")]
        public bool enableVisemeTestKeys = false;

        public KeyCode[] visemeTestKeys =
        {
        KeyCode.BackQuote, KeyCode.Tab, KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T,
        KeyCode.Y, KeyCode.U, KeyCode.I, KeyCode.O, KeyCode.P, KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.Backslash
    };

        [Tooltip("Test key used to manually trigger laughter")]
        public KeyCode laughterKey = KeyCode.CapsLock;

        [Tooltip("Blendshape index to trigger for laughter")]
        public int laughterBlendTarget = OVRLipSync.VisemeCount;

        [Range(0.0f, 1.0f)]
        public float laughterThreshold = 0.5f;

        [Range(0.0f, 3.0f)]
        public float laughterMultiplier = 1.5f;

        [Range(1, 100)]
        public int smoothAmount = 70;



        [Range(0.0f, 3.0f)]
        [Tooltip("Global multiplier for viseme intensity (0-1 range). Adjust this if the mouth shapes are too subtle.")]
        public float visemeIntensityMultiplier = 1.8f; // Start with a value around 1.8 

        private OVRLipSyncContextBase lipsyncContext = null;




        public delegate void VisemeDataUpdated(float[] visemes);
        public delegate void LaughterDataUpdated(float laughter);

        public event System.Action<float[]> OnVisemeDataUpdated;
        public event System.Action<float> OnLaughterDataUpdated;

        private OVRLipSync.Frame frame = null;

        public AudioMixerGroup myMixerGroup;

        public AudioMixer myMixer;

        private float lastFrameTime = 0f;
        private const float LIPSYNC_UPDATE_RATE = 1f / 30f;
        private bool isAudioPlaying = false;


        [SerializeField] private string _apiKey;
        [SerializeField]
        private string _apiUrl = "https://api.elevenlabs.io";


        private bool streamCompleted = false;
        private bool allAudioQueued = false;

        [SerializeField] private int latencyOptimization = 1; // 0-4, higher = lower latency
        [SerializeField] private float bufferTime = 0.5f; // Buffer before starting playback


        // Threading and streaming management
        private CancellationTokenSource ttsTokenSource;
        private ConcurrentQueue<float[]> audioChunkQueue = new ConcurrentQueue<float[]>();
        private volatile bool isProcessingAudio = false;
        private Queue<AudioClip> audioClipPool = new Queue<AudioClip>();
        private const int MAX_AUDIO_POOL_SIZE = 5;

        // Audio streaming constants
        private const int streamingSampleRate = 16000; // PCM 16kHz
        private const int streamingChannels = 1; // Mono
        private const int minChunkSize = 4096; // Process in 4KB chunks
        private const int targetBufferSize = 8192; // 0.5 seconds at 16kHz


        // Audio playback management
        private Queue<AudioClip> playbackQueue = new Queue<AudioClip>();
        private bool isPlaybackActive = false;


        [Header("Chat-Optimized TTS")]
        [SerializeField] private bool enableSmartCaching = true;
        [SerializeField] private bool prioritizeLatency = true;
        [SerializeField] private int maxSimultaneousTTS = 3;

        [SerializeField]
        public string ttvoice;


        public AudioSource selfvoicesource;


        //  private const string ApiUrl = "https://api.openai.com/v1/chat/completions"; // OpenAI API endpoint
        //  private OpenAIApi openai = new OpenAIApi("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");


        //  [SerializeField] private string openAIApiKey = "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";
        //  private OpenAIApi openai;

        [Header("Hinglish Conversion Settings")]
        [SerializeField] private string OpenApiKey = "your_api_key";
        [SerializeField] private string OpenApiUrl = "https://api.openai.com/v1/chat/completions";
        [SerializeField] private float requestTimeout = 10f; // 10 second timeout

        // Reuse HttpClient for better performance
        private static readonly HttpClient httpClient = new HttpClient();
        private CancellationTokenSource conversionTokenSource;

        // Cache for conversion results
        private Dictionary<string, string> conversionCache = new Dictionary<string, string>();
        private const int MAX_CACHE_SIZE = 100;


        // KEEP these (per-instance, no tracking needed):
        private List<byte[]> remoteAudioBuffer = new List<byte[]>(); // Single buffer per instance
        private int expectedSequenceNumber = 0; // Single sequence tracker
        private const int NETWORK_BUFFER_CHUNKS = 3;

        // STREAMING AUDIO: Sequenced chunks
        private int currentSequenceNumber = 0;





        void Start()
        {

            skinnedMeshRenderer = gameObject.transform.parent.GetChild(2).GetChild(0).Find("Head_Mesh").GetComponent<SkinnedMeshRenderer>();
            selfvoicesource = gameObject.transform.parent.GetChild(2).GetComponent<AudioSource>();
            selfvoicesource.outputAudioMixerGroup = myMixerGroup;


            if (skinnedMeshRenderer == null)
            {
                Debug.LogError("Please set the target Skinned Mesh Renderer!");
                return;
            }

            lipsyncContext = transform.parent.GetChild(2).GetComponent<OVRLipSyncContextBase>();
            if (lipsyncContext == null)
            {
                Debug.LogError("No OVRLipSyncContext component found!");
            }
            else
            {
                lipsyncContext.Smoothing = smoothAmount;
            }

            // frame = lipsyncContext.GetCurrentPhonemeFrame();

            if (selfvoicesource != null)
            {
                // You might need to implement these events or use coroutines
                StartCoroutine(MonitorAudioPlayback());
            }

            if (httpClient.DefaultRequestHeaders.Authorization == null)
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + OpenApiKey);
                httpClient.Timeout = TimeSpan.FromSeconds(30); // Global timeout
            }


        }


        private IEnumerator MonitorAudioPlayback()
        {
            while (true)
            {
                bool wasPlaying = isAudioPlaying;
                isAudioPlaying = selfvoicesource.isPlaying;

                // Reset blendshapes when audio stops
                if (wasPlaying && !isAudioPlaying)
                {
                    ResetVisemes();
                }

                yield return new WaitForSeconds(0.1f); // Check every 100ms
            }
        }

        private void ResetVisemes()
        {
            if (skinnedMeshRenderer == null) return;

            // Reset all viseme blendshapes to 0
            for (int i = 0; i < visemeToBlendTargets.Length; i++)
            {
                if (visemeToBlendTargets[i] != -1)
                {
                    skinnedMeshRenderer.SetBlendShapeWeight(visemeToBlendTargets[i], 0f);
                }
            }

            // Reset laughter
            if (laughterBlendTarget != -1)
            {
                skinnedMeshRenderer.SetBlendShapeWeight(laughterBlendTarget, 0f);
            }
        }


        private void Update()
        {
            if (isAudioPlaying && Time.time - lastFrameTime >= LIPSYNC_UPDATE_RATE)
            {
                UpdateLipsync();
                lastFrameTime = Time.time;
            }
        }

        private void UpdateLipsync()
        {
            if (lipsyncContext == null || skinnedMeshRenderer == null) return;

            // Get fresh frame data
            frame = lipsyncContext.GetCurrentPhonemeFrame();

            if (frame != null)
            {
                SetVisemeToMorphTarget(frame);
                SetLaughterToMorphTarget(frame);

                OnVisemeDataUpdated?.Invoke(frame.Visemes);
                OnLaughterDataUpdated?.Invoke(frame.laughterScore);
            }
        }



        void SetVisemeToMorphTarget(OVRLipSync.Frame frame)
        {

            for (int i = 0; i < visemeToBlendTargets.Length; i++)
            {
                if (visemeToBlendTargets[i] != -1)
                {

                    // skinnedMeshRenderer.SetBlendShapeWeight(visemeToBlendTargets[i], frame.Visemes[i] * 1.0f);


                    float finalWeight = frame.Visemes[i]; // The base weight (0 to 1)

                    // Apply the intensity multiplier
                    finalWeight *= visemeIntensityMultiplier;

                    // Clamp the value to ensure it stays within the expected 0f to 1f range of the GLB model
                    finalWeight = Mathf.Clamp(finalWeight, 0f, 1f);

                    skinnedMeshRenderer.SetBlendShapeWeight(
                        visemeToBlendTargets[i],
                        finalWeight);


                }
            }
        }

        void SetLaughterToMorphTarget(OVRLipSync.Frame frame)
        {
            if (laughterBlendTarget != -1)
            {
                float laughterScore = frame.laughterScore;
                laughterScore = laughterScore < laughterThreshold ? 0.0f : laughterScore - laughterThreshold;
                laughterScore = Mathf.Min(laughterScore * laughterMultiplier, 1.0f);
                laughterScore *= 1.0f / laughterThreshold;


                skinnedMeshRenderer.SetBlendShapeWeight(laughterBlendTarget, laughterScore);





            }
        }


        public void MuteAudio()
        {
            myMixer.SetFloat("Volume", -80f); // Mute
        }

        public void UnmuteAudio()
        {
            myMixer.SetFloat("Volume", 0f); // Full volume
        }





        public void Convertext(string text)
        {


            conversionTokenSource?.Cancel();
            conversionTokenSource = new CancellationTokenSource();



            ConvertHinglishToHindiAsync(text).Forget();
        }

        private async UniTaskVoid ConvertHinglishToHindiAsync(string hinglishInput)
        {
            try
            {
                await ConvertHinglishToHindiInternal(hinglishInput, conversionTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Hinglish conversion cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Hinglish conversion failed: {ex.Message}");
                // Fallback: use original text
                GoSound(hinglishInput);
            }




            /*
                    if (IsPureEnglish(hinglishInput))
                    {
                        GoSound(hinglishInput);
                        Debug.Log("Pure English detected, kept as-is: " + hinglishInput);
                        return;
                    }

                    using (HttpClient client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + ApiKey);

                        // Construct the JSON payload with improved system prompt
                        var requestBody = new
                        {
                            model = "gpt-4",
                            messages = new[]
                            {
                            new {
                                role = "system",
                                content = @"You are a Hinglish Normalizer. Your task is to convert ONLY Hinglish text (mixed Hindi-English) into clear Hindi text. 

            IMPORTANT RULES:
            1. If the input is PURE ENGLISH (no Hindi words mixed), return it as-is without translation
            2. Only convert text that contains a MIX of Hindi and English words (actual Hinglish)
            3. Keep conversational tone and avoid formal alternatives
            4. Don't add extra words like 'हूँ' or 'है' unless explicitly in input

            Examples:
            ✅ CONVERT (Hinglish):
            - 'wo pro player hai' → 'वो प्रो प्लेयर है'
            - 'tushe pata' → 'तुझे पता'
            - 'tm kaha ho' → 'तुम कहाँ हो'
            - 'are bhai' → 'अरे भाई'

            ❌ DON'T CONVERT (Pure English):
            - 'I am coming' → 'I am coming' (keep as-is)
            - 'Hello how are you' → 'Hello how are you' (keep as-is)
            - 'Good morning' → 'Good morning' (keep as-is)"
                            },
                            new { role = "user", content = hinglishInput }
                        },
                            temperature = 0.3  // Lower temperature for more consistent behavior
                        };

                        // Serialize using Newtonsoft.Json
                        string jsonBody = JsonConvert.SerializeObject(requestBody);

                        HttpContent content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                        HttpResponseMessage response = await client.PostAsync(ApiUrl, content);

                        if (response.IsSuccessStatusCode)
                        {
                            string responseBody = await response.Content.ReadAsStringAsync();
                            var openAIResponse = JsonConvert.DeserializeObject<OpenAIResponse>(responseBody);
                            string processedText = openAIResponse.choices[0].message.content;

                            GoSound(processedText);
                            Debug.Log("Processed Text: " + processedText);
                        }
                        else
                        {
                            string errorResponse = await response.Content.ReadAsStringAsync();
                            Debug.LogError("API Error: " + response.StatusCode + " - " + errorResponse);
                        }
                    }*/



        }


        private async UniTask ConvertHinglishToHindiInternal(string hinglishInput, CancellationToken cancellationToken)
        {

            if (conversionCache.TryGetValue(hinglishInput.ToLower().Trim(), out var cachedResult))
            {
                Debug.Log($"Using cached conversion: {hinglishInput} → {cachedResult}");
                GoSound(cachedResult);
                return;
            }

            string processedInput = ProcessEmojis(hinglishInput, out bool shouldSendToAPI);

            if (string.IsNullOrWhiteSpace(processedInput))
            {
                Debug.Log("Input was only non-laughing emojis, ignoring.");
                return; // Nothing to process
            }




            var directConversion = HandleCommonHindiSounds(processedInput);
            if (directConversion != null)
            {
                // CacheConversionResult(hinglishInput, directConversion);
                GoSound(directConversion);
                return;
            }



            if (shouldSendToAPI)
            {
                var processedText = await ProcessHinglishConversion(processedInput, cancellationToken);

                // Cache the result
                CacheConversionResult(processedInput, processedText);

                // Play the audio
                GoSound(processedText);
            }
            else
            {

                GoSound(processedInput); // Direct output (like "hahahaha!")
            }












            // Process on background thread to avoid blocking main thread





        }


        private string ProcessEmojis(string input, out bool shouldSendToAPI)
        {
            shouldSendToAPI = true;

            // Define laughing emojis
            string[] laughingEmojis = { "😂", "🤣", "😄", "😃", "😀", "😊", "😆", "😁" };

            // Check for laughing emojis
            bool hasLaughingEmojis = laughingEmojis.Any(emoji => input.Contains(emoji));

            // Remove all emojis (simple approach)
            string textOnly = "";
            foreach (char c in input)
            {
                // Keep only basic text characters
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
                {
                    // Skip emoji ranges
                    if (c >= 0x1F600 && c <= 0x1F64F) continue; // Emoticons
                    if (c >= 0x1F300 && c <= 0x1F5FF) continue; // Misc symbols
                    if (c >= 0x1F680 && c <= 0x1F6FF) continue; // Transport
                    if (c >= 0x2600 && c <= 0x26FF) continue;   // Misc symbols
                    if (c >= 0x2700 && c <= 0x27BF) continue;   // Dingbats

                    textOnly += c;
                }
            }

            textOnly = textOnly.Trim();

            // Decision logic
            if (string.IsNullOrWhiteSpace(textOnly))
            {
                if (hasLaughingEmojis)
                {
                    shouldSendToAPI = false;
                    return "hahahaha!";
                }
                else
                {
                    shouldSendToAPI = false;
                    return "";
                }
            }
            else
            {
                if (hasLaughingEmojis)
                {
                    return textOnly + " hahahaha";
                }
                else
                {
                    return textOnly;
                }
            }
        }







        private string HandleCommonHindiSounds(string input)
        {
            var lowerInput = input.ToLower().Trim();

            // FIXED: Only handle SINGLE Hindi words/sounds, not mixed sentences
            var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Only handle single words or very short phrases (2 words max)
            if (words.Length > 2)
            {
                return null; // Let mixed sentences go to API
            }

            // Common single Hindi sounds/expressions
            var hindiSounds = new Dictionary<string, string>
    {
        // Single character responses
        {"h", "हाँ"},
        {"k", "ओके"},
        {"oky", "ओके"},
        {"okay", "ओके"},
        {"ok", "ओके"},
        {"okk", "ओके"},
        {"okkk", "ओके"},
        {"okkkk", "ओके"},
        



        // Common sounds
        {"hmm", "हूँ..."},
        {"hmmm", "हूँ....."},
        {"hmmmm", "हूँ....."},
        {"hu", "हूँ.."},
        {"hoon", "हूँ.."},
        {"haan", "हाँ"},
        {"han", "हाँ"},
        {"nahi", "नहीं"},
        {"nahin", "नहीं"},
        {"kya", "क्या"},
        {"hai", "है"},
        {"ho", "हो"},

        // Sweet words 
        {"madarchod", "मादरचोद"},
        {"behenchod", "बहनचोद"},
        {"benchod", "बहनचोद"},
        {"bencho", "बेंचो"},
        {"bosidke", "भोसड़ीके"},
        {"bsdk", "भोसड़ीके"},
        {"bkl", "बहन  के  लौढ़े"},
        {"chutiya", "चूतीया"},
        {"lodu", "लौढ़ू"},
        {"gandu" ,"गांडू"},
        {"mkc" ,"माँ  की  चूत"},
        {"behen ke lode", "बहन  के  लौढ़े"},


         
        
        // Common expressions
        {"achha", "अच्छा"},
        {"acha", "अच्छा"},
        {"theek", "ठीक"},
        {"thik", "ठीक"},
        {"sahi", "सही"},
        {"galat", "गलत"},
        {"bas", "बस"},
        {"chalo", "चलो"},
        {"aao", "आओ"},
        {"jao", "जाओ"},
        {"karo", "करो"},
        {"dekho", "देखो"},
        {"suno", "सुनो"},
        {"arre","अरे" },
        {"arey","अरे" },
        {"abey","अबे" },
        {"abe","अबे" },

       
        
        // Common words
        {"bhai", "भाई"},
        {"yaar", "यार"},
        {"dost", "दोस्त"},
        {"ghar", "घर"},
       
        
        // Question words
        {"kab", "कब"},
        {"kb", "कब"},
        {"kahan", "कहाँ"},
        {"kaha", "कहाँ"},
        {"kaise", "कैसे"},
        {"kyun", "क्यों"},
        {"kyu", "क्यों"},
        {"kaun", "कौन"},
        
        // Pronouns
        {"main", "मैं"},
        {"mai", "मैं"},
        {"tum", "तुम"},
        {"wo", "वो"},
        {"woh", "वो"},
        {"ye", "ये"},
        {"yeh", "ये"},
        {"hum", "हम"},
        {"aap", "आप"},
        
        // Two-word common phrases
        {"theek hai", "ठीक है"},
        {"kya hai", "क्या है"},
        {"kya ho", "क्या हो"},
        {"main hu", "मैं हूँ"},
        {"tu hai", "तू है"},
        {"wo hai", "वो है"}
    };

            // Check for exact matches
            if (hindiSounds.TryGetValue(lowerInput, out var directConversion))
            {
                Debug.Log($"Direct conversion: {input} → {directConversion}");
                return directConversion;
            }

            return null; // No direct match, let it go to next step

        }










        private void CacheConversionResult(string hinglishInput, string processedText)
        {
            var key = hinglishInput.ToLower().Trim();

            // Manage cache size
            if (conversionCache.Count >= MAX_CACHE_SIZE)
            {
                // Remove oldest entries (simple FIFO)
                var firstKey = conversionCache.Keys.First();
                conversionCache.Remove(firstKey);
            }

            conversionCache[key] = processedText;
        }


        private async UniTask<string> ProcessHinglishConversion(string hinglishInput, CancellationToken cancellationToken)
        {
            var requestData = await UniTask.RunOnThreadPool(() =>
            {
                var requestBody = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                    new {
                        role = "system",
                        content = @"You are a Hinglish Normalizer. Convert Hinglish text (mixed Hindi-English) into clear Hindi text.

IMPORTANT RULES:
1. Keep PURE ENGLISH sentences as-is (complete English sentences)
2. Don't add extra words like 'हूँ' or 'है' unless explicitly in input
3. Keep conversational tone and avoid formal alternatives
4. ALWAYS add appropriate punctuation to ALL responses for better TTS pronunciation





✅ Mixed Hinglish sentences:
- 'wo pro player hai' → 'वो प्रो प्लेयर है'
- 'main busy hu' → 'मैं बिज़ी हूँ'
- 'kya kar rahe ho' → 'क्या कर रहे हो'
- 'are bhai' → 'अरे भाई'

❌ DON'T CONVERT (Complete English sentences):
- 'I am coming home' → 'I am coming home' (keep as-is)
- 'Hello how are you today' → 'Hello how are you today' (keep as-is)
- 'Good morning everyone' → 'Good morning everyone' (keep as-is)
- 'What time is the meeting' → 'What time is the meeting' (keep as-is)

PUNCTUATION RULES (Apply to ALL responses):
- Questions: Add question mark (?)
  'kya' → 'क्या?', 'kya kar rahe ho' → 'क्या कर रहे हो?'
- Affirmative/Agreement: Add exclamation mark (!)
  'achha' → 'अच्छा!', 'theek hai bhai' → 'ठीक है भाई!'
- Thoughtful/Hesitation: Add ellipsis (...)
  'hmm' → 'हम्म...', 'main soch raha hu' → 'मैं सोच रहा हूँ...'
- Statements: Add period (.)
  'nahi' → 'नहीं.', 'main ghar ja raha hu' → 'मैं घर जा रहा हूँ.'
- Excitement: Add exclamation (!)
  'wow' → 'wow!', 'are yaar kya baat hai' → 'अरे यार क्या बात है!'

EXAMPLES:
- 'achha' → 'अच्छा!'
- 'kya kar rahe ho' → 'क्या कर रहे हो?'
- 'main busy hu aaj' → 'मैं बिज़ी हूँ आज.'
- 'are bhai kya scene hai' → 'अरे भाई क्या सीन है!'




LOGIC:
- If input contains Hindi words mixed with English → Convert Hindi parts
- If input is complete English sentence (no Hindi sounds) → Keep as-is
- When in doubt, favor conversion for better Hindi TTS pronunciation"



                    },
                    new { role = "user", content = hinglishInput }
                    },
                    temperature = 0.3
                };

                return JsonConvert.SerializeObject(requestBody);
            }, cancellationToken: cancellationToken);

            // Make HTTP request with timeout
            var content = new StringContent(requestData, Encoding.UTF8, "application/json");

            // Use UniTask for HTTP request with timeout
            var response = await httpClient.PostAsync(OpenApiUrl, content, cancellationToken)
          .AsUniTask()
          .Timeout(TimeSpan.FromSeconds(requestTimeout));

            if (!response.IsSuccessStatusCode)
            {
                string errorResponse = await response.Content.ReadAsStringAsync();
                throw new Exception($"API Error: {response.StatusCode} - {errorResponse}");
            }

            // Read and parse response on background thread
            var processedText = await UniTask.RunOnThreadPool(async () =>
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                var openAIResponse = JsonConvert.DeserializeObject<OpenAIResponse>(responseBody);
                return openAIResponse.choices[0].message.content;
            }, cancellationToken: cancellationToken);

            return processedText;

        }









        private void GoSound(string hinglishInput)
        {
            // Log when we're cancelling previous request
            if (ttsTokenSource != null && !ttsTokenSource.Token.IsCancellationRequested)
            {
                Debug.Log($"Cancelling previous TTS to start new request: '{hinglishInput.Substring(0, Math.Min(30, hinglishInput.Length))}...'");
            }


            ttsTokenSource?.Cancel();
            ttsTokenSource = new CancellationTokenSource();


            HandleSmartTTS(hinglishInput).Forget();



        }



        private async UniTaskVoid HandleSmartTTS(string text)
        {
            try
            {
                // Quick decision for chat messages
                if (IsShortChatMessage(text))
                {
                    Debug.Log($"Using OPTIMIZED TTS for short message: '{text.Substring(0, Math.Min(30, text.Length))}...'");
                    await SendRegularTTSRequest(text, ttsTokenSource.Token);
                }
                else
                {
                    Debug.Log($"Using STREAMING TTS for long message: '{text.Substring(0, Math.Min(30, text.Length))}...'");
                    await SendStreamingTTSRequest(text, ttsTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("TTS cancelled for new message");
            }
            catch (Exception ex)
            {
                Debug.LogError($"TTS failed: {ex.Message}");
            }
        }

        private bool IsShortChatMessage(string text)
        {
            // Chat-specific heuristics
            return text.Length <= 50 ||                    // Short text
                   text.Split(' ').Length <= 8 ||          // Few words
                   IsCommonChatPattern(text) ||             // Common patterns
                   EstimateAudioDuration(text) <= 3f;      // Short audio
        }

        private bool IsCommonChatPattern(string text)
        {
            var lowerText = text.ToLower().Trim();

            // Common chat patterns that are always short
            var shortPatterns = new[]
            {
        "lol", "haha", "hehe", "ok", "okay", "yes", "no", "hi", "hello", "hey",
        "bye", "thanks", "thank you", "welcome", "sorry", "wow", "nice", "cool",
        "good", "great", "bad", "maybe", "sure", "nope", "yep", "right", "exactly",
        "true", "false", "agree", "disagree", "kya", "haan", "nahi", "theek hai",
        "achha", "wah", "are yaar", "bhai", "dude"
    };

            return shortPatterns.Any(pattern =>
                lowerText == pattern ||
                lowerText.StartsWith(pattern + " ") ||
                lowerText.EndsWith(" " + pattern) ||
                lowerText.Contains(" " + pattern + " "));
        }

        private float EstimateAudioDuration(string text)
        {
            // Estimation based on typical speech rates
            // Average: 150-160 words per minute, ~5 characters per word
            float wordsPerMinute = 150f;
            float charactersPerWord = 5f;
            float estimatedWords = text.Length / charactersPerWord;
            float estimatedMinutes = estimatedWords / wordsPerMinute;
            return estimatedMinutes * 60f; // Convert to seconds
        }



        private async UniTask SendStreamingTTSRequest(string responseText, CancellationToken cancellationToken)
        {
            streamCompleted = false;
            allAudioQueued = false;

            var postData = new TextToSpeechRequest
            {
                text = responseText,
                model_id = "eleven_turbo_v2_5",
                voice_settings = new VoiceSettings
                {
                    stability = 0.4f,
                    similarity_boost = 0.75f,
                    style = 0.0f,
                    use_speaker_boost = true
                }
            };

            var json = JsonConvert.SerializeObject(postData);
            var url = $"{_apiUrl}/v1/text-to-speech/{ttvoice}/stream" +
                      $"?model_id=eleven_multilingual_v2_5" +
                      $"&optimize_streaming_latency={latencyOptimization}" +
                      $"&output_format=pcm_16000";

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));

            var streamingHandler = new StreamingDownloadHandler();
            request.downloadHandler = streamingHandler;

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("xi-api-key", _apiKey);
            request.SetRequestHeader("Accept", "audio/pcm");

            cancellationToken.ThrowIfCancellationRequested();

            ClearAudioBuffers();

            // Start tasks
            var audioProcessingTask = ProcessStreamingAudioBackground(streamingHandler, cancellationToken);
            var playbackTask = StartAudioPlaybackLoop(cancellationToken);
            var networkTask = request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

            // Wait for network to complete
            await networkTask;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Streaming TTS Error: {request.error}");
                return;
            }

            Debug.Log("Network request completed, waiting for audio processing...");
            streamCompleted = true; // Mark stream as completed

            // Wait for audio processing to finish
            await audioProcessingTask;

            Debug.Log("Audio processing completed, waiting for playback...");
            allAudioQueued = true; // Mark all audio as queued

            // Wait for playback to finish
            await playbackTask;

            // CRITICAL: Additional wait for all audio to actually play
            await WaitForAllAudioToFinish();

            Debug.Log("Streaming TTS completed successfully");




            /*var postData = new TextToSpeechRequest
            {
                text = responseText,
                model_id = "eleven_turbo_v2_5",
                voice_settings = new VoiceSettings
                {
                    stability = 0.4f,
                    similarity_boost = 0.75f,
                    style = 0.0f,
                    use_speaker_boost = true
                }
            };

            var json = JsonConvert.SerializeObject(postData);
            var url = $"{_apiUrl}/v1/text-to-speech/{ttvoice}/stream" +
                      $"?model_id=eleven_multilingual_v2_5" +
                      $"&optimize_streaming_latency={latencyOptimization}" +
                      $"&output_format=pcm_16000";

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));

            var streamingHandler = new StreamingDownloadHandler();
            request.downloadHandler = streamingHandler;

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("xi-api-key", _apiKey);
            request.SetRequestHeader("Accept", "audio/pcm");

            cancellationToken.ThrowIfCancellationRequested();

            // Clear previous audio data
            ClearAudioBuffers();

            var audioProcessingTask = ProcessStreamingAudioBackground(streamingHandler, cancellationToken);

            // Start main thread audio playback
            var playbackTask = StartAudioPlaybackLoop(cancellationToken);

            // Start network request
            var networkTask = request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

            // Wait for all tasks to complete
            await UniTask.WhenAll(networkTask, audioProcessingTask, playbackTask);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Streaming TTS Error: {request.error}");
                return;
            }

            // CRITICAL: Wait for ALL audio to finish playing
            await WaitForAudioCompletion();

            Debug.Log("Streaming TTS completed successfully");*/
        }

        // NEW: Ensure all audio finishes playing
        private async UniTask WaitForAudioCompletion()
        {
            Debug.Log("Waiting for audio completion...");

            // Wait for playback queue to empty
            while (playbackQueue.Count > 0)
            {
                Debug.Log($"Waiting for {playbackQueue.Count} audio chunks to play");
                await UniTask.Delay(50);
            }

            // Wait for current audio to finish
            while (selfvoicesource.isPlaying)
            {
                Debug.Log($"Waiting for current audio to finish: {selfvoicesource.time:F2}/{selfvoicesource.clip.length:F2}");
                await UniTask.Delay(50);
            }

            // Extra safety buffer
            await UniTask.Delay(200);

            Debug.Log("All audio playback completed");


            // Start background audio processing
            /*var audioProcessingTask = ProcessStreamingAudioBackground(streamingHandler, cancellationToken);

            // Start main thread audio playback
            var playbackTask = StartAudioPlaybackLoop(cancellationToken);

            // Start network request (runs in background)
            var networkTask = request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

            // Wait for all tasks to complete
            await UniTask.WhenAll(networkTask, audioProcessingTask, playbackTask);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Streaming TTS Error: {request.error}");
                return;
            }

            Debug.Log("Streaming TTS completed successfully");*/
        }

        // Background audio processing
        private async UniTask ProcessStreamingAudioBackground(StreamingDownloadHandler handler, CancellationToken cancellationToken)
        {

            isProcessingAudio = true;
            var processedBytes = 0;
            currentSequenceNumber = 0; // Reset for new stream

            await UniTask.RunOnThreadPool(async () =>
            {
                var buffer = new List<byte>();
                var networkChunkBuffer = new List<byte>();

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var availableData = handler.GetAvailableData(processedBytes);

                        if (availableData.Length > 0)
                        {
                            buffer.AddRange(availableData);
                            networkChunkBuffer.AddRange(availableData);
                            processedBytes += availableData.Length;

                            // Send 8KB chunks to network
                            if (networkChunkBuffer.Count >= minChunkSize * 2)
                            {
                                var networkChunk = networkChunkBuffer.ToArray();
                                networkChunkBuffer.Clear();

                                await UniTask.SwitchToMainThread();
                                SendAudioChunkToClients(networkChunk, currentSequenceNumber++);
                                await UniTask.SwitchToThreadPool();
                            }
                        }

                        // Local playback: 4KB chunks
                        while (buffer.Count >= minChunkSize)
                        {
                            var chunkData = buffer.GetRange(0, minChunkSize).ToArray();
                            buffer.RemoveRange(0, minChunkSize);
                            var samples = ConvertPCMToFloatOptimized(chunkData);
                            audioChunkQueue.Enqueue(samples);
                        }

                        if (streamCompleted && handler.IsComplete && availableData.Length == 0)
                        {
                            break;
                        }

                        await UniTask.Delay(3, cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Processing error: {ex.Message}");
                        break;
                    }
                }

                // Send final chunks
                if (buffer.Count > 0)
                {
                    var samples = ConvertPCMToFloatOptimized(buffer.ToArray());
                    audioChunkQueue.Enqueue(samples);
                }

                if (networkChunkBuffer.Count > 0)
                {
                    await UniTask.SwitchToMainThread();
                    SendAudioChunkToClients(networkChunkBuffer.ToArray(), currentSequenceNumber++);
                }

            }, cancellationToken: cancellationToken);

            isProcessingAudio = false;



            /* isProcessingAudio = true;
             var processedBytes = 0;
             var totalBytesProcessed = 0;

             await UniTask.RunOnThreadPool(async () =>
             {
                 var buffer = new List<byte>();
                 var networkChunkBuffer = new List<byte>();

                 while (!cancellationToken.IsCancellationRequested)
                 {
                     try
                     {
                         var availableData = handler.GetAvailableData(processedBytes);

                         if (availableData.Length > 0)
                         {
                             buffer.AddRange(availableData);
                             networkChunkBuffer.AddRange(availableData);
                             processedBytes += availableData.Length;
                             totalBytesProcessed += availableData.Length;

                             if (networkChunkBuffer.Count >= minChunkSize)
                             {
                                 var networkChunk = networkChunkBuffer.ToArray();
                                 networkChunkBuffer.Clear();

                                 await UniTask.SwitchToMainThread();
                                 SendAudioChunkToClients(networkChunk);
                                 await UniTask.SwitchToThreadPool();
                             }
                         }

                         // Process complete chunks
                         while (buffer.Count >= minChunkSize)
                         {
                             var chunkData = buffer.GetRange(0, minChunkSize).ToArray();
                             buffer.RemoveRange(0, minChunkSize);

                             var samples = ConvertPCMToFloatOptimized(chunkData);
                             audioChunkQueue.Enqueue(samples);
                             Debug.Log($"Queued audio chunk: {samples.Length} samples");
                         }

                         // Check if stream is complete and no more data
                         if (streamCompleted && handler.IsComplete && availableData.Length == 0)
                         {
                             Debug.Log("Stream completed and no more data available");
                             break;
                         }

                         await UniTask.Delay(3, cancellationToken: cancellationToken);
                     }
                     catch (Exception ex)
                     {
                         Debug.LogError($"Background audio processing error: {ex.Message}");
                         break;
                     }
                 }

                 // CRITICAL: Process ALL remaining data
                 if (buffer.Count > 0)
                 {
                     Debug.Log($"Processing final buffer: {buffer.Count} bytes");
                     var samples = ConvertPCMToFloatOptimized(buffer.ToArray());
                     audioChunkQueue.Enqueue(samples);
                     Debug.Log($"Queued final audio chunk: {samples.Length} samples");
                 }

                 if (networkChunkBuffer.Count > 0)
                 {
                     await UniTask.SwitchToMainThread();
                     SendAudioChunkToClients(networkChunkBuffer.ToArray());
                 }

                 Debug.Log($"Background processing finished. Total bytes processed: {totalBytesProcessed}");

             }, cancellationToken: cancellationToken);

             isProcessingAudio = false;*/




            /*isProcessingAudio = true;
            var processedBytes = 0;

            // Run on background thread
            await UniTask.RunOnThreadPool(async () =>
            {
                var buffer = new List<byte>();
                var networkChunkBuffer = new List<byte>();

                while (!cancellationToken.IsCancellationRequested && (!handler.IsComplete || buffer.Count > 0))
                {
                    try
                    {
                        // Get new data
                        var availableData = handler.GetAvailableData(processedBytes);

                        if (availableData.Length > 0)
                        {
                            buffer.AddRange(availableData);
                            networkChunkBuffer.AddRange(availableData);
                            processedBytes += availableData.Length;

                            // Send network chunks when we have enough data
                            if (networkChunkBuffer.Count >= minChunkSize)
                            {
                                var networkChunk = networkChunkBuffer.ToArray();
                                networkChunkBuffer.Clear();

                                // Switch to main thread for network operations
                                await UniTask.SwitchToMainThread();
                                SendAudioChunkToClients(networkChunk);
                                await UniTask.SwitchToThreadPool();
                            }
                        }

                        // Process complete chunks for local playback
                        while (buffer.Count >= minChunkSize)
                        {
                            var chunkData = buffer.GetRange(0, minChunkSize).ToArray();
                            buffer.RemoveRange(0, minChunkSize);

                            // Convert PCM to float on background thread
                            var samples = ConvertPCMToFloatOptimized(chunkData);

                            // Queue for main thread playback
                            audioChunkQueue.Enqueue(samples);
                        }

                        // Small delay to prevent busy waiting
                        await UniTask.Delay(5, cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Background audio processing error: {ex.Message}");
                        break;
                    }
                }

                // Process remaining data
                if (buffer.Count > 0)
                {
                    var samples = ConvertPCMToFloatOptimized(buffer.ToArray());
                    audioChunkQueue.Enqueue(samples);
                }

                // Send remaining network data
                if (networkChunkBuffer.Count > 0)
                {
                    await UniTask.SwitchToMainThread();
                    SendAudioChunkToClients(networkChunkBuffer.ToArray());
                }

            }, cancellationToken: cancellationToken);

            isProcessingAudio = false;*/
        }

        private async UniTask WaitForAllAudioToFinish()
        {
            Debug.Log("Starting comprehensive audio completion wait...");

            // Wait for processing to finish
            while (isProcessingAudio)
            {
                Debug.Log("Still processing audio...");
                await UniTask.Delay(50);
            }

            // Wait for queue to empty
            while (!audioChunkQueue.IsEmpty)
            {
                Debug.Log($"Still have {audioChunkQueue.Count} chunks in audio queue");
                await UniTask.Delay(50);
            }

            // Wait for playback queue to empty
            while (playbackQueue.Count > 0)
            {
                Debug.Log($"Still have {playbackQueue.Count} chunks in playback queue");
                await UniTask.Delay(50);
            }

            // Wait for current audio to finish
            while (selfvoicesource.isPlaying)
            {
                var remaining = selfvoicesource.clip.length - selfvoicesource.time;
                Debug.Log($"Current audio still playing: {remaining:F2}s remaining");
                await UniTask.Delay(100);
            }

            // Extra safety buffer
            Debug.Log("Adding final safety buffer...");
            await UniTask.Delay(500);

            Debug.Log("All audio completion confirmed!");
        }



        // Optimized PCM conversion (background thread safe)
        private float[] ConvertPCMToFloatOptimized(byte[] pcmData)
        {
            int sampleCount = pcmData.Length / 2;
            float[] samples = new float[sampleCount];

            // Optimized conversion without unsafe code (Unity compatible)
            for (int i = 0; i < sampleCount; i++)
            {
                short pcmSample = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
                samples[i] = pcmSample / 32768f;
            }

            return samples;
        }

        // Main thread audio playback loop
        private async UniTask StartAudioPlaybackLoop(CancellationToken cancellationToken)
        {
            var playbackBuffer = new List<float>();
            bool hasStartedPlaying = false;
            var bufferStartTime = Time.time;

            while (!cancellationToken.IsCancellationRequested &&
                   (isProcessingAudio || !audioChunkQueue.IsEmpty || playbackBuffer.Count > 0))
            {
                // Collect audio chunks
                while (audioChunkQueue.TryDequeue(out var chunk))
                {
                    playbackBuffer.AddRange(chunk);
                }

                bool shouldStartPlaying = !hasStartedPlaying &&
                                        (playbackBuffer.Count >= targetBufferSize * 3 ||
                                         Time.time - bufferStartTime >= bufferTime * 2);

                if (shouldStartPlaying && playbackBuffer.Count > 0)
                {
                    hasStartedPlaying = true;
                    StartStreamingPlayback();
                }

                if (hasStartedPlaying)
                {
                    if (isProcessingAudio && playbackBuffer.Count >= targetBufferSize * 2)
                    {
                        // Normal playback
                        var samplesToPlay = playbackBuffer.GetRange(0, targetBufferSize).ToArray();
                        playbackBuffer.RemoveRange(0, targetBufferSize);
                        PlayAudioChunkOptimized(samplesToPlay);
                    }
                    else if (!isProcessingAudio && playbackBuffer.Count > 0)
                    {
                        // Stream finished - play ALL remaining audio
                        Debug.Log($"Stream finished, playing final {playbackBuffer.Count} samples");
                        var finalSamples = playbackBuffer.ToArray();
                        playbackBuffer.Clear();
                        PlayAudioChunkOptimized(finalSamples);
                    }
                    else if (isProcessingAudio && playbackBuffer.Count < targetBufferSize)
                    {
                        Debug.Log("Buffer underrun detected, waiting for more audio...");
                    }
                }

                await UniTask.Delay(10, cancellationToken: cancellationToken);
            }

            // Final cleanup
            if (playbackBuffer.Count > 0)
            {
                Debug.Log($"Final cleanup: playing remaining {playbackBuffer.Count} samples");
                PlayAudioChunkOptimized(playbackBuffer.ToArray());
            }

            // CRITICAL: Don't exit until all queued audio is actually playing
            Debug.Log("Playback loop finished, waiting for queue to empty...");
            while (playbackQueue.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                Debug.Log($"Still have {playbackQueue.Count} chunks in queue");
                await UniTask.Delay(50, cancellationToken: cancellationToken);
            }

            Debug.Log("Audio playback loop completed");



            /*var playbackBuffer = new List<float>();
            bool hasStartedPlaying = false;
            var bufferStartTime = Time.time;

            while (!cancellationToken.IsCancellationRequested && (isProcessingAudio || !audioChunkQueue.IsEmpty || playbackBuffer.Count > 0))
            {
                // Collect audio chunks from background thread
                while (audioChunkQueue.TryDequeue(out var chunk))
                {
                    playbackBuffer.AddRange(chunk);
                }

                // Start playing when we have enough buffered or timeout
                bool shouldStartPlaying = !hasStartedPlaying &&
                                        (playbackBuffer.Count >= targetBufferSize ||
                                         Time.time - bufferStartTime >= bufferTime);

                if (shouldStartPlaying && playbackBuffer.Count > 0)
                {
                    hasStartedPlaying = true;
                    StartStreamingPlayback();
                }

                // Play buffered audio in chunks
                if (hasStartedPlaying && playbackBuffer.Count >= targetBufferSize)
                {
                    var samplesToPlay = playbackBuffer.GetRange(0, targetBufferSize).ToArray();
                    playbackBuffer.RemoveRange(0, targetBufferSize);

                    PlayAudioChunkOptimized(samplesToPlay);
                }

                // Wait for next frame
                await UniTask.Yield();
            }

            // Play remaining audio
            if (playbackBuffer.Count > 0)
            {
                PlayAudioChunkOptimized(playbackBuffer.ToArray());
            }

            Debug.Log("Audio playback loop completed");*/
        }

        private void StartStreamingPlayback()
        {
            isPlaybackActive = true;

            // CRITICAL: Configure audio source for smooth playback
            selfvoicesource.priority = 0; // Highest priority
            selfvoicesource.volume = 1.0f;
            selfvoicesource.pitch = 1.0f;
            selfvoicesource.panStereo = 0f;
            selfvoicesource.spatialBlend = 0f; // 2D audio for consistency
            selfvoicesource.reverbZoneMix = 0f; // No reverb interference

            StartCoroutine(ManageAudioPlayback());
            Debug.Log("Started streaming audio playback");



            /* isPlaybackActive = true;
             StartCoroutine(ManageAudioPlayback());
             Debug.Log("Started streaming audio playback");*/
        }

        // Optimized audio playback with queue management
        private void PlayAudioChunkOptimized(float[] samples)
        {
            try
            {
                ApplyAudioSmoothing(samples);

                var clip = GetOrCreateStreamingClip(samples.Length);
                clip.SetData(samples, 0);

                playbackQueue.Enqueue(clip);

                Debug.Log($"Queued audio chunk: {samples.Length} samples, queue size: {playbackQueue.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create audio chunk: {ex.Message}");
            }



            /*try
            {
                // CRITICAL: Apply smoothing to prevent clicks/pops
                ApplyAudioSmoothing(samples);

                var clip = GetOrCreateStreamingClip(samples.Length);
                clip.SetData(samples, 0);

                playbackQueue.Enqueue(clip);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create audio chunk: {ex.Message}");
            }*/




            /*try
            {
                var clip = GetOrCreateStreamingClip(samples.Length);
                clip.SetData(samples, 0);

                // Add to playback queue
                playbackQueue.Enqueue(clip);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create audio chunk: {ex.Message}");
            }*/
        }

        private IEnumerator ManageAudioPlayback()
        {

            var checkInterval = new WaitForSeconds(0.01f);

            while (isPlaybackActive || playbackQueue.Count > 0)
            {
                if (!selfvoicesource.isPlaying && playbackQueue.Count > 0)
                {
                    var nextClip = playbackQueue.Dequeue();

                    Debug.Log($"Playing audio chunk, {playbackQueue.Count} remaining in queue");

                    selfvoicesource.Stop();
                    selfvoicesource.clip = nextClip;
                    selfvoicesource.Play();

                    StartCoroutine(ReturnClipAfterPlaying(nextClip));
                }

                yield return checkInterval;
            }

            Debug.Log("Audio playback management completed - all chunks played");
            isPlaybackActive = false;

            /*var checkInterval = new WaitForSeconds(0.01f);

            while (isPlaybackActive || playbackQueue.Count > 0)
            {
                if (!selfvoicesource.isPlaying && playbackQueue.Count > 0)
                {
                    var nextClip = playbackQueue.Dequeue();

                    selfvoicesource.Stop();
                    selfvoicesource.clip = nextClip;
                    selfvoicesource.Play();

                    StartCoroutine(ReturnClipAfterPlaying(nextClip));
                }

                yield return checkInterval;
            }

            // ADDITIONAL: Wait a bit more to ensure last clip finishes
            yield return new WaitForSeconds(0.5f);

            Debug.Log("Audio playback management completed");
            isPlaybackActive = false;
    */






            /*while (isPlaybackActive || playbackQueue.Count > 0)
            {
                if (!selfvoicesource.isPlaying && playbackQueue.Count > 0)
                {
                    var nextClip = playbackQueue.Dequeue();
                    selfvoicesource.clip = nextClip;
                    selfvoicesource.Play();

                    // Schedule clip return to pool
                    StartCoroutine(ReturnClipAfterPlaying(nextClip));
                }

                yield return new WaitForSeconds(0.05f); // Check every 50ms
            }

            isPlaybackActive = false;*/
        }


        private void ApplyAudioSmoothing(float[] samples)
        {
            if (samples.Length < 64) return; // Skip very small chunks

            // Smooth the beginning and end of each chunk to prevent clicks
            int fadeLength = Math.Min(32, samples.Length / 4); // Fade first/last 32 samples

            // Fade in
            for (int i = 0; i < fadeLength; i++)
            {
                float fadeMultiplier = (float)i / fadeLength;
                samples[i] *= fadeMultiplier;
            }

            // Fade out
            for (int i = samples.Length - fadeLength; i < samples.Length; i++)
            {
                float fadeMultiplier = (float)(samples.Length - 1 - i) / fadeLength;
                samples[i] *= fadeMultiplier;
            }
        }



        private void ReturnClipToPool(AudioClip clip)
        {
            if (audioClipPool.Count < 20) // Limit pool size
            {
                audioClipPool.Enqueue(clip);
            }
            else
            {
                DestroyImmediate(clip);
            }
        }

        private AudioClip GetOrCreateStreamingClip(int sampleCount)
        {
            var targetSamples = sampleCount / streamingChannels;

            // Try to reuse existing clip with same size
            if (audioClipPool.Count > 0)
            {
                var pooledClip = audioClipPool.Dequeue();
                if (pooledClip.samples == targetSamples)
                {
                    return pooledClip;
                }
                DestroyImmediate(pooledClip);
            }

            return AudioClip.Create("StreamingChunk", targetSamples, streamingChannels, streamingSampleRate, false);
        }

        // Network streaming for multiplayer
        [ServerRpc]
        private void SendAudioChunkToClients(byte[] chunkData)
        {
            ReceiveAudioChunk(chunkData);
        }

        [ObserversRpc]
        private void ReceiveAudioChunk(byte[] chunkData)
        {
            if (!IsOwner)
            {
                try
                {
                    var samples = ConvertPCMToFloatOptimized(chunkData);

                    // Add to remote playback queue
                    var clip = GetOrCreateStreamingClip(samples.Length);
                    clip.SetData(samples, 0);

                    if (!selfvoicesource.isPlaying)
                    {
                        selfvoicesource.clip = clip;
                        selfvoicesource.Play();
                        StartCoroutine(ReturnClipAfterPlaying(clip));
                    }
                    else
                    {
                        // Queue for later playback
                        playbackQueue.Enqueue(clip);
                        if (!isPlaybackActive)
                        {
                            StartStreamingPlayback();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to play received audio chunk: {ex.Message}");
                }
            }
        }

        // Fallback to regular TTS if streaming fails
        private async UniTask SendRegularTTSRequest(string responseText, CancellationToken cancellationToken)
        {
            var postData = new TextToSpeechRequest
            {
                text = responseText,
                model_id = "eleven_turbo_v2_5",
                voice_settings = new VoiceSettings
                {
                    stability = 0.4f,
                    similarity_boost = 0.75f,
                    style = 0.0f,
                    use_speaker_boost = true
                }
            };

            var json = JsonConvert.SerializeObject(postData);

            // Use faster endpoint for short messages
            var url = $"{_apiUrl}/v1/text-to-speech/{ttvoice}" +
                      $"?output_format=pcm_16000" +
                      $"&optimize_streaming_latency=4"; // Max optimization for short messages

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("xi-api-key", _apiKey);
            request.SetRequestHeader("Accept", "audio/pcm");

            cancellationToken.ThrowIfCancellationRequested();

            // Fast network request
            await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Optimized TTS Error: {request.error}");
                return;
            }

            byte[] pcmData = request.downloadHandler.data;

            if (pcmData == null || pcmData.Length < 100)
            {
                Debug.LogError("Invalid audio data");
                return;
            }

            // Local playback
            var samples = await UniTask.RunOnThreadPool(() =>
                ConvertPCMToFloatOptimized(pcmData), cancellationToken: cancellationToken);

            var clip = GetOrCreateStreamingClip(samples.Length);
            clip.SetData(samples, 0);
            selfvoicesource.clip = clip;
            selfvoicesource.Play();

            // Network send - REMOVED textLength parameter
            SendCompleteAudioToClients(pcmData);

            StartCoroutine(ReturnClipAfterPlaying(clip));
            /* byte[] pcmData = request.downloadHandler.data;
             var samples = await UniTask.RunOnThreadPool(() =>
                 ConvertPCMToFloatOptimized(pcmData), cancellationToken: cancellationToken);

             // Play immediately
             var clip = GetOrCreateStreamingClip(samples.Length);
             clip.SetData(samples, 0);
             selfvoicesource.clip = clip;
             selfvoicesource.Play();

             // Send to network (compressed for chat)
             SendCompressedAudioToClients(pcmData);

             StartCoroutine(ReturnClipAfterPlaying(clip));*/


        }



        private void CleanupNetworkAudio()
        {
            remoteAudioBuffer.Clear();
            expectedSequenceNumber = 0;
            currentSequenceNumber = 0;
        }


        [ServerRpc(RequireOwnership = false)]
        private void SendAudioChunkToClients(byte[] chunkData, int sequenceNumber)
        {
            ReceiveAudioChunkReliable(chunkData, sequenceNumber);
        }

        [ObserversRpc(ExcludeOwner = true, BufferLast = false)]
        private void ReceiveAudioChunkReliable(byte[] chunkData, int sequenceNumber)
        {
            try
            {
                // No client ID needed - this runs on THIS avatar's instance on remote clients

                // Check sequence order
                if (sequenceNumber == expectedSequenceNumber)
                {
                    remoteAudioBuffer.Add(chunkData);
                    expectedSequenceNumber++;

                    // Start playback when buffered enough
                    if (remoteAudioBuffer.Count >= NETWORK_BUFFER_CHUNKS && !isPlaybackActive)
                    {
                        ProcessBufferedRemoteAudioAsync().Forget();
                    }
                }
                else if (sequenceNumber > expectedSequenceNumber)
                {
                    Debug.LogWarning($"Missed audio chunks: expected {expectedSequenceNumber}, got {sequenceNumber}");
                    // Reset and accept this chunk
                    expectedSequenceNumber = sequenceNumber + 1;
                    remoteAudioBuffer.Clear();
                    remoteAudioBuffer.Add(chunkData);
                }
                // Ignore old/duplicate chunks (sequenceNumber < expectedSequenceNumber)
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to receive audio chunk: {ex.Message}");
            }
        }

        private async UniTaskVoid ProcessBufferedRemoteAudioAsync()
        {
            isPlaybackActive = true;

            while (remoteAudioBuffer.Count > 0)
            {
                var chunkData = remoteAudioBuffer[0];
                remoteAudioBuffer.RemoveAt(0);

                // Convert on background thread
                var samples = await UniTask.RunOnThreadPool(() =>
                    ConvertPCMToFloatOptimized(chunkData));

                // Play on main thread
                var clip = GetOrCreateStreamingClip(samples.Length);
                clip.SetData(samples, 0);

                // Wait for previous audio to finish
                while (selfvoicesource.isPlaying)
                {
                    await UniTask.Delay(10);
                }

                selfvoicesource.clip = clip;
                selfvoicesource.Play();

                // Wait for playback
                await UniTask.Delay((int)(clip.length * 1000) + 50);
                ReturnClipToPool(clip);
            }

            isPlaybackActive = false;
        }

        // ✅ SHORT MESSAGES: Complete audio
        [ServerRpc(RequireOwnership = false)]
        private void SendCompleteAudioToClients(byte[] pcmData)
        {
            if (pcmData == null || pcmData.Length == 0)
            {
                Debug.LogError("Attempted to send null/empty audio");
                return;
            }

            Debug.Log($"Sending {pcmData.Length} bytes to clients");
            ReceiveCompleteAudio(pcmData);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveCompleteAudio(byte[] pcmData)
        {
            if (pcmData == null || pcmData.Length == 0)
            {
                Debug.LogError("Received null/empty audio");
                return;
            }

            Debug.Log($"Received {pcmData.Length} bytes");
            ProcessRemoteCompleteAudio(pcmData).Forget();
        }

        private async UniTaskVoid ProcessRemoteCompleteAudio(byte[] pcmData)
        {
            try
            {
                var samples = await UniTask.RunOnThreadPool(() =>
                    ConvertPCMToFloatOptimized(pcmData));

                // Wait for current playback
                while (selfvoicesource.isPlaying)
                {
                    await UniTask.Delay(50);
                }

                var clip = GetOrCreateStreamingClip(samples.Length);
                clip.SetData(samples, 0);

                selfvoicesource.clip = clip;
                selfvoicesource.Play();

                StartCoroutine(ReturnClipAfterPlaying(clip));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Remote audio failed: {ex.Message}");
            }
        }





        [ServerRpc]
        private void SendCompressedAudioToClients(byte[] pcmData)
        {
            // Simple compression for short audio
            var compressed = CompressAudioSimple(pcmData);
            ReceiveCompressedAudio(compressed);
        }

        private byte[] CompressAudioSimple(byte[] pcmData)
        {
            // For short chat messages, simple compression is enough
            using var output = new MemoryStream();
            using var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest);
            gzip.Write(pcmData, 0, pcmData.Length);
            return output.ToArray();
        }

        [ObserversRpc]
        private void ReceiveCompressedAudio(byte[] compressedData)
        {
            if (!IsOwner)
            {
                ProcessCompressedRemoteAudio(compressedData).Forget();
            }
        }

        private async UniTaskVoid ProcessCompressedRemoteAudio(byte[] compressedData)
        {
            try
            {
                // Decompress on background thread
                var pcmData = await UniTask.RunOnThreadPool(() => DecompressAudio(compressedData));
                var samples = await UniTask.RunOnThreadPool(() => ConvertPCMToFloatOptimized(pcmData));

                // Play on main thread
                var clip = GetOrCreateStreamingClip(samples.Length);
                clip.SetData(samples, 0);
                selfvoicesource.clip = clip;
                selfvoicesource.Play();

                StartCoroutine(ReturnClipAfterPlaying(clip));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Remote compressed audio failed: {ex.Message}");
            }
        }

        private byte[] DecompressAudio(byte[] compressedData)
        {
            using var input = new MemoryStream(compressedData);
            using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }




        // Audio clip pooling and management
        private void ReturnAudioClipToPool(AudioClip clip)
        {
            if (audioClipPool.Count < MAX_AUDIO_POOL_SIZE && clip != null)
            {
                audioClipPool.Enqueue(clip);
            }
            else if (clip != null)
            {
                DestroyImmediate(clip);
            }
        }

        private IEnumerator ReturnClipAfterPlaying(AudioClip clip)
        {
            if (clip != null)
            {
                yield return new WaitForSeconds(clip.length + 0.5f);
                ReturnAudioClipToPool(clip);
            }
        }

        // Utility methods
        private void ClearAudioBuffers()
        {
            // Clear streaming buffers
            while (audioChunkQueue.TryDequeue(out _)) { }

            // Clear playback queue
            while (playbackQueue.Count > 0)
            {
                var clip = playbackQueue.Dequeue();
                ReturnAudioClipToPool(clip);
            }

            isPlaybackActive = false;
        }

        // Public control methods


        public void SetLatencyOptimization(int level)
        {
            latencyOptimization = Mathf.Clamp(level, 0, 4);
            Debug.Log($"Latency optimization set to {latencyOptimization}");
        }

        public void SetBufferTime(float time)
        {
            bufferTime = Mathf.Clamp(time, 0.1f, 2f);
            Debug.Log($"Buffer time set to {bufferTime}s");
        }

        // Performance monitoring
        public string GetStreamingStats()
        {
            return $"Processing: {isProcessingAudio}, " +
                   $"Queue: {audioChunkQueue.Count}, " +
                   $"Playback: {playbackQueue.Count}, " +
                   $"Pool: {audioClipPool.Count}/{MAX_AUDIO_POOL_SIZE}";
        }

        // Cleanup methods
        private void CleanupTTSStreaming()
        {
            // Cancel ongoing operations
            ttsTokenSource?.Cancel();
            ttsTokenSource?.Dispose();

            // Stop processing
            isProcessingAudio = false;
            isPlaybackActive = false;

            // Clear buffers
            ClearAudioBuffers();

            // Clean up audio clip pool
            while (audioClipPool.Count > 0)
            {
                var clip = audioClipPool.Dequeue();
                if (clip != null)
                    DestroyImmediate(clip);
            }

            Debug.Log("TTS streaming cleanup completed");
        }





        public void ClearConversionCache()
        {
            conversionCache.Clear();
            Debug.Log("Conversion cache cleared");
        }

        public string GetCacheStats()
        {
            return $"Conversion cache: {conversionCache.Count}/{MAX_CACHE_SIZE} entries";
        }















        void OnDestroy()
        {
            conversionTokenSource?.Cancel();
            conversionTokenSource?.Dispose();
            CleanupTTSStreaming();
            CleanupNetworkAudio();
            StopAllCoroutines();
            // Add your existing cleanup code here
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // Pause streaming when app goes to background
                ttsTokenSource?.Cancel();
                ClearConversionCache();

            }
        }


        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                // Cancel streaming when app loses focus
                ttsTokenSource?.Cancel();
            }
        }
































    }


    [Serializable]
    public class TextToSpeechRequest
    {
        public string text;
        public string model_id; // eleven_monolingual_v1
        public VoiceSettings voice_settings;
    }

    [Serializable]
    public class VoiceSettings
    {
        public float stability; // 0
        public float similarity_boost; // 0
        public float style; // 0.5
        public bool use_speaker_boost; // true
    }





    public class OpenAIResponse
    {
        public Choice[] choices { get; set; }
    }

    public class Choice
    {
        public Message message { get; set; }
    }

    public class Message
    {
        public string role { get; set; }
        public string content { get; set; }
    }



}