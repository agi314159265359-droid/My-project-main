using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet.Object;
using UnityEngine;

namespace Mikk.Avatar
{
    public class AvatarChatManager : NetworkBehaviour
    {
        [Header("Systems")]
        [SerializeField] private EmotionAnalyzerVAD emotionAnalyzer;
        [SerializeField] private RealtimeFaceDriver faceDriver;
        [SerializeField] private VoicePipeline voicePipeline;
        [SerializeField] private AvatarNetworkSync networkSync;
        [SerializeField] private BodyAnimationController bodyController;  // ← ADD

        [Header("Settings")]
        [SerializeField] private float maxEmotionWait = 2f;
        [SerializeField] private bool enableDebugLogs = true;

        private CancellationTokenSource _messageCts;
        private bool _isProcessing;
        public bool IsProcessing => _isProcessing;

        // ══════════════════════════════════════════════
        // PUBLIC API
        // ══════════════════════════════════════════════

        public void ProcessChatMessage(string rawMessage)
        {
            if (string.IsNullOrWhiteSpace(rawMessage)) return;

            CancelCurrent();
            _messageCts = new CancellationTokenSource();
            ProcessAsync(rawMessage, _messageCts.Token).Forget();
        }


        private async UniTaskVoid ProcessAsync(string rawMessage, CancellationToken ct)
        {
            _isProcessing = true;

            try
            {
                // ─── PHASE 1 ───
                var processed = MessagePreprocessor.Process(rawMessage);
                if (processed.IsEmpty) { Log("Empty, skipping"); return; }

                string cleanText = processed.CleanText;
                Log($"Processing: \"{cleanText}\"");

                // ─── PHASE 2: fire both, store as tasks ───
                var emotionTask = emotionAnalyzer.AnalyzeAsync(
                    cleanText, ct,
                    hasLaugh: processed.HasLaughter,
                    hasSad: processed.HasSadness,
                    hasAngry: processed.HasAnger);

                var conversionTask = voicePipeline.ConvertOnlyAsync(cleanText, ct);

                // ─── PHASE 3: await each separately ───
                EmotionAnalyzerVAD.EmotionResult result;
                try
                {
                    result = await emotionTask.Timeout(TimeSpan.FromSeconds(maxEmotionWait));
                }
                catch (TimeoutException)
                {
                    result = EmotionAnalyzerVAD.QuickEstimate(
                        cleanText,
                        hasLaugh: processed.HasLaughter,
                        hasSad: processed.HasSadness,
                        hasAngry: processed.HasAnger);
                    Log($"VAD timeout, estimated: {result}");
                }

                string convertedText = await conversionTask;
                ct.ThrowIfCancellationRequested();

                Log($"Emotion: {result.VAD} | Gesture: {result.Hint}");
                Log($"Converted: \"{convertedText}\"");

                // ══════════════════════════════════════════════════════════
                // APPLY EMOTION & SYNC
                // ══════════════════════════════════════════════════════════

                // SetEmotion() handles:
                // - Face blendshapes
                // - Body animations
                // - Head gestures  
                // - Network sync for emotion/body/head
                faceDriver.SetEmotion(result.VAD);

                // Gesture hint needs separate sync (not handled by SetEmotion)
                bodyController?.SetGestureHint(result.Hint);
                networkSync?.SyncGestureHint(result.Hint);  // ← ADD THIS

                // ❌ REMOVED: networkSync?.SyncEmotion(result.VAD);
                // (already synced inside faceDriver.SetEmotion())

                // ══════════════════════════════════════════════════════════
                // TTS
                // ══════════════════════════════════════════════════════════

                try
                {
                    await voicePipeline.SpeakAsync(convertedText, result.VAD, ct);
                }
                catch (Exception ex)
                {
                    Log($"Voice error (non-fatal): {ex.Message}");
                }

                ct.ThrowIfCancellationRequested();
                await WaitForAudioComplete(ct);
                Log("Complete");
            }
            catch (OperationCanceledException) { Log("Cancelled"); }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarChat] {ex.Message}\n{ex.StackTrace}");
                faceDriver?.ReturnToNeutral();
            }
            finally { _isProcessing = false; }
        }


        private async UniTask WaitForAudioComplete(CancellationToken ct)
        {
            if (voicePipeline == null) return;

            while (voicePipeline.IsPlaying && !ct.IsCancellationRequested)
                await UniTask.Delay(50, cancellationToken: ct);

            if (!ct.IsCancellationRequested)
                await UniTask.Delay(200, cancellationToken: ct);
        }

        private void CancelCurrent()
        {
            if (_messageCts != null)
            {
                _messageCts.Cancel();
                _messageCts.Dispose();
                _messageCts = null;
            }

            voicePipeline?.StopCurrent();
            faceDriver?.Interrupt();
            networkSync?.SyncInterrupt();
        }

        private void Log(string msg)
        {
            if (enableDebugLogs) Debug.Log($"[AvatarChat] {msg}");
        }

        // ══════════════════════════════════════════════
        // LIFECYCLE
        // ══════════════════════════════════════════════

        private void OnDestroy() => CancelCurrent();
        private void OnApplicationPause(bool paused) { if (paused) CancelCurrent(); }
    }
}