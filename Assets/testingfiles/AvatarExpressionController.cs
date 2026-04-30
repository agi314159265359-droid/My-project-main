using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Mikk.Avatar.Expression
{
    /// <summary>
    /// Master controller for avatar facial expressions.
    /// This is a MonoBehaviour — network sync is handled by ChatGPT script.
    /// </summary>
    public class AvatarExpressionController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SkinnedMeshRenderer faceMesh;
        [SerializeField] private Animator bodyAnimator;

        [Header("Expression Settings")]
        [SerializeField] private float emotionTransitionSpeed = 2.5f;
        [SerializeField] private float microExpressionIntensity = 1.0f;
        [SerializeField] private bool enableIdleMicroMovements = true;
        [SerializeField] private bool enableEmotionOverlays = true;

        // Sub-systems
        private IdleMicroMovementLayer idleLayer;
        private EmotionBlendshapeLayer emotionLayer;
        private MicroExpressionLayer microExpressionLayer;
        private ExpressionAIAnalyzer aiAnalyzer;

        private Dictionary<string, int> blendshapeMap = new Dictionary<string, int>();

        // Thread-safe emotion queue
        private readonly Queue<FullExpressionData> expressionQueue = new Queue<FullExpressionData>();
        private readonly object queueLock = new object();

        // Cached blendshape weights (applied every frame)
        private float[] finalBlendshapeWeights;
        private float[] idleWeights;
        private float[] emotionWeights;
        private float[] microExpressionWeights;
        private int totalBlendshapes;

        // Cached viseme indices (don't rebuild every frame)
        private HashSet<int> cachedVisemeIndices;

        private CancellationTokenSource expressionCts;
        private Coroutine returnToIdleCoroutine;

        private bool isInitialized = false;

        private void Start()
        {
            InitializeBlendshapeMapping();
            InitializeLayers();
            isInitialized = true;
        }

        private void InitializeBlendshapeMapping()
        {
            if (faceMesh == null)
            {
                faceMesh = transform.GetChild(0).Find("Head_Mesh").GetComponent<SkinnedMeshRenderer>();
            }
            if (bodyAnimator == null)
            {
                bodyAnimator = transform.GetComponent<Animator>();
            }

            if (faceMesh == null)
            {
                Debug.LogError("[AvatarExpression] Could not find Head_Mesh SkinnedMeshRenderer!");
                return;
            }

            totalBlendshapes = faceMesh.sharedMesh.blendShapeCount;
            finalBlendshapeWeights = new float[totalBlendshapes];
            idleWeights = new float[totalBlendshapes];
            emotionWeights = new float[totalBlendshapes];
            microExpressionWeights = new float[totalBlendshapes];

            // Auto-discover blendshapes by name
            for (int i = 0; i < totalBlendshapes; i++)
            {
                string name = faceMesh.sharedMesh.GetBlendShapeName(i);
                blendshapeMap[name.ToLower()] = i;
                var shortName = name.Contains(".") ? name.Split('.').Last().ToLower() : name.ToLower();
                if (!blendshapeMap.ContainsKey(shortName))
                    blendshapeMap[shortName] = i;
            }

            // Cache viseme indices once
            cachedVisemeIndices = BuildVisemeIndices();

            Debug.Log($"[AvatarExpression] Mapped {blendshapeMap.Count} blendshapes, " +
                      $"{cachedVisemeIndices.Count} visemes excluded");
        }

        private void InitializeLayers()
        {
            idleLayer = new IdleMicroMovementLayer(blendshapeMap, totalBlendshapes);
            emotionLayer = new EmotionBlendshapeLayer(blendshapeMap, totalBlendshapes);
            microExpressionLayer = new MicroExpressionLayer(blendshapeMap, totalBlendshapes);
            aiAnalyzer = new ExpressionAIAnalyzer();
        }

        private void Update()
        {
            if (!isInitialized || faceMesh == null) return;

            float dt = Time.deltaTime;

            ProcessExpressionQueue();
            UpdateIdleLayer(dt);
            UpdateEmotionLayer(dt);
            UpdateMicroExpressionLayer(dt);
            CompositeAndApply();
        }

        /// <summary>
        /// Called when a chat message is received. Main entry point.
        /// Call this from your ChatGPT script.
        /// </summary>
        public void OnChatMessageReceived(string message)
        {
            if (!isInitialized)
            {
                Debug.LogWarning("[AvatarExpression] Not initialized yet, ignoring message");
                return;
            }

            expressionCts?.Cancel();
            expressionCts = new CancellationTokenSource();
            AnalyzeAndApplyExpression(message, expressionCts.Token).Forget();
        }

        /// <summary>
        /// Called from network to apply expression on remote clients.
        /// Call this from your ChatGPT script's ObserversRpc.
        /// </summary>
        public void ApplyExpressionFromNetwork(FullExpressionData data)
        {
            if (!isInitialized || data == null) return;
            QueueExpression(data);
        }

        /// <summary>
        /// Returns the analyzed expression data for network sync.
        /// Your ChatGPT script can use this in its ServerRpc.
        /// </summary>
        public event Action<FullExpressionData> OnExpressionReady;

        private async UniTaskVoid AnalyzeAndApplyExpression(string message, CancellationToken ct)
        {
            try
            {
                if (aiAnalyzer == null)
                {
                    Debug.LogError("[AvatarExpression] aiAnalyzer is NULL!");
                    return;
                }

                // Try cache first
                var cached = aiAnalyzer.TryGetCached(message);
                if (cached != null)
                {
                    Debug.Log($"[AvatarExpression] Cache hit: {cached.primaryEmotion}");
                    QueueExpression(cached);
                    OnExpressionReady?.Invoke(cached);
                    return;
                }

                // Analyze with AI
                var expressionData = await aiAnalyzer.AnalyzeMessage(message, ct);

                if (expressionData == null)
                {
                    Debug.LogWarning("[AvatarExpression] AI returned null, using default");
                    expressionData = FullExpressionData.CreateDefault();
                }

                Debug.Log($"[AvatarExpression] Result: {expressionData.primaryEmotion} " +
                          $"intensity={expressionData.emotionIntensity} " +
                          $"micros={expressionData.microExpressions?.Count ?? 0}");

                QueueExpression(expressionData);

                // Notify listeners (ChatGPT script) for network sync
                OnExpressionReady?.Invoke(expressionData);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[AvatarExpression] Expression analysis cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarExpression] Analysis failed: {ex.Message}\n{ex.StackTrace}");
                QueueExpression(FullExpressionData.CreateDefault());
            }
        }

        private void QueueExpression(FullExpressionData data)
        {
            lock (queueLock)
            {
                expressionQueue.Clear();
                expressionQueue.Enqueue(data);
            }
        }

        private void ProcessExpressionQueue()
        {
            FullExpressionData data = null;
            lock (queueLock)
            {
                if (expressionQueue.Count > 0)
                    data = expressionQueue.Dequeue();
            }

            if (data != null)
            {
                ApplyFullExpression(data);
            }
        }

        private void ApplyFullExpression(FullExpressionData data)
        {
            if (data == null) return;

            if (emotionLayer == null || idleLayer == null || microExpressionLayer == null)
            {
                Debug.LogError("[AvatarExpression] Layers not initialized!");
                return;
            }

            // Set emotion target
            emotionLayer.SetTargetEmotion(data.primaryEmotion ?? "neutral", data.emotionIntensity);

            // Trigger body animation
            if (bodyAnimator != null)
            {
                TriggerBodyAnimation(data);
            }

            // Apply micro-expression sequence
            if (data.microExpressions != null && data.microExpressions.Count > 0)
            {
                microExpressionLayer.PlaySequence(data.microExpressions);
            }

            // Adjust idle layer based on emotional state
            idleLayer.SetEmotionalContext(data.primaryEmotion ?? "neutral", data.emotionIntensity);

            // Schedule return to idle
            float holdDuration = data.isLongMessage ? 5f : 3f;
            holdDuration *= Mathf.Lerp(0.8f, 1.5f, data.emotionIntensity);

            // Stop previous return-to-idle
            if (returnToIdleCoroutine != null)
            {
                StopCoroutine(returnToIdleCoroutine);
            }
            returnToIdleCoroutine = StartCoroutine(ScheduleReturnToIdle(holdDuration, data.emotionIntensity));
        }

        private void TriggerBodyAnimation(FullExpressionData data)
        {
            if (bodyAnimator == null || data == null) return;

            string emotion = data.primaryEmotion ?? "neutral";
            string variant = data.animationVariant ?? "0";
            string animState = $"{emotion}_{variant}";
            string floatParam = $"{emotion}float";

            try
            {
                bodyAnimator.SetFloat(floatParam, data.emotionIntensity);
                bodyAnimator.CrossFade(animState, 0.3f);

                int faceLayerIndex = bodyAnimator.GetLayerIndex("faceLayer");
                if (faceLayerIndex >= 0)
                {
                    bodyAnimator.CrossFade(animState, 0.3f, faceLayerIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AvatarExpression] Body anim failed for '{animState}': {ex.Message}");
            }
        }

        private IEnumerator ScheduleReturnToIdle(float delay, float currentIntensity)
        {
            yield return new WaitForSeconds(delay);

            float fadeDuration = Mathf.Lerp(0.5f, 1.5f, currentIntensity);
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;
                float smoothT = t * t * (3f - 2f * t);
                emotionLayer.FadeToNeutral(smoothT);
                yield return null;
            }

            emotionLayer.SetTargetEmotion("neutral", 0f);

            if (bodyAnimator != null)
            {
                int armLayer = bodyAnimator.GetLayerIndex("ArmLayer");
                if (armLayer >= 0)
                    bodyAnimator.CrossFade("New State", 0.5f, armLayer);
            }

            returnToIdleCoroutine = null;
        }

        #region Layer Updates

        private void UpdateIdleLayer(float dt)
        {
            if (!enableIdleMicroMovements || idleLayer == null) return;
            idleLayer.Update(dt, idleWeights);
        }

        private void UpdateEmotionLayer(float dt)
        {
            if (emotionLayer == null) return;
            emotionLayer.Update(dt, emotionTransitionSpeed, emotionWeights);
        }

        private void UpdateMicroExpressionLayer(float dt)
        {
            if (!enableEmotionOverlays || microExpressionLayer == null) return;
            microExpressionLayer.Update(dt, microExpressionWeights);
        }

        #endregion

        #region Compositing

        private void CompositeAndApply()
        {
            for (int i = 0; i < totalBlendshapes; i++)
            {
                float weight = idleWeights[i];

                weight = Mathf.Lerp(weight, emotionWeights[i],
                    emotionWeights[i] > 0.01f ? 1f : 0f);

                weight += microExpressionWeights[i];

                finalBlendshapeWeights[i] = Mathf.Clamp01(weight);
            }

            ApplyBlendshapeWeights();
        }

        private void ApplyBlendshapeWeights()
        {
            if (cachedVisemeIndices == null) return;

            for (int i = 0; i < totalBlendshapes; i++)
            {
                if (cachedVisemeIndices.Contains(i)) continue;

                float currentWeight = faceMesh.GetBlendShapeWeight(i);
                float targetWeight = finalBlendshapeWeights[i] * 2.0f;

                float smoothed = Mathf.Lerp(currentWeight, targetWeight, Time.deltaTime * 8f);
                faceMesh.SetBlendShapeWeight(i, smoothed);
            }
        }

        private HashSet<int> BuildVisemeIndices()
        {
            var indices = new HashSet<int>();
            var visemeNames = new[] {
                "viseme_sil", "viseme_pp", "viseme_ff", "viseme_th",
                "viseme_dd", "viseme_kk", "viseme_ch", "viseme_ss",
                "viseme_nn", "viseme_rr", "viseme_aa", "viseme_e",
                "viseme_i", "viseme_o", "viseme_u"
            };

            foreach (var name in visemeNames)
            {
                if (blendshapeMap.TryGetValue(name, out int idx))
                    indices.Add(idx);
            }
            return indices;
        }

        #endregion

        private void OnDestroy()
        {
            expressionCts?.Cancel();
            expressionCts?.Dispose();
            if (returnToIdleCoroutine != null)
                StopCoroutine(returnToIdleCoroutine);
        }
    }
}