using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using System.Collections.Generic;

namespace Mikk.Avatar
{
    /// <summary>
    /// Complete network sync for avatar expressions.
    /// Syncs: emotion, audio, body anims, head gestures, talking state, gesture hints.
    /// </summary>
    public class AvatarNetworkSync : NetworkBehaviour
    {
        [Header("Components")]
        [SerializeField] private RealtimeFaceDriver faceDriver;
        [SerializeField] private BodyAnimationController bodyController;
        [SerializeField] private HeadMotionController headMotion;
        [SerializeField] private VADToFaceMapper faceMapper;

        // ══════════════════════════════════════════════════════════════════
        // EMOTION SYNC
        // ══════════════════════════════════════════════════════════════════

        public void SyncEmotion(EmotionVAD emotion)
        {
            if (!IsOwner) return;
            SyncEmotion_ServerRpc(emotion.Valence, emotion.Arousal, emotion.Dominance);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SyncEmotion_ServerRpc(float v, float a, float d)
        {
            ReceiveEmotion_ObserversRpc(v, a, d);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveEmotion_ObserversRpc(float v, float a, float d)
        {
            var emotion = new EmotionVAD { Valence = v, Arousal = a, Dominance = d };
            faceDriver?.SetEmotion(emotion);
            Debug.Log($"[NetSync] Emotion received: {emotion}");
        }

        // ══════════════════════════════════════════════════════════════════
        // BODY ANIMATION SYNC
        // ══════════════════════════════════════════════════════════════════

        public void SyncBodyAnimation(string animName, float returnDelay)
        {
            if (!IsOwner) return;
            SyncBodyAnimation_ServerRpc(animName, returnDelay);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SyncBodyAnimation_ServerRpc(string animName, float returnDelay)
        {
            ReceiveBodyAnimation_ObserversRpc(animName, returnDelay);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveBodyAnimation_ObserversRpc(string animName, float returnDelay)
        {
            if (bodyController == null) return;
            bodyController.PlayAnimation(animName);
            bodyController.ReturnToIdle(returnDelay);
            Debug.Log($"[NetSync] Body anim: {animName} (return in {returnDelay}s)");
        }

        // ══════════════════════════════════════════════════════════════════
        // HEAD GESTURE SYNC
        // ══════════════════════════════════════════════════════════════════

        public enum HeadGestureType : byte
        {
            Nod,
            Shake,
            Tilt,
            LookDown,
            TalkingGesture,
            Suppress
        }

        public void SyncHeadGesture(HeadGestureType type, float param = 0f)
        {
            if (!IsOwner) return;
            SyncHeadGesture_ServerRpc((byte)type, param);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SyncHeadGesture_ServerRpc(byte type, float param)
        {
            ReceiveHeadGesture_ObserversRpc(type, param);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveHeadGesture_ObserversRpc(byte type, float param)
        {
            if (headMotion == null) return;

            switch ((HeadGestureType)type)
            {
                case HeadGestureType.Nod:
                    headMotion.TriggerNod();
                    Debug.Log("[NetSync] Head: Nod");
                    break;

                case HeadGestureType.Shake:
                    headMotion.TriggerShake();
                    Debug.Log("[NetSync] Head: Shake");
                    break;

                case HeadGestureType.Tilt:
                    headMotion.TriggerTilt(param); // param = direction (-1 or 1)
                    Debug.Log($"[NetSync] Head: Tilt {param}");
                    break;

                case HeadGestureType.LookDown:
                    headMotion.TriggerLookDown();
                    Debug.Log("[NetSync] Head: LookDown");
                    break;

                case HeadGestureType.TalkingGesture:
                    headMotion.TriggerTalkingGesture();
                    Debug.Log("[NetSync] Head: TalkingGesture");
                    break;

                case HeadGestureType.Suppress:
                    headMotion.SuppressDuringBodyAnim(param); // param = duration
                    Debug.Log($"[NetSync] Head: Suppress {param}s");
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // GESTURE HINT SYNC
        // ══════════════════════════════════════════════════════════════════

        public void SyncGestureHint(GestureHint hint)
        {
            if (!IsOwner) return;
            SyncGestureHint_ServerRpc((byte)hint);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SyncGestureHint_ServerRpc(byte hint)
        {
            ReceiveGestureHint_ObserversRpc(hint);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveGestureHint_ObserversRpc(byte hint)
        {
            bodyController?.SetGestureHint((GestureHint)hint);
            Debug.Log($"[NetSync] Gesture hint: {(GestureHint)hint}");
        }

        // ══════════════════════════════════════════════════════════════════
        // TALKING STATE SYNC
        // ══════════════════════════════════════════════════════════════════

        public void SyncTalkingState(bool isTalking)
        {
            if (!IsOwner) return;
            SyncTalkingState_ServerRpc(isTalking);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SyncTalkingState_ServerRpc(bool isTalking)
        {
            ReceiveTalkingState_ObserversRpc(isTalking);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveTalkingState_ObserversRpc(bool isTalking)
        {
            if (bodyController == null) return;

            if (isTalking)
            {
                bodyController.StartTalkingGestures();
                Debug.Log("[NetSync] Talking started");
            }
            else
            {
                bodyController.StopTalkingGesturesGracefully();
                Debug.Log("[NetSync] Talking stopped");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // INTERRUPT SYNC
        // ══════════════════════════════════════════════════════════════════

        public void SyncInterrupt()
        {
            if (!IsOwner) return;
            SyncInterrupt_ServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void SyncInterrupt_ServerRpc()
        {
            ReceiveInterrupt_ObserversRpc();
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveInterrupt_ObserversRpc()
        {
            faceDriver?.Interrupt();
            bodyController?.InterruptAnimation();
            headMotion?.ResetToNeutral();
            Debug.Log("[NetSync] Interrupt");
        }
    }
}