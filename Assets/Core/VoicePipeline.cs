using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet.Object;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;
/*
namespace Mikk.Avatar
{
    public class VoicePipeline : NetworkBehaviour
    {
        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioMixerGroup mixerGroup;
        [SerializeField] private AudioMixer mixer;

        [Header("Sarvam TTS")]
        [SerializeField] private string sarvamApiKey;
        [SerializeField] private string sarvamApiUrl = "https://api.sarvam.ai/text-to-speech";
        [SerializeField] private string sarvamLanguage = "hi-IN";
        [SerializeField] private string sarvamSpeaker = "arvind";
        [SerializeField] private string sarvamModel = "bulbul:v2";

        // Baseline values — VAD offsets these at runtime
        [SerializeField, Range(0.5f, 2f)] private float basePitch = 1.0f;
        [SerializeField, Range(0.5f, 2f)] private float basePace = 1.0f;
        [SerializeField, Range(0.5f, 3f)] private float baseLoudness = 1.5f;

        [Header("Hinglish")]
        [SerializeField] private string openAIKey;
        [SerializeField] private string openAIUrl = "https://api.openai.com/v1/chat/completions";
        [SerializeField] private float conversionTimeout = 8f;

        [Header("Playback")]
        [SerializeField, Range(0.1f, 2f)] private float bufferTime = 0.3f;
        [SerializeField] private int shortThreshold = 50;
        [SerializeField] private int sarvamMaxChars = 500;

        // State
        public bool IsPlaying => _isPlaying;
        private volatile bool _isPlaying;
        private volatile bool _isProcessing;

        // Audio queue
        private ConcurrentQueue<float[]> _chunkQueue = new ConcurrentQueue<float[]>();
        private Queue<AudioClip> _playQueue = new Queue<AudioClip>();
        private Queue<AudioClip> _clipPool = new Queue<AudioClip>();

        private const int CHANNELS = 1;
        private const int MAX_POOL = 8;
        private int _currentSampleRate = 22050;

        // Network buffering — same pattern as ElevenLabs version
        private CancellationTokenSource _cts;
        private int _sendSeq;
        private int _recvSeq;
        private List<byte[]> _remoteBuf = new List<byte[]>();
        private const int NET_BUF_THRESHOLD = 3;

        private Dictionary<string, string> _hinglishCache =
            new Dictionary<string, string>(200);
        private static HttpClient _http;

        private static readonly Dictionary<string, string> HindiDirect =
            new Dictionary<string, string>
        {
            {"h", "हाँ"}, {"k", "ओके"}, {"ok", "ओके"}, {"okay", "ओके"},
            {"haan", "हाँ"}, {"han", "हाँ"}, {"nahi", "नहीं"}, {"nhi", "नहीं"},
            {"kya", "क्या?"}, {"hai", "है"}, {"ho", "हो"},
            {"achha", "अच्छा!"}, {"acha", "अच्छा!"}, {"theek", "ठीक"},
            {"bas", "बस"}, {"chalo", "चलो"}, {"bhai", "भाई"}, {"yaar", "यार"},
            {"kab", "कब?"}, {"kahan", "कहाँ?"}, {"kaise", "कैसे?"},
            {"kyun", "क्यों?"}, {"main", "मैं"}, {"tum", "तुम"},
            {"theek hai", "ठीक है"}, {"kya hai", "क्या है?"},
        };

        private void Awake()
        {
            if (_http == null)
            {
                _http = new HttpClient();
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAIKey}");
                _http.Timeout = TimeSpan.FromSeconds(30);
            }
        }

        private void Start()
        {
            if (audioSource != null && mixerGroup != null)
                audioSource.outputAudioMixerGroup = mixerGroup;
        }

        // ══════════════════════════════════════════════
        // PUBLIC API — called by AvatarChatManager
        // ══════════════════════════════════════════════

        /// <summary>
        /// Phase 2 (runs parallel with VAD in AvatarChatManager):
        /// Only does Hinglish → Hindi conversion. No TTS yet.
        /// Returns converted text so AvatarChatManager can hold it.
        /// </summary>
        public async UniTask<string> ConvertOnlyAsync(string text, CancellationToken ct)
        {
            try
            {
                return await ConvertAndEnrichAsync(text, EmotionVAD.Neutral, ct);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Voice] ConvertOnly failed: {ex.Message}");
                return text;
            }
        }

        /// <summary>
        /// Phase 3 (called after both VAD + conversion are done):
        /// Takes pre-converted text + real VAD, fires TTS with emotion-driven params.
        /// </summary>
        public async UniTask SpeakAsync(
            string convertedText, EmotionVAD vad, CancellationToken ct)
        {
            StopCurrent();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;

            try
            {
                _isPlaying = true;

                string enriched = ApplyVADPunctuation(convertedText, vad);
                var sarvamParams = BuildSarvamParams(vad);

                Debug.Log($"[Voice] Speaking: \"{enriched}\" | " +
                          $"pitch={sarvamParams.pitch:F2} pace={sarvamParams.pace:F2} " +
                          $"loudness={sarvamParams.loudness:F2} | {DescribeEmotion(vad)}");

                if (IsShort(enriched))
                    await PlaySarvamTTS(enriched, sarvamParams, token);
                else
                    await PlaySarvamChunked(enriched, sarvamParams, token);

                await WaitPlaybackDone(token);
            }
            finally
            {
                _isPlaying = false;
            }
        }

        public void StopCurrent()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            StopAllCoroutines();
            audioSource?.Stop();
            ClearBuffers();
            _isPlaying = false;
            _isProcessing = false;
        }

        public void Mute() => mixer?.SetFloat("Volume", -80f);
        public void Unmute() => mixer?.SetFloat("Volume", 0f);

        // ══════════════════════════════════════════════
        // VAD → SARVAM PARAMS
        // ══════════════════════════════════════════════

        private struct SarvamVoiceParams
        {
            public float pitch;
            public float pace;
            public float loudness;
        }

        /// <summary>
        /// Arousal  → pace + pitch  (high energy = faster, higher pitch)
        /// Valence  → pitch offset  (positive = slightly brighter)
        /// Dominance → loudness     (assertive = louder)
        /// Pitch clamped to 0.5–1.0 (Sarvam hard limit)
        /// </summary>
        private SarvamVoiceParams BuildSarvamParams(EmotionVAD vad)
        {
            // Arousal -1→+1 maps pace 0.85→1.30
            float pace = Mathf.Lerp(0.85f, 1.30f,
                Mathf.InverseLerp(-1f, 1f, vad.Arousal));

            // Arousal + Valence → pitch (both kept small so sum stays ≤ 1.0)
            float pitchArousal = Mathf.Lerp(-0.10f, 0.08f,
                Mathf.InverseLerp(-1f, 1f, vad.Arousal));
            float pitchValence = Mathf.Lerp(-0.04f, 0.04f,
                Mathf.InverseLerp(-1f, 1f, vad.Valence));

            // Clamp to Sarvam's hard limit: 0.5–1.0
            float pitch = Mathf.Clamp(basePitch + pitchArousal + pitchValence, 0.5f, 1.0f);

            // Dominance -1→+1 maps loudness 1.1→2.0
            float loudness = Mathf.Lerp(1.1f, 2.0f,
                Mathf.InverseLerp(-1f, 1f, vad.Dominance));
            loudness = Mathf.Clamp(loudness * (baseLoudness / 1.5f), 0.5f, 3.0f);

            return new SarvamVoiceParams
            {
                pitch = pitch,
                pace = pace,
                loudness = loudness
            };
        }

        // ══════════════════════════════════════════════
        // TEXT CONVERSION + ENRICHMENT
        // ══════════════════════════════════════════════

        private async UniTask<string> ConvertAndEnrichAsync(
            string text, EmotionVAD vad, CancellationToken ct)
        {
            string key = $"{text.ToLower().Trim()}|{vad.Arousal:F1}|{vad.Valence:F1}";

            if (_hinglishCache.TryGetValue(key, out var cached))
                return cached;

            string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 2 &&
                HindiDirect.TryGetValue(text.ToLower().Trim(), out var direct))
            {
                string enrichedDirect = ApplyVADPunctuation(direct, vad);
                CacheHinglish(key, enrichedDirect);
                return enrichedDirect;
            }

            if (IsPureEnglish(text))
            {
                string enrichedEn = ApplyVADPunctuation(text, vad);
                CacheHinglish(key, enrichedEn);
                return enrichedEn;
            }

            try
            {
                var result = await ConvertWithGPT(text, vad, ct);
                Debug.Log($"[Voice] GPT: \"{text}\" → \"{result}\"");
                CacheHinglish(key, result);
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Voice] GPT failed: {ex.Message}");
                return ApplyVADPunctuation(text, vad);
            }
        }

        private async UniTask<string> ConvertWithGPT(
            string input, EmotionVAD vad, CancellationToken ct)
        {
            string emotionHint = DescribeEmotion(vad);

            string systemPrompt =
                "You are preparing text for a TTS voice engine (Indian avatar app). " +
                "Do ALL of the following in one step:\n\n" +

                "1. CONVERT: Hinglish/Roman Hindi → Hindi Devanagari script. " +
                "Pure English words (names, tech terms) → keep as-is in English.\n\n" +

                "2. PUNCTUATE for prosody:\n" +
                "   • Add । or . to mark sentence ends\n" +
                "   • Add ? for questions\n" +
                "   • Add ! for exclamations\n" +
                "   • Add , for natural breath pauses\n" +
                "   • Use ... for trailing off / hesitation\n\n" +

                "3. ENRICH for expressiveness based on the speaker's emotional state:\n" +
                $"   • Current emotion: {emotionHint}\n" +
                "   • Excited/happy → emphasis punctuation, keep energy in phrasing\n" +
                "   • Sad/low energy → softer phrasing, trailing ... where natural\n" +
                "   • Angry/assertive → firm punctuation, no softening\n" +
                "   • Uncertain/confused → add hmm... or अच्छा... prefix if natural\n" +
                "   • Do NOT add words that change meaning. Only adjust punctuation " +
                "and minor phrasing a real speaker would naturally use.\n\n" +
                "OUTPUT: Return ONLY the final Hindi text. No explanation, no quotes.";

            var body = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = input }
                },
                temperature = 0.25,
                max_tokens = 500
            };

            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(openAIUrl, content, ct)
                .AsUniTask()
                .Timeout(TimeSpan.FromSeconds(conversionTimeout));

            if (!response.IsSuccessStatusCode)
                throw new Exception($"HTTP {response.StatusCode}");

            var responseBody = await response.Content.ReadAsStringAsync();
            var parsed = JsonConvert.DeserializeObject<GPTResponse>(responseBody);
            return parsed.choices[0].message.content?.Trim() ?? input;
        }

        private string ApplyVADPunctuation(string text, EmotionVAD vad)
        {
            string t = text.Trim();
            bool hasEnd = t.EndsWith(".") || t.EndsWith("!") || t.EndsWith("?") ||
                          t.EndsWith("...") || t.EndsWith("।");

            if (vad.Arousal > 0.5f && !hasEnd) t += "!";
            else if (vad.Valence < -0.3f && vad.Arousal < 0f && !hasEnd) t += "...";
            else if (!hasEnd) t += ".";

            if (vad.Dominance < -0.25f && Mathf.Abs(vad.Valence) < 0.3f &&
                !t.StartsWith("hmm") && t.Split(' ').Length > 2)
                t = "hmm... " + t;

            return t;
        }

        private string DescribeEmotion(EmotionVAD vad)
        {
            string label;
            if (vad.Valence > 0.5f && vad.Arousal > 0.4f) label = "excited and happy";
            else if (vad.Valence > 0.3f && vad.Arousal > 0.1f) label = "cheerful and warm";
            else if (vad.Valence > 0.1f) label = "casual and friendly";
            else if (vad.Valence < -0.5f && vad.Arousal < -0.1f) label = "sad and tired";
            else if (vad.Valence < -0.3f && vad.Dominance > 0.2f) label = "frustrated and assertive";
            else if (vad.Valence < -0.2f && vad.Arousal > 0.3f) label = "angry or upset";
            else if (vad.Valence < -0.1f && vad.Dominance < -0.2f) label = "apologetic or uncertain";
            else if (vad.Dominance < -0.3f && vad.Arousal > 0.1f) label = "confused or questioning";
            else if (vad.Arousal > 0.5f) label = "high energy";
            else if (vad.Arousal < -0.3f) label = "calm and low energy";
            else label = "neutral and casual";

            string intensity = vad.Magnitude > 0.7f ? "very " :
                               vad.Magnitude > 0.4f ? "" : "slightly ";

            return $"{intensity}{label} " +
                   $"(V:{vad.Valence:+0.0;-0.0} A:{vad.Arousal:+0.0;-0.0} D:{vad.Dominance:+0.0;-0.0})";
        }

        private bool IsPureEnglish(string text) =>
            text.Length > 10 &&
            text.All(c => c < 128 || char.IsWhiteSpace(c) || char.IsPunctuation(c)) &&
            text.Split(' ').Length >= 3;

        private void CacheHinglish(string key, string val)
        {
            if (_hinglishCache.Count >= 200)
                _hinglishCache.Remove(_hinglishCache.Keys.First());
            _hinglishCache[key] = val;
        }

        // ══════════════════════════════════════════════
        // SARVAM TTS — SHORT
        // ══════════════════════════════════════════════

        private async UniTask PlaySarvamTTS(
            string text, SarvamVoiceParams p, CancellationToken ct)
        {
            byte[] wavBytes = await FetchSarvamAudio(text, p, ct);
            if (wavBytes == null || wavBytes.Length < 100) return;

            var (samples, sampleRate) = await UniTask.RunOnThreadPool(
                () => DecodeWav(wavBytes), cancellationToken: ct);
            if (samples == null || samples.Length == 0) return;

            ct.ThrowIfCancellationRequested();
            _currentSampleRate = sampleRate;

            var clip = AudioClip.Create("SarvamTTS", samples.Length, CHANNELS, sampleRate, false);
            clip.SetData(samples, 0);

            ConfigureAudioSource();
            audioSource.clip = clip;
            audioSource.Play();

            // Send raw WAV to remote clients — same pattern as ElevenLabs PCM
            SendComplete_ServerRpc(wavBytes);
            StartCoroutine(ReturnClip(clip));
        }

        // ══════════════════════════════════════════════
        // SARVAM TTS — CHUNKED (long text)
        // ══════════════════════════════════════════════

        private async UniTask PlaySarvamChunked(
            string text, SarvamVoiceParams p, CancellationToken ct)
        {
            _isProcessing = true;
            _sendSeq = 0;
            ClearBuffers();

            var chunks = SplitTextIntoChunks(text, sarvamMaxChars);
            var fetchTasks = chunks.Select(c => FetchSarvamAudio(c, p, ct)).ToList();

            Debug.Log($"[Voice] Chunked: {chunks.Count} segments");
            ConfigureAudioSource();
            StartCoroutine(PlaybackCoroutine());

            for (int i = 0; i < fetchTasks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                byte[] wavBytes = await fetchTasks[i];
                if (wavBytes == null || wavBytes.Length < 100) continue;

                var (samples, sampleRate) = await UniTask.RunOnThreadPool(
                    () => DecodeWav(wavBytes), cancellationToken: ct);
                if (samples == null || samples.Length == 0) continue;

                _currentSampleRate = sampleRate;
                Enqueue(samples);

                // Send each chunk to remote clients in sequence
                SendChunk_ServerRpc(wavBytes, _sendSeq++);
            }

            _isProcessing = false;
        }

        // ══════════════════════════════════════════════
        // SARVAM API CALL
        // ══════════════════════════════════════════════

        private async UniTask<byte[]> FetchSarvamAudio(
            string text, SarvamVoiceParams p, CancellationToken ct)
        {
            var payload = new SarvamTTSRequest
            {
                inputs = new[] { text },
                target_language_code = sarvamLanguage,
                speaker = sarvamSpeaker,
                model = sarvamModel,
                pitch = p.pitch,
                pace = p.pace,
                loudness = p.loudness,
                enable_preprocessing = true
            };

            var json = JsonConvert.SerializeObject(payload);

            using var req = new UnityWebRequest(sarvamApiUrl, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("api-subscription-key", sarvamApiKey);

            await req.SendWebRequest().ToUniTask(cancellationToken: ct);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Voice] Sarvam error: {req.error}\n{req.downloadHandler?.text}");
                return null;
            }

            var response = JsonConvert.DeserializeObject<SarvamTTSResponse>(
                req.downloadHandler.text);

            if (response?.audios == null || response.audios.Length == 0)
            {
                Debug.LogError("[Voice] Sarvam: no audio returned");
                return null;
            }

            try { return Convert.FromBase64String(response.audios[0]); }
            catch (Exception ex)
            {
                Debug.LogError($"[Voice] Base64 decode: {ex.Message}");
                return null;
            }
        }

        // ══════════════════════════════════════════════
        // AUDIO UTILITIES
        // ══════════════════════════════════════════════

        private void Enqueue(float[] samples)
        {
            Smooth(samples);
            var clip = MakeClip(samples.Length, _currentSampleRate);
            clip.SetData(samples, 0);
            _playQueue.Enqueue(clip);
        }

        private IEnumerator PlaybackCoroutine()
        {
            var wait = new WaitForSeconds(0.01f);
            while (_isProcessing || _playQueue.Count > 0)
            {
                if (!audioSource.isPlaying && _playQueue.Count > 0)
                {
                    var clip = _playQueue.Dequeue();
                    audioSource.Stop();
                    audioSource.clip = clip;
                    audioSource.Play();
                    StartCoroutine(ReturnClip(clip));
                }
                yield return wait;
            }
        }

        private (float[] samples, int sampleRate) DecodeWav(byte[] wav)
        {
            if (wav == null || wav.Length < 44) return (null, 22050);
            try
            {
                if (Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" ||
                    Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
                    return (PCMToFloat(wav), 22050);

                int audioFormat = BitConverter.ToInt16(wav, 20);
                int channels = BitConverter.ToInt16(wav, 22);
                int sampleRate = BitConverter.ToInt32(wav, 24);
                int bitsPerSample = BitConverter.ToInt16(wav, 34);

                int dataOffset = 12, dataSize = 0;
                while (dataOffset < wav.Length - 8)
                {
                    string chunkId = Encoding.ASCII.GetString(wav, dataOffset, 4);
                    int chunkSize = BitConverter.ToInt32(wav, dataOffset + 4);
                    if (chunkId == "data") { dataOffset += 8; dataSize = chunkSize; break; }
                    dataOffset += 8 + chunkSize;
                }

                if (dataSize == 0 || dataOffset >= wav.Length) return (null, sampleRate);
                dataSize = Math.Min(dataSize, wav.Length - dataOffset);

                float[] samples;
                if (bitsPerSample == 16)
                {
                    int n = dataSize / 2; samples = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        int idx = dataOffset + i * 2;
                        if (idx + 1 >= wav.Length) break;
                        samples[i] = (short)(wav[idx] | (wav[idx + 1] << 8)) / 32768f;
                    }
                }
                else if (bitsPerSample == 8)
                {
                    samples = new float[dataSize];
                    for (int i = 0; i < dataSize; i++)
                        samples[i] = (wav[dataOffset + i] - 128) / 128f;
                }
                else if (bitsPerSample == 32 && audioFormat == 3)
                {
                    int n = dataSize / 4; samples = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        int idx = dataOffset + i * 4;
                        if (idx + 3 >= wav.Length) break;
                        samples[i] = BitConverter.ToSingle(wav, idx);
                    }
                }
                else return (null, sampleRate);

                if (channels == 2 && samples.Length > 1)
                {
                    int mn = samples.Length / 2; var mono = new float[mn];
                    for (int i = 0; i < mn; i++)
                        mono[i] = (samples[i * 2] + samples[i * 2 + 1]) * 0.5f;
                    samples = mono;
                }

                return (samples, sampleRate);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Voice] WAV decode: {ex.Message}");
                return (null, 22050);
            }
        }

        private static float[] PCMToFloat(byte[] pcm)
        {
            int n = pcm.Length / 2;
            float[] s = new float[n];
            for (int i = 0; i < n; i++)
                s[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
            return s;
        }

        private static void Smooth(float[] s)
        {
            if (s.Length < 64) return;
            int fade = Math.Min(32, s.Length / 4);
            for (int i = 0; i < fade; i++) s[i] *= (float)i / fade;
            for (int i = s.Length - fade; i < s.Length; i++)
                s[i] *= (float)(s.Length - 1 - i) / fade;
        }

        private AudioClip MakeClip(int samples, int sampleRate)
        {
            int target = samples / CHANNELS;
            if (_clipPool.Count > 0)
            {
                var p = _clipPool.Dequeue();
                if (p.samples == target && p.frequency == sampleRate) return p;
                DestroyImmediate(p);
            }
            return AudioClip.Create("SarvamTTS", target, CHANNELS, sampleRate, false);
        }

        private void ConfigureAudioSource()
        {
            audioSource.priority = 0;
            audioSource.volume = 1f;
            audioSource.pitch = 1f;
            audioSource.spatialBlend = 0f;
            audioSource.reverbZoneMix = 0f;
        }

        private IEnumerator ReturnClip(AudioClip clip)
        {
            yield return new WaitForSeconds(clip.length + 0.3f);
            if (_clipPool.Count < MAX_POOL) _clipPool.Enqueue(clip);
            else DestroyImmediate(clip);
        }

        private async UniTask WaitPlaybackDone(CancellationToken ct)
        {
            while (_playQueue.Count > 0 && !ct.IsCancellationRequested)
                await UniTask.Delay(50, cancellationToken: ct);
            while (audioSource.isPlaying && !ct.IsCancellationRequested)
                await UniTask.Delay(50, cancellationToken: ct);
            if (!ct.IsCancellationRequested)
                await UniTask.Delay(200, cancellationToken: ct);
        }

        private void ClearBuffers()
        {
            while (_chunkQueue.TryDequeue(out _)) { }
            while (_playQueue.Count > 0)
            {
                var c = _playQueue.Dequeue();
                if (c) DestroyImmediate(c);
            }
        }

        private bool IsShort(string t) =>
            t.Length <= shortThreshold || t.Split(' ').Length <= 8;

        // ══════════════════════════════════════════════
        // TEXT CHUNKING
        // ══════════════════════════════════════════════

        private List<string> SplitTextIntoChunks(string text, int maxChars)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;

            char[] sentenceEnders = { '.', '!', '?', '।', '॥' };
            var sentences = new List<string>();
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (Array.IndexOf(sentenceEnders, text[i]) >= 0)
                {
                    var sentence = text.Substring(start, i - start + 1).Trim();
                    if (!string.IsNullOrEmpty(sentence)) sentences.Add(sentence);
                    start = i + 1;
                }
            }

            if (start < text.Length)
            {
                var remaining = text.Substring(start).Trim();
                if (!string.IsNullOrEmpty(remaining)) sentences.Add(remaining);
            }

            var current = new StringBuilder();
            foreach (var sentence in sentences)
            {
                if (sentence.Length > maxChars)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString().Trim());
                        current.Clear();
                    }
                    result.AddRange(ForceSplitLong(sentence, maxChars));
                    continue;
                }

                if (current.Length + sentence.Length + 1 > maxChars)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                if (current.Length > 0) current.Append(" ");
                current.Append(sentence);
            }

            if (current.Length > 0) result.Add(current.ToString().Trim());
            return result;
        }

        private List<string> ForceSplitLong(string text, int maxChars)
        {
            var parts = new List<string>();
            foreach (var delim in new[] { ',', '،', ' ' })
            {
                if (!text.Contains(delim)) continue;
                var buf = new StringBuilder();
                foreach (var seg in text.Split(delim))
                {
                    if (buf.Length + seg.Length + 1 > maxChars && buf.Length > 0)
                    {
                        parts.Add(buf.ToString().Trim());
                        buf.Clear();
                    }
                    if (buf.Length > 0) buf.Append(delim);
                    buf.Append(seg);
                }
                if (buf.Length > 0) parts.Add(buf.ToString().Trim());
                return parts;
            }

            for (int i = 0; i < text.Length; i += maxChars)
                parts.Add(text.Substring(i, Math.Min(maxChars, text.Length - i)));
            return parts;
        }

        // ══════════════════════════════════════════════
        // NETWORK — same pattern as ElevenLabs version
        // ══════════════════════════════════════════════

        // Short audio: send full WAV bytes in one RPC (same as ElevenLabs PCM)
        [ServerRpc(RequireOwnership = false)]
        private void SendComplete_ServerRpc(byte[] wav) => ReceiveComplete_ObserversRpc(wav);

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveComplete_ObserversRpc(byte[] wav) => PlayRemote(wav).Forget();

        // Long audio: send WAV chunks in sequence (same pattern as ElevenLabs)
        [ServerRpc(RequireOwnership = false)]
        private void SendChunk_ServerRpc(byte[] chunk, int seq) =>
            ReceiveChunk_ObserversRpc(chunk, seq);

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveChunk_ObserversRpc(byte[] chunk, int seq)
        {
            if (seq == _recvSeq)
            {
                _remoteBuf.Add(chunk);
                _recvSeq++;
                if (_remoteBuf.Count >= NET_BUF_THRESHOLD) PlayRemoteBuffered().Forget();
            }
            else if (seq > _recvSeq)
            {
                _recvSeq = seq + 1;
                _remoteBuf.Clear();
                _remoteBuf.Add(chunk);
            }
        }

        private async UniTaskVoid PlayRemoteBuffered()
        {
            while (_remoteBuf.Count > 0)
            {
                var data = _remoteBuf[0];
                _remoteBuf.RemoveAt(0);

                var (samples, sampleRate) = await UniTask.RunOnThreadPool(
                    () => DecodeWav(data));
                if (samples == null) continue;

                var clip = MakeClip(samples.Length, sampleRate);
                clip.SetData(samples, 0);

                while (audioSource.isPlaying) await UniTask.Delay(10);
                audioSource.clip = clip;
                audioSource.Play();
                await UniTask.Delay((int)(clip.length * 1000) + 50);

                if (_clipPool.Count < MAX_POOL) _clipPool.Enqueue(clip);
                else DestroyImmediate(clip);
            }
        }

        private async UniTaskVoid PlayRemote(byte[] wav)
        {
            if (wav == null || wav.Length == 0) return;

            var (samples, sampleRate) = await UniTask.RunOnThreadPool(() => DecodeWav(wav));
            if (samples == null) return;

            while (audioSource.isPlaying) await UniTask.Delay(50);

            var clip = MakeClip(samples.Length, sampleRate);
            clip.SetData(samples, 0);
            audioSource.clip = clip;
            audioSource.Play();
            StartCoroutine(ReturnClip(clip));
        }

        // ══════════════════════════════════════════════
        // CLEANUP
        // ══════════════════════════════════════════════

        private void OnDestroy()
        {
            StopCurrent();
            while (_clipPool.Count > 0)
            {
                var c = _clipPool.Dequeue();
                if (c) DestroyImmediate(c);
            }
        }

        // ══════════════════════════════════════════════
        // DTOs
        // ══════════════════════════════════════════════

        [Serializable]
        private class SarvamTTSRequest
        {
            public string[] inputs;
            public string target_language_code;
            public string speaker;
            public string model;
            public float pitch;
            public float pace;
            public float loudness;
            public bool enable_preprocessing;
        }

        [Serializable] private class SarvamTTSResponse { public string[] audios; }
        [Serializable] private class GPTResponse { public GPTChoice[] choices; }
        [Serializable] private class GPTChoice { public GPTMsg message; }
        [Serializable] private class GPTMsg { public string content; }
    }
}*/




namespace Mikk.Avatar
{
    public class VoicePipeline : NetworkBehaviour  // ← CHANGED from MonoBehaviour
    {
        [Header("Audio")]
        [SerializeField] public AudioSource audioSource;
        [SerializeField] private AudioMixerGroup mixerGroup;
        [SerializeField] private AudioMixer mixer;

        [Header("Sarvam TTS")]
        [SerializeField] private string sarvamApiKey;
        [SerializeField] private string sarvamApiUrl = "https://api.sarvam.ai/text-to-speech";
        [SerializeField] private string sarvamLanguage = "hi-IN";
        [SerializeField] private string sarvamSpeaker = "arvind";
        [SerializeField] private string sarvamModel = "bulbul:v2";

        // Baseline values — VAD offsets these at runtime
        [SerializeField, Range(0.5f, 2f)] private float basePitch = 1.0f;
        [SerializeField, Range(0.5f, 2f)] private float basePace = 1.0f;
        [SerializeField, Range(0.5f, 3f)] private float baseLoudness = 1.5f;

        [Header("GPT")]
        [SerializeField] private string openAIKey;
        [SerializeField] private string openAIUrl = "https://api.openai.com/v1/chat/completions";
        [SerializeField] private float conversionTimeout = 8f;

        [Header("Playback")]
        [SerializeField, Range(0.1f, 2f)] private float bufferTime = 0.3f;
        [SerializeField] private int shortThreshold = 50;
        [SerializeField] private int sarvamMaxChars = 500;

        public bool IsPlaying => _isPlaying;
        private volatile bool _isPlaying;
        private volatile bool _isProcessing;

        private Queue<AudioClip> _playQueue = new Queue<AudioClip>();
        private Queue<AudioClip> _clipPool = new Queue<AudioClip>();

        private int _currentSampleRate = 22050;
        private const int CHANNELS = 1;
        private const int MAX_POOL = 8;

        private CancellationTokenSource _cts;

        // ═══════════════════════════════════════════════
        // NETWORK BUFFERING — added from script 1
        // ═══════════════════════════════════════════════
        private int _sendSeq;
        private int _recvSeq;
        private List<byte[]> _remoteBuf = new List<byte[]>();
        private const int NET_BUF_THRESHOLD = 3;

        private Dictionary<string, string> _hinglishCache =
            new Dictionary<string, string>(200);

        private static readonly Dictionary<string, string> HindiDirect =
            new Dictionary<string, string>
        {
            {"h", "हाँ"}, {"k", "ओके"}, {"ok", "ओके"}, {"okay", "ओके"},
            {"haan", "हाँ"}, {"han", "हाँ"}, {"nahi", "नहीं"}, {"nhi", "नहीं"},
            {"kya", "क्या?"}, {"hai", "है"}, {"ho", "हो"},
            {"achha", "अच्छा!"}, {"acha", "अच्छा!"}, {"theek", "ठीक"},
            {"bas", "बस"}, {"chalo", "चलो"}, {"bhai", "भाई"}, {"yaar", "यार"},
            {"kab", "कब?"}, {"kahan", "कहाँ?"}, {"kaise", "कैसे?"},
            {"kyun", "क्यों?"}, {"main", "मैं"}, {"tum", "तुम"},
            {"theek hai", "ठीक है"}, {"kya hai", "क्या है?"},
        };

        private static HttpClient _http;

        [SerializeField] private StreamojiLipSyncBridge lipSyncBridge;

        private void Awake()
        {
            if (_http == null)
            {
                _http = new HttpClient();
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAIKey}");
                _http.Timeout = TimeSpan.FromSeconds(30);
            }
        }

        private void Start()
        {
            audioSource = transform.parent.GetComponentInChildren<AudioSource>();
            if (audioSource != null && mixerGroup != null)
                audioSource.outputAudioMixerGroup = mixerGroup;
        }

        // ══════════════════════════════════════════════
        // PUBLIC API — called by AvatarChatManager
        // ══════════════════════════════════════════════

        public async UniTask<string> ConvertOnlyAsync(string text, CancellationToken ct)
        {
            try
            {
                return await ConvertAndEnrichAsync(text, EmotionVAD.Neutral, ct);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Voice] ConvertOnly failed: {ex.Message}");
                return text;
            }
        }

        public async UniTask SpeakAsync(
            string convertedText, EmotionVAD vad, CancellationToken ct)
        {
            StopCurrent();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;

            try
            {
                _isPlaying = true;
                lipSyncBridge?.NotifyTTSStarted();


                string enriched = ApplyVADPunctuation(convertedText, vad);
                var sarvamParams = BuildSarvamParams(vad);

                if (IsShort(enriched))
                    await PlaySarvamTTS(enriched, sarvamParams, token);
                else
                    await PlaySarvamChunked(enriched, sarvamParams, token);

                await WaitPlaybackDone(token);
            }
            finally
            {
                _isPlaying = false;
                lipSyncBridge?.NotifyTTSStopped();

            }
        }

        public void StopCurrent()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            StopAllCoroutines();

            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }

            ClearBuffers();
            _isPlaying = false;
            _isProcessing = false;
            lipSyncBridge?.NotifyTTSStopped();
        }

        public void Mute() => mixer?.SetFloat("Volume", -80f);
        public void Unmute() => mixer?.SetFloat("Volume", 0f);

        // ══════════════════════════════════════════════
        // VAD → SARVAM PARAMS
        // ══════════════════════════════════════════════

        private struct SarvamVoiceParams
        {
            public float pitch;
            public float pace;
            public float loudness;
        }

        private SarvamVoiceParams BuildSarvamParams(EmotionVAD vad)
        {
            float pace = Mathf.Lerp(0.85f, 1.30f,
                Mathf.InverseLerp(-1f, 1f, vad.Arousal));

            float pitchArousal = Mathf.Lerp(-0.10f, 0.08f,
                Mathf.InverseLerp(-1f, 1f, vad.Arousal));
            float pitchValence = Mathf.Lerp(-0.04f, 0.04f,
                Mathf.InverseLerp(-1f, 1f, vad.Valence));
            float pitch = Mathf.Clamp(basePitch + pitchArousal + pitchValence, 0.5f, 1.0f);

            float loudness = Mathf.Lerp(1.1f, 2.0f,
                Mathf.InverseLerp(-1f, 1f, vad.Dominance));
            loudness = Mathf.Clamp(loudness * (baseLoudness / 1.5f), 0.8f, 2.5f);

            return new SarvamVoiceParams
            {
                pitch = pitch,
                pace = pace,
                loudness = loudness
            };
        }

        // ElevenLabs equivalent — swap in when you migrate
        private struct ElevenLabsVoiceSettings
        {
            public float stability;
            public float similarity_boost;
            public float style;
            public bool use_speaker_boost;
        }

        private ElevenLabsVoiceSettings BuildElevenLabsParams(EmotionVAD vad)
        {
            float stability = Mathf.Lerp(0.65f, 0.18f,
                Mathf.InverseLerp(-1f, 1f, vad.Arousal));
            float emotionalIntensity = (Mathf.Abs(vad.Valence) + vad.Arousal) / 2f;
            float style = Mathf.Clamp01(emotionalIntensity * 0.9f);
            float similarity = Mathf.Lerp(0.82f, 0.65f, style);

            return new ElevenLabsVoiceSettings
            {
                stability = stability,
                similarity_boost = similarity,
                style = style,
                use_speaker_boost = true
            };
        }

        // ══════════════════════════════════════════════
        // TEXT CONVERSION + ENRICHMENT
        // ══════════════════════════════════════════════

        private async UniTask<string> ConvertAndEnrichAsync(
            string text, EmotionVAD vad, CancellationToken ct)
        {
            string key = $"{text.ToLower().Trim()}|{vad.Arousal:F1}|{vad.Valence:F1}";

            if (_hinglishCache.TryGetValue(key, out var cached))
                return cached;

            string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 2 &&
                HindiDirect.TryGetValue(text.ToLower().Trim(), out var direct))
            {
                string enrichedDirect = ApplyVADPunctuation(direct, vad);
                CacheHinglish(key, enrichedDirect);
                return enrichedDirect;
            }

            if (IsPureEnglish(text))
            {
                string enrichedEn = ApplyVADPunctuation(text, vad);
                CacheHinglish(key, enrichedEn);
                return enrichedEn;
            }

            try
            {
                var result = await ConvertWithGPT(text, vad, ct);
                Debug.Log($"[Voice] GPT: \"{text}\" → \"{result}\"");
                CacheHinglish(key, result);
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Voice] GPT failed: {ex.Message}");
                return ApplyVADPunctuation(text, vad);
            }
        }

        private async UniTask<string> ConvertWithGPT(
            string input, EmotionVAD vad, CancellationToken ct)
        {
            string emotionHint = DescribeEmotion(vad);

            string systemPrompt =
                "You are preparing text for a TTS voice engine (Indian avatar app). " +
                "Do ALL of the following in one step:\n\n" +

                "1. CONVERT: Hinglish/Roman Hindi → Hindi Devanagari script. " +
                "Pure English words (names, tech terms) → keep as-is in English.\n\n" +

                "2. PUNCTUATE for prosody:\n" +
                "   • Add । or . to mark sentence ends\n" +
                "   • Add ? for questions\n" +
                "   • Add ! for exclamations\n" +
                "   • Add , for natural breath pauses\n" +
                "   • Use ... for trailing off / hesitation\n\n" +

                "3. ENRICH for expressiveness based on the speaker's emotional state:\n" +
                $"   • Current emotion: {emotionHint}\n" +
                "   • Excited/happy → emphasis punctuation, keep energy\n" +
                "   • Sad/low energy → softer phrasing, trailing ... where natural\n" +
                "   • Angry/assertive → firm punctuation, no softening\n" +
                "   • Uncertain/confused → add hmm... or अच्छा... prefix if natural\n" +
                "   • Do NOT add words that change meaning. Only adjust punctuation " +
                "and minor phrasing a real speaker would naturally use.\n\n" +

                "OUTPUT: Return ONLY the final Hindi text. No explanation, no quotes.";

            var body = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = input }
                },
                temperature = 0.25,
                max_tokens = 500
            };

            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(openAIUrl, content, ct)
                .AsUniTask()
                .Timeout(TimeSpan.FromSeconds(conversionTimeout));

            if (!response.IsSuccessStatusCode)
                throw new Exception($"HTTP {response.StatusCode}");

            var responseBody = await response.Content.ReadAsStringAsync();
            var parsed = JsonConvert.DeserializeObject<GPTResponse>(responseBody);
            return parsed.choices[0].message.content?.Trim() ?? input;
        }

        private string DescribeEmotion(EmotionVAD vad)
        {
            string label;
            if (vad.Valence > 0.5f && vad.Arousal > 0.4f) label = "excited and happy";
            else if (vad.Valence > 0.3f && vad.Arousal > 0.1f) label = "cheerful and warm";
            else if (vad.Valence > 0.1f) label = "casual and friendly";
            else if (vad.Valence < -0.5f && vad.Arousal < -0.1f) label = "sad and tired";
            else if (vad.Valence < -0.3f && vad.Dominance > 0.2f) label = "frustrated and assertive";
            else if (vad.Valence < -0.2f && vad.Arousal > 0.3f) label = "angry or upset";
            else if (vad.Valence < -0.1f && vad.Dominance < -0.2f) label = "apologetic or uncertain";
            else if (vad.Dominance < -0.3f && vad.Arousal > 0.1f) label = "confused or questioning";
            else if (vad.Arousal > 0.5f) label = "high energy";
            else if (vad.Arousal < -0.3f) label = "calm and low energy";
            else label = "neutral and casual";

            string intensity = vad.Magnitude > 0.7f ? "very " :
                               vad.Magnitude > 0.4f ? "" : "slightly ";

            return $"{intensity}{label} " +
                   $"(V:{vad.Valence:+0.0;-0.0} A:{vad.Arousal:+0.0;-0.0} D:{vad.Dominance:+0.0;-0.0})";
        }

        private string ApplyVADPunctuation(string text, EmotionVAD vad)
        {
            string t = text.Trim();
            bool hasEnd = t.EndsWith(".") || t.EndsWith("!") || t.EndsWith("?") ||
                          t.EndsWith("...") || t.EndsWith("।");

            if (vad.Arousal > 0.5f && !hasEnd) t += "!";
            else if (vad.Valence < -0.3f && vad.Arousal < 0f && !hasEnd) t += "...";
            else if (!hasEnd) t += ".";

            if (vad.Dominance < -0.25f && Mathf.Abs(vad.Valence) < 0.3f &&
                !t.StartsWith("hmm") && t.Split(' ').Length > 2)
                t = "hmm... " + t;

            return t;
        }

        private bool IsPureEnglish(string text) =>
            text.Length > 10 &&
            text.All(c => c < 128 || char.IsWhiteSpace(c) || char.IsPunctuation(c)) &&
            text.Split(' ').Length >= 3;

        private void CacheHinglish(string key, string val)
        {
            if (_hinglishCache.Count >= 200)
                _hinglishCache.Remove(_hinglishCache.Keys.First());
            _hinglishCache[key] = val;
        }

        // ══════════════════════════════════════════════
        // SARVAM TTS
        // ══════════════════════════════════════════════

        private async UniTask PlaySarvamTTS(
            string text, SarvamVoiceParams p, CancellationToken ct)
        {
            byte[] audioBytes = await FetchSarvamAudio(text, p, ct);
            if (audioBytes == null || audioBytes.Length < 100) return;

            var (samples, sampleRate) = await UniTask.RunOnThreadPool(
                () => DecodeWav(audioBytes), cancellationToken: ct);
            if (samples == null || samples.Length == 0) return;

            ct.ThrowIfCancellationRequested();
            _currentSampleRate = sampleRate;

            var clip = AudioClip.Create("SarvamTTS", samples.Length, CHANNELS, sampleRate, false);
            clip.SetData(samples, 0);

            ConfigureAudioSource();
            audioSource.clip = clip;
            audioSource.Play();

            // ═══════════════════════════════════════════════
            // NETWORK: Send complete audio to remote clients
            // ═══════════════════════════════════════════════
            SendComplete_ServerRpc(audioBytes,true);

            ReturnClipDelayedAsync(clip, ct).Forget();  // ✅ FIXED

        }

        private async UniTask PlaySarvamChunked(
            string text, SarvamVoiceParams p, CancellationToken ct)
        {
            _isProcessing = true;
            _sendSeq = 0;  // ← ADDED: reset sequence counter
            ClearBuffers();

            var chunks = SplitTextIntoChunks(text, sarvamMaxChars);
            var fetchTasks = chunks.Select(c => FetchSarvamAudio(c, p, ct)).ToList();

            Debug.Log($"[Voice] Chunked: {chunks.Count} segments");
            ConfigureAudioSource();

            for (int i = 0; i < fetchTasks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                byte[] audioBytes = await fetchTasks[i];
                if (audioBytes == null || audioBytes.Length < 100) continue;

                var (samples, sampleRate) = await UniTask.RunOnThreadPool(
                    () => DecodeWav(audioBytes), cancellationToken: ct);
                if (samples == null || samples.Length == 0) continue;

                Smooth(samples);
                var clip = AudioClip.Create($"Sarvam_{i}", samples.Length, CHANNELS, sampleRate, false);
                clip.SetData(samples, 0);
                _playQueue.Enqueue(clip);


                bool isFirstChunk = (i == 0);
                bool isLastChunk = (i == fetchTasks.Count - 1);
                // ═══════════════════════════════════════════════
                // NETWORK: Send each chunk to remote clients
                // ═══════════════════════════════════════════════
                SendChunk_ServerRpc(audioBytes, _sendSeq++,isFirstChunk , isLastChunk);
            }

            _isProcessing = false;
            await PlayQueuedClipsAsync(ct);  // ✅ RENAMED

        }

        private async UniTask PlayQueuedClipsAsync(CancellationToken ct)  // ✅ RENAMED
        {
            while (_playQueue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                if (!audioSource.isPlaying && _playQueue.Count > 0)
                {
                    var clip = _playQueue.Dequeue();
                    audioSource.Stop();
                    audioSource.clip = clip;
                    audioSource.Play();

                    ReturnClipDelayedAsync(clip, ct).Forget();  // ✅ FIXED

                    await UniTask.WaitWhile(
                        () => audioSource.isPlaying, cancellationToken: ct);
                }

                await UniTask.Yield(ct);
            }
        }

        private async UniTask<byte[]> FetchSarvamAudio(
            string text, SarvamVoiceParams p, CancellationToken ct)
        {
            var payload = new SarvamTTSRequest
            {
                inputs = new[] { text },
                target_language_code = sarvamLanguage,
                speaker = sarvamSpeaker,
                model = sarvamModel,
                pitch = p.pitch,
                pace = p.pace,
                loudness = p.loudness,
                enable_preprocessing = true
            };

            var json = JsonConvert.SerializeObject(payload);
            using var req = new UnityWebRequest(sarvamApiUrl, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("api-subscription-key", sarvamApiKey);

            await req.SendWebRequest().ToUniTask(cancellationToken: ct);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Voice] Sarvam error: {req.error}\n{req.downloadHandler?.text}");
                return null;
            }

            var response = JsonConvert.DeserializeObject<SarvamTTSResponse>(req.downloadHandler.text);
            if (response?.audios == null || response.audios.Length == 0)
            {
                Debug.LogError("[Voice] Sarvam: no audio returned");
                return null;
            }

            try { return Convert.FromBase64String(response.audios[0]); }
            catch (Exception ex) { Debug.LogError($"[Voice] Base64: {ex.Message}"); return null; }
        }

        // ══════════════════════════════════════════════
        // NETWORK RPCs — same pattern as script 1
        // ══════════════════════════════════════════════

        /// <summary>
        [ServerRpc(RequireOwnership = false)]
        private void SendComplete_ServerRpc(byte[] wav, bool startLipSync)
        {
            Debug.Log($"[Voice] SendComplete_ServerRpc - size={wav?.Length ?? 0}, lipSync={startLipSync}");
            ReceiveComplete_ObserversRpc(wav, startLipSync);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveComplete_ObserversRpc(byte[] wav, bool startLipSync)
        {
            Debug.Log($"[Voice] ReceiveComplete_ObserversRpc - Owner={IsOwner}, lipSync={startLipSync}");

            if (!IsOwner)
            {
                // ✅ START LIP SYNC IMMEDIATELY BEFORE PLAYING AUDIO
                if (startLipSync && lipSyncBridge != null)
                {
                    lipSyncBridge.NotifyTTSStarted();
                    Debug.Log("[Voice] Remote lip sync STARTED (with audio)");
                }

                PlayRemote(wav).Forget();
            }
        }




        /// <summary>
        /// Long audio: send WAV chunks in sequence
        [ServerRpc(RequireOwnership = false)]
        private void SendChunk_ServerRpc(byte[] chunk, int seq, bool isFirstChunk, bool isLastChunk)
        {
            Debug.Log($"[Voice] SendChunk_ServerRpc - seq={seq}, first={isFirstChunk}, last={isLastChunk}");
            ReceiveChunk_ObserversRpc(chunk, seq, isFirstChunk, isLastChunk);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveChunk_ObserversRpc(byte[] chunk, int seq, bool isFirstChunk, bool isLastChunk)
        {
            Debug.Log($"[Voice] ReceiveChunk - seq={seq}, expected={_recvSeq}, first={isFirstChunk}, last={isLastChunk}");

            if (seq == _recvSeq)
            {
                _remoteBuf.Add(chunk);
                _recvSeq++;

                // ✅ START LIP SYNC WITH FIRST CHUNK
                if (isFirstChunk && lipSyncBridge != null)
                {
                    lipSyncBridge.NotifyTTSStarted();
                    Debug.Log("[Voice] Remote lip sync STARTED (first chunk)");
                }

                if (_remoteBuf.Count >= NET_BUF_THRESHOLD)
                {
                    PlayRemoteBuffered(isLastChunk).Forget();  // ← Pass last chunk flag
                }
            }
            else if (seq > _recvSeq)
            {
                Debug.LogWarning($"[Voice] Out of order chunk! Expected {_recvSeq}, got {seq}");
                _recvSeq = seq + 1;
                _remoteBuf.Clear();
                _remoteBuf.Add(chunk);

                // Restart lip sync if we had to reset
                if (isFirstChunk && lipSyncBridge != null)
                {
                    lipSyncBridge.NotifyTTSStarted();
                }
            }
        }




        private async UniTaskVoid PlayRemoteBuffered(bool isLastChunk = false)
        {
            Debug.Log($"[Voice] PlayRemoteBuffered - bufferCount={_remoteBuf.Count}, isLast={isLastChunk}");

            var cts = new CancellationTokenSource();
            var ct = cts.Token;

            try
            {
                int chunkIndex = 0;
                while (_remoteBuf.Count > 0)
                {
                    var data = _remoteBuf[0];
                    _remoteBuf.RemoveAt(0);

                    var (samples, sampleRate) = await UniTask.RunOnThreadPool(
                        () => DecodeWav(data), cancellationToken: ct);

                    if (samples == null) continue;

                    var clip = AudioClip.Create($"RemoteTTS_{chunkIndex}", samples.Length, CHANNELS, sampleRate, false);
                    clip.SetData(samples, 0);

                    while (audioSource.isPlaying)
                    {
                        await UniTask.Delay(10, cancellationToken: ct);
                    }

                    audioSource.clip = clip;
                    audioSource.Play();

                    Debug.Log($"[Voice] Playing remote chunk {chunkIndex}");

                    await UniTask.Delay((int)(clip.length * 1000) + 50, cancellationToken: ct);

                    if (_clipPool.Count < MAX_POOL)
                        _clipPool.Enqueue(clip);
                    else
                        DestroyImmediate(clip);

                    chunkIndex++;
                }

                // ✅ STOP LIP SYNC WHEN ALL CHUNKS PLAYED
                if (isLastChunk && lipSyncBridge != null)
                {
                    lipSyncBridge.NotifyTTSStopped();
                    Debug.Log("[Voice] Remote lip sync STOPPED (last chunk played)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Voice] PlayRemoteBuffered error: {ex.Message}");
            }
            finally
            {
                cts?.Dispose();
            }
        }


        private async UniTaskVoid PlayRemote(byte[] wav)
        {
            Debug.Log($"[Voice] PlayRemote - size={wav?.Length ?? 0}");

            if (wav == null || wav.Length == 0) return;

            var cts = new CancellationTokenSource();
            var ct = cts.Token;

            try
            {
                var (samples, sampleRate) = await UniTask.RunOnThreadPool(
                    () => DecodeWav(wav), cancellationToken: ct);

                if (samples == null) return;

                while (audioSource.isPlaying)
                    await UniTask.Delay(50, cancellationToken: ct);

                var clip = AudioClip.Create("RemoteTTS", samples.Length, CHANNELS, sampleRate, false);
                clip.SetData(samples, 0);

                audioSource.clip = clip;
                audioSource.Play();

                Debug.Log($"[Voice] Playing remote audio: {clip.length:F2}s");

                // ✅ WAIT FOR AUDIO TO FINISH, THEN STOP LIP SYNC
                await UniTask.Delay((int)(clip.length * 1000), cancellationToken: ct);

                if (lipSyncBridge != null)
                {
                    lipSyncBridge.NotifyTTSStopped();
                    Debug.Log("[Voice] Remote lip sync STOPPED (audio finished)");
                }

                ReturnClipDelayedAsync(clip, ct).Forget();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Voice] PlayRemote error: {ex.Message}");
            }
            finally
            {
                cts?.Dispose();
            }
        }

        // ══════════════════════════════════════════════
        // EVERYTHING BELOW UNCHANGED
        // ══════════════════════════════════════════════

        private List<string> SplitTextIntoChunks(string text, int maxChars)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;

            char[] sentenceEnders = { '.', '!', '?', '।', '॥' };
            var sentences = new List<string>();
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (Array.IndexOf(sentenceEnders, text[i]) >= 0)
                {
                    var sentence = text.Substring(start, i - start + 1).Trim();
                    if (!string.IsNullOrEmpty(sentence)) sentences.Add(sentence);
                    start = i + 1;
                }
            }

            if (start < text.Length)
            {
                var remaining = text.Substring(start).Trim();
                if (!string.IsNullOrEmpty(remaining)) sentences.Add(remaining);
            }

            var current = new StringBuilder();
            foreach (var sentence in sentences)
            {
                if (sentence.Length > maxChars)
                {
                    if (current.Length > 0) { result.Add(current.ToString().Trim()); current.Clear(); }
                    result.AddRange(ForceSplitLong(sentence, maxChars));
                    continue;
                }
                if (current.Length + sentence.Length + 1 > maxChars)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                if (current.Length > 0) current.Append(" ");
                current.Append(sentence);
            }

            if (current.Length > 0) result.Add(current.ToString().Trim());
            return result;
        }

        private List<string> ForceSplitLong(string text, int maxChars)
        {
            var parts = new List<string>();
            foreach (var delim in new[] { ',', '،', ' ' })
            {
                if (!text.Contains(delim)) continue;
                var buf = new StringBuilder();
                foreach (var seg in text.Split(delim))
                {
                    if (buf.Length + seg.Length + 1 > maxChars && buf.Length > 0)
                    { parts.Add(buf.ToString().Trim()); buf.Clear(); }
                    if (buf.Length > 0) buf.Append(delim);
                    buf.Append(seg);
                }
                if (buf.Length > 0) parts.Add(buf.ToString().Trim());
                return parts;
            }
            for (int i = 0; i < text.Length; i += maxChars)
                parts.Add(text.Substring(i, Math.Min(maxChars, text.Length - i)));
            return parts;
        }

        private (float[] samples, int sampleRate) DecodeWav(byte[] wav)
        {
            if (wav == null || wav.Length < 44) return (null, 22050);
            try
            {
                if (Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" ||
                    Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
                    return (PCMToFloat(wav), 22050);

                int audioFormat = BitConverter.ToInt16(wav, 20);
                int channels = BitConverter.ToInt16(wav, 22);
                int sampleRate = BitConverter.ToInt32(wav, 24);
                int bitsPerSample = BitConverter.ToInt16(wav, 34);

                int dataOffset = 12, dataSize = 0;
                while (dataOffset < wav.Length - 8)
                {
                    string chunkId = Encoding.ASCII.GetString(wav, dataOffset, 4);
                    int chunkSize = BitConverter.ToInt32(wav, dataOffset + 4);
                    if (chunkId == "data") { dataOffset += 8; dataSize = chunkSize; break; }
                    dataOffset += 8 + chunkSize;
                }

                if (dataSize == 0 || dataOffset >= wav.Length) return (null, sampleRate);
                dataSize = Math.Min(dataSize, wav.Length - dataOffset);

                float[] samples;
                if (bitsPerSample == 16)
                {
                    int n = dataSize / 2; samples = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        int idx = dataOffset + i * 2;
                        if (idx + 1 >= wav.Length) break;
                        samples[i] = (short)(wav[idx] | (wav[idx + 1] << 8)) / 32768f;
                    }
                }
                else if (bitsPerSample == 8)
                {
                    samples = new float[dataSize];
                    for (int i = 0; i < dataSize; i++)
                        samples[i] = (wav[dataOffset + i] - 128) / 128f;
                }
                else if (bitsPerSample == 32 && audioFormat == 3)
                {
                    int n = dataSize / 4; samples = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        int idx = dataOffset + i * 4;
                        if (idx + 3 >= wav.Length) break;
                        samples[i] = BitConverter.ToSingle(wav, idx);
                    }
                }
                else return (null, sampleRate);

                if (channels == 2 && samples.Length > 1)
                {
                    int mn = samples.Length / 2; var mono = new float[mn];
                    for (int i = 0; i < mn; i++)
                        mono[i] = (samples[i * 2] + samples[i * 2 + 1]) * 0.5f;
                    samples = mono;
                }

                return (samples, sampleRate);
            }
            catch (Exception ex) { Debug.LogError($"[Voice] WAV: {ex.Message}"); return (null, 22050); }
        }

        private static float[] PCMToFloat(byte[] pcm)
        {
            int n = pcm.Length / 2; var s = new float[n];
            for (int i = 0; i < n; i++)
                s[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
            return s;
        }

        private static void Smooth(float[] s)
        {
            if (s.Length < 64) return;
            int fade = Math.Min(32, s.Length / 4);
            for (int i = 0; i < fade; i++) s[i] *= (float)i / fade;
            for (int i = s.Length - fade; i < s.Length; i++)
                s[i] *= (float)(s.Length - 1 - i) / fade;
        }

        private void ConfigureAudioSource()
        {
            audioSource.priority = 0;
            audioSource.volume = 1f;
            audioSource.pitch = 1f;
            audioSource.spatialBlend = 0f;
            audioSource.reverbZoneMix = 0f;
        }

        private async UniTaskVoid ReturnClipDelayedAsync(AudioClip clip, CancellationToken ct)
        {
            try
            {
                await UniTask.Delay(
                    (int)((clip.length + 0.5f) * 1000),
                    cancellationToken: ct
                );

                if (_clipPool.Count < MAX_POOL)
                    _clipPool.Enqueue(clip);
                else
                    DestroyImmediate(clip);
            }
            catch (OperationCanceledException)
            {
                // Cleanup on cancel
                if (clip != null) DestroyImmediate(clip);
            }
        }

        private async UniTask WaitPlaybackDone(CancellationToken ct)
        {
            while (_playQueue.Count > 0 && !ct.IsCancellationRequested)
                await UniTask.Delay(50, cancellationToken: ct);
            while (audioSource.isPlaying && !ct.IsCancellationRequested)
                await UniTask.Delay(50, cancellationToken: ct);
            if (!ct.IsCancellationRequested)
                await UniTask.Delay(200, cancellationToken: ct);
        }

        private void ClearBuffers()
        {
            while (_playQueue.Count > 0) { var c = _playQueue.Dequeue(); if (c) DestroyImmediate(c); }
        }

        private bool IsShort(string t) =>
            t.Length <= shortThreshold || t.Split(' ').Length <= 8;

        private void OnDestroy()
        {
            StopCurrent();
            while (_clipPool.Count > 0) { var c = _clipPool.Dequeue(); if (c) DestroyImmediate(c); }
        }

        [Serializable]
        private class SarvamTTSRequest
        {
            public string[] inputs;
            public string target_language_code, speaker, model;
            public float pitch, pace, loudness;
            public bool enable_preprocessing;
        }
        [Serializable] private class SarvamTTSResponse { public string[] audios; }
        [Serializable] private class GPTResponse { public GPTChoice[] choices; }
        [Serializable] private class GPTChoice { public GPTMsg message; }
        [Serializable] private class GPTMsg { public string content; }
    }
}