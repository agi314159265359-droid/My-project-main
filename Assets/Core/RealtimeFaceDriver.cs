using System.Collections.Generic;
using UnityEngine;

namespace Mikk.Avatar
{
    public class RealtimeFaceDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] public SkinnedMeshRenderer faceMesh;
        [SerializeField] public SkinnedMeshRenderer eyeLeftMesh;   // ← ADDED
        [SerializeField] public SkinnedMeshRenderer eyeRightMesh;  // ← ADDED
        [SerializeField] private AliveSystem aliveSystem;
        [SerializeField] private StreamojiLipSyncBridge lipSyncBridge;
        [SerializeField] private AvatarNetworkSync networkSync;


        [SerializeField] private HeadMotionController headMotion;
        [SerializeField] private VADToFaceMapper faceMapper;
        [SerializeField] private BodyAnimationController bodyController;

        [Header("Transition Speeds")]
        [SerializeField, Range(1f, 8f)] private float emotionOnsetSpeed = 4f;
        [SerializeField, Range(0.3f, 3f)] private float emotionFadeSpeed = 1.2f;

        // Blendshape index cache — face mesh
        private Dictionary<string, int> _blendIndex = new Dictionary<string, int>();

        // Blendshape index cache — eye meshes
        private Dictionary<string, int> _eyeLeftIndex = new Dictionary<string, int>();
        private Dictionary<string, int> _eyeRightIndex = new Dictionary<string, int>();

        // Eye look blendshape names that route to eye meshes instead of face mesh
        private static readonly HashSet<string> EyeLeftBlendshapes = new HashSet<string>
        {
            "eyeLookUpLeft", "eyeLookDownLeft", "eyeLookInLeft", "eyeLookOutLeft"
        };

        private static readonly HashSet<string> EyeRightBlendshapes = new HashSet<string>
        {
            "eyeLookUpRight", "eyeLookDownRight", "eyeLookInRight", "eyeLookOutRight"
        };

        // RPM-Specific indices (face mesh only)
        private int _idx_mouthOpen = 0;
        private int _idx_mouthSmile = 16;
        private int _idx_eyesClosed = -1;
        private int _idx_eyesLookUp = -1;
        private int _idx_eyesLookDown = -1;
        private int _idx_eyeBlinkLeft = -1;
        private int _idx_eyeBlinkRight = -1;

        // State
        private FacialPose _currentPose = FacialPose.Neutral;
        private FacialPose _targetPose = FacialPose.Neutral;
        private bool _isReturningToNeutral;
        private float _holdTimer;
        private float _holdDuration;
        private bool _wasLipSyncing;
        private bool _holdUntilAudioDone;
        private EmotionVAD _currentEmotion = EmotionVAD.Neutral;

        private HashSet<string> _activeLastFrame = new HashSet<string>();




        private float _browFlashTimer = 0f;
        private float _browFlashDuration = 0.18f;   // quick — 180ms
        private float _browFlashStrength = 0f;
        private bool _browFlashActive = false;


        private bool _wasAudioPlaying = false;



        

        private void Start()
        {
            BuildBlendshapeIndex();
            BuildEyeBlendshapeIndex();  // ← ADDED


            if (aliveSystem == null) aliveSystem = GetComponent<AliveSystem>();
            if (lipSyncBridge == null) lipSyncBridge = GetComponent<StreamojiLipSyncBridge>();



            ForceResetAllBlendshapes();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            aliveSystem?.Tick(dt);
            UpdateTransition(dt);
            UpdateHoldTimer(dt);
            UpdateBrowFlash(dt); // ← ADD
            UpdateTalkingGestureState();  // ← ADD

            ApplyToMesh();

            if (headMotion != null && aliveSystem != null)
                headMotion.ApplyMicroOffset(aliveSystem.HeadMicroOffset);
        }



        private void UpdateBrowFlash(float dt)
        {
            if (!_browFlashActive) return;

            _browFlashTimer += dt;
            float t = _browFlashTimer / _browFlashDuration;

            if (t >= 1f)
            {
                _browFlashActive = false;
                _browFlashTimer = 0f;
                _browFlashStrength = 0f;
                return;
            }

            // Arc shape: rises fast, falls slow — like a real surprised micro-expression
            // Peak at t=0.25, back to 0 at t=1.0
            _browFlashStrength = Mathf.Sin(t * Mathf.PI) *
                                 Mathf.Lerp(0.30f, 0.08f, Mathf.SmoothStep(0f, 1f, t));
        }


        // ══════════════════════════════════════════════
        // PUBLIC API — unchanged
        // ══════════════════════════════════════════════

        public void SetEmotion(EmotionVAD emotion)
        {
            _currentEmotion = emotion;

            // ══════ NETWORK SYNC ══════
            networkSync?.SyncEmotion(emotion);

            if (emotion.IsNeutral)
            {
                if (headMotion != null)
                {
                    headMotion.TriggerTalkingGesture();
                    networkSync?.SyncHeadGesture(
                        AvatarNetworkSync.HeadGestureType.TalkingGesture);
                    Debug.Log("[Face] Head → TALKING GESTURE (neutral message)");
                }

                _targetPose = FacialPose.Neutral;
                _isReturningToNeutral = false;
                _holdUntilAudioDone = true;
                _holdDuration = 3f;
                _holdTimer = 0f;
                return;
            }

            _targetPose = faceMapper.MapToFace(emotion);
            bodyController?.SetCurrentEmotion(emotion);
            _isReturningToNeutral = false;
            _holdDuration = emotion.GetHoldDuration();
            _holdTimer = 0f;
            _holdUntilAudioDone = true;

            bool bodyAnimPlaying = false;
            if (bodyController != null)
            {
                string bodyAnim = faceMapper.GetBodyAnimation(emotion);
                if (bodyAnim != null)
                {
                    bodyController.PlayAnimation(bodyAnim);
                    float returnDelay = _holdDuration + 0.5f;
                    bodyController.ReturnToIdle(returnDelay);
                    bodyAnimPlaying = true;

                    // ══════ NETWORK: Sync body animation ══════
                    networkSync?.SyncBodyAnimation(bodyAnim, returnDelay);
                    Debug.Log($"[Face] Body animation: {bodyAnim}");
                }
            }

            if (headMotion != null)
            {
                if (bodyAnimPlaying)
                {
                    float suppressDuration = _holdDuration + 0.5f;
                    headMotion.SuppressDuringBodyAnim(suppressDuration);

                    // ══════ NETWORK: Sync head suppression ══════
                    networkSync?.SyncHeadGesture(
                        AvatarNetworkSync.HeadGestureType.Suppress,
                        suppressDuration);
                    Debug.Log("[Face] Head SUPPRESSED (body anim)");
                }
                else
                {
                    // ══════ NETWORK: Sync head gestures ══════
                    if (emotion.ShouldShake)
                    {
                        headMotion.TriggerShake();
                        networkSync?.SyncHeadGesture(
                            AvatarNetworkSync.HeadGestureType.Shake);
                        Debug.Log("[Face] Head → SHAKE");
                    }
                    else if (emotion.ShouldLookDown)
                    {
                        headMotion.TriggerLookDown();
                        networkSync?.SyncHeadGesture(
                            AvatarNetworkSync.HeadGestureType.LookDown);
                        Debug.Log("[Face] Head → LOOK DOWN");
                    }
                    else if (emotion.ShouldNod)
                    {
                        headMotion.TriggerNod();
                        networkSync?.SyncHeadGesture(
                            AvatarNetworkSync.HeadGestureType.Nod);
                        Debug.Log("[Face] Head → NOD");
                    }
                    else if (emotion.ShouldTilt)
                    {
                        float direction = Random.value > 0.5f ? 1f : -1f;
                        headMotion.TriggerTilt(direction);
                        networkSync?.SyncHeadGesture(
                            AvatarNetworkSync.HeadGestureType.Tilt, direction);
                        Debug.Log("[Face] Head → TILT");
                    }
                    else
                    {
                        headMotion.TriggerTalkingGesture();
                        networkSync?.SyncHeadGesture(
                            AvatarNetworkSync.HeadGestureType.TalkingGesture);
                        Debug.Log("[Face] Head → TALKING GESTURE");
                    }
                }
            }

            if (!emotion.IsNeutral)
            {
                _browFlashStrength = Mathf.Lerp(0.12f, 0.30f, emotion.Magnitude);
                _browFlashTimer = 0f;
                _browFlashActive = true;
            }

            Debug.Log($"[Face] Emotion: {emotion} → {_targetPose.ActiveCount} blendshapes");
        }


        private void UpdateTalkingGestureState()
        {
            bool audioPlaying = lipSyncBridge != null && lipSyncBridge.IsActive;

            if (audioPlaying && !_wasAudioPlaying)
            {
                bodyController?.StartTalkingGestures();

                // ══════ NETWORK: Sync talking started ══════
                networkSync?.SyncTalkingState(true);
                Debug.Log("[Face] Audio started → talking gestures on");
            }
            else if (!audioPlaying && _wasAudioPlaying)
            {
                bodyController?.StopTalkingGesturesGracefully();

                // ══════ NETWORK: Sync talking stopped ══════
                networkSync?.SyncTalkingState(false);
                Debug.Log("[Face] Audio stopped → talking gestures graceful stop");
            }

            _wasAudioPlaying = audioPlaying;
        }




        public void ReturnToNeutral()
        {
            _targetPose = FacialPose.Neutral;
            _isReturningToNeutral = true;
            Debug.Log("[Face] Returning to neutral");
        }




        public void Interrupt()
        {
            _targetPose = FacialPose.Neutral;
            _currentPose = FacialPose.Neutral;
            _isReturningToNeutral = true;
            _holdTimer = 0;
            _holdDuration = 0;
            _holdUntilAudioDone = false;
            bodyController?.InterruptAnimation();
            ForceResetAllBlendshapes();
        }

        public EmotionVAD GetCurrentEmotion() => _currentEmotion;

        // ══════════════════════════════════════════════
        // TRANSITION — unchanged
        // ══════════════════════════════════════════════

        private void UpdateTransition(float dt)
        {
            float speed = _isReturningToNeutral ? emotionFadeSpeed : emotionOnsetSpeed;
            _currentPose = FacialPose.Lerp(_currentPose, _targetPose, Mathf.Clamp01(speed * dt));
        }



        // ══════════════════════════════════════════════
        // APPLY TO MESH — CHANGED
        // ══════════════════════════════════════════════


        private void UpdateHoldTimer(float dt)
        {
            if (_isReturningToNeutral) return;
            if (_holdDuration <= 0) return;

            if (_holdUntilAudioDone)
            {
                // FIXED: && not ||
                bool audioPlaying = lipSyncBridge != null && lipSyncBridge.IsActive;

                if (audioPlaying)
                {
                    _holdTimer = 0f;
                    return;
                }
                else
                {
                    _holdUntilAudioDone = false;
                    _holdTimer = 0f;
                    Debug.Log($"[Face] Audio done, holding for {_holdDuration:F1}s more");
                }
            }

            _holdTimer += dt;
            if (_holdTimer >= _holdDuration)
                ReturnToNeutral();
        }



        private void ApplyToMesh()
        {
            if (faceMesh == null) return;

            bool lipSyncActive = lipSyncBridge != null && lipSyncBridge.IsActive;

            if (_wasLipSyncing && !lipSyncActive)
                lipSyncBridge?.ResetMouth();
            _wasLipSyncing = lipSyncActive;

            var activeThisFrame = new HashSet<string>();

            // Step 1: Apply emotion blendshapes
            foreach (var kvp in _currentPose.weights)
            {
                if (kvp.Value < 0.001f) continue;

                if (lipSyncActive && LipSyncDriver.ControlledBlendshapes.Contains(kvp.Key))
                    continue;

                // Skip legacy RPM combined shapes
                if (kvp.Key == "mouthOpen" || kvp.Key == "mouthSmile" ||
                    kvp.Key == "eyesClosed" || kvp.Key == "eyesLookUp" ||
                    kvp.Key == "eyesLookDown") continue;

                // ── CHANGED: route eye look to eye meshes ──
                if (EyeLeftBlendshapes.Contains(kvp.Key))
                {
                    SetEyeBlend(_eyeLeftIndex, eyeLeftMesh, kvp.Key, kvp.Value);
                    activeThisFrame.Add(kvp.Key);
                    continue;
                }

                if (EyeRightBlendshapes.Contains(kvp.Key))
                {
                    SetEyeBlend(_eyeRightIndex, eyeRightMesh, kvp.Key, kvp.Value);
                    activeThisFrame.Add(kvp.Key);
                    continue;
                }

                // Everything else goes to face mesh as before
                SetBlend(kvp.Key, kvp.Value);
                activeThisFrame.Add(kvp.Key);
            }

            // Step 2: Reset blendshapes that were active but aren't now
            foreach (string prevActive in _activeLastFrame)
            {
                if (activeThisFrame.Contains(prevActive)) continue;
                if (lipSyncActive && LipSyncDriver.ControlledBlendshapes.Contains(prevActive)) continue;
                if (IsAliveControlled(prevActive)) continue;

                // ── CHANGED: reset on correct mesh ──
                if (EyeLeftBlendshapes.Contains(prevActive))
                    SetEyeBlend(_eyeLeftIndex, eyeLeftMesh, prevActive, 0f);
                else if (EyeRightBlendshapes.Contains(prevActive))
                    SetEyeBlend(_eyeRightIndex, eyeRightMesh, prevActive, 0f);
                else
                    SetBlend(prevActive, 0f);
            }

            _activeLastFrame = activeThisFrame;

            // Step 3: Alive system
            ApplyAliveSystem(lipSyncActive);

            // Step 4: LipSync

        }


        private void ApplyAliveSystem(bool lipSyncActive)
        {
            if (aliveSystem == null) return;

            // Blinks — face mesh, unchanged
            if (aliveSystem.IsBlinking)
            {
                SetBlend("eyeBlinkLeft", aliveSystem.BlinkLeft);
                SetBlend("eyeBlinkRight", aliveSystem.BlinkRight);

                if (_idx_eyesClosed >= 0)
                {
                    float both = (aliveSystem.BlinkLeft + aliveSystem.BlinkRight) * 0.5f;
                    faceMesh.SetBlendShapeWeight(_idx_eyesClosed, both);
                }
            }
            else
            {
                SetBlend("eyeBlinkLeft", 0f);
                SetBlend("eyeBlinkRight", 0f);
                if (_idx_eyesClosed >= 0)
                    faceMesh.SetBlendShapeWeight(_idx_eyesClosed, 0f);
            }

            // Gaze — CHANGED: route to eye meshes
            bool emotionDrivesGaze = _currentPose.weights.ContainsKey("eyeLookUpLeft") ||
                                     _currentPose.weights.ContainsKey("eyeLookDownLeft") ||
                                     _currentPose.weights.ContainsKey("eyeLookInLeft") ||
                                     _currentPose.weights.ContainsKey("eyeLookOutLeft");

            if (!emotionDrivesGaze)
            {
                // Left eye → eyeLeftMesh
                SetEyeBlend(_eyeLeftIndex, eyeLeftMesh, "eyeLookUpLeft", aliveSystem.EyeLookUpLeft);
                SetEyeBlend(_eyeLeftIndex, eyeLeftMesh, "eyeLookDownLeft", aliveSystem.EyeLookDownLeft);
                SetEyeBlend(_eyeLeftIndex, eyeLeftMesh, "eyeLookInLeft", aliveSystem.EyeLookInLeft);
                SetEyeBlend(_eyeLeftIndex, eyeLeftMesh, "eyeLookOutLeft", aliveSystem.EyeLookOutLeft);

                // Right eye → eyeRightMesh
                SetEyeBlend(_eyeRightIndex, eyeRightMesh, "eyeLookUpRight", aliveSystem.EyeLookUpRight);
                SetEyeBlend(_eyeRightIndex, eyeRightMesh, "eyeLookDownRight", aliveSystem.EyeLookDownRight);
                SetEyeBlend(_eyeRightIndex, eyeRightMesh, "eyeLookInRight", aliveSystem.EyeLookInRight);
                SetEyeBlend(_eyeRightIndex, eyeRightMesh, "eyeLookOutRight", aliveSystem.EyeLookOutRight);

                // RPM combined — still on face mesh
                if (_idx_eyesLookUp >= 0)
                    faceMesh.SetBlendShapeWeight(_idx_eyesLookUp, aliveSystem.EyesLookUp);
                if (_idx_eyesLookDown >= 0)
                    faceMesh.SetBlendShapeWeight(_idx_eyesLookDown, aliveSystem.EyesLookDown);
            }

            bool emotionDrivesBrows =
        _currentPose.weights.TryGetValue("browInnerUp", out float emotionBrowVal) &&
        emotionBrowVal > 0.15f;

            if (!emotionDrivesBrows)
            {
                // Drift from AliveSystem
                float driftL = aliveSystem.BrowInnerUpLeft;
                float driftR = aliveSystem.BrowInnerUpRight;

                // Flash adds on top of drift
                float flashL = _browFlashActive ? _browFlashStrength : 0f;
                float flashR = _browFlashActive ? _browFlashStrength * 0.92f : 0f;  // slight asymmetry

                SetBlend("browInnerUp", Mathf.Clamp01(driftL + flashL));
                SetBlend("browOuterUpLeft", Mathf.Clamp01(driftL * 0.4f + flashL * 0.5f));
                SetBlend("browOuterUpRight", Mathf.Clamp01(driftR * 0.4f + flashR * 0.5f));
            }
            else
            {
                // Emotion is driving brows — just add flash on top, clamped
                float flashAdd = _browFlashActive ? _browFlashStrength * 0.5f : 0f;
                if (flashAdd > 0.01f)
                {
                    float current = _currentPose.weights.TryGetValue("browInnerUp", out float v) ? v : 0f;
                    SetBlend("browInnerUp", Mathf.Clamp01(current + flashAdd));
                }
            }


        }

        private bool IsAliveControlled(string name)
        {
            return name == "eyeBlinkLeft" || name == "eyeBlinkRight" ||
                   name == "eyesClosed" ||
                   name.StartsWith("eyeLookUp") || name.StartsWith("eyeLookDown") ||
                   name.StartsWith("eyeLookIn") || name.StartsWith("eyeLookOut") ||
                   name == "eyesLookUp" || name == "eyesLookDown";
        }

        // ══════════════════════════════════════════════
        // BLENDSHAPE HELPERS — CHANGED
        // ══════════════════════════════════════════════

        // Face mesh — unchanged
        private void SetBlend(string name, float weight01)
        {
            if (_blendIndex.TryGetValue(name, out int idx))
                faceMesh.SetBlendShapeWeight(idx, weight01);
        }

        // Eye meshes — ADDED
        private void SetEyeBlend(
            Dictionary<string, int> index,
            SkinnedMeshRenderer mesh,
            string name,
            float weight01)
        {
            if (mesh == null) return;
            if (index.TryGetValue(name, out int idx))
                mesh.SetBlendShapeWeight(idx, weight01);
        }

        private void ForceResetAllBlendshapes()
        {
            if (faceMesh != null && faceMesh.sharedMesh != null)
            {
                int count = faceMesh.sharedMesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                    faceMesh.SetBlendShapeWeight(i, 0f);
            }

            // ADDED: also reset eye meshes
            if (eyeLeftMesh != null && eyeLeftMesh.sharedMesh != null)
            {
                int count = eyeLeftMesh.sharedMesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                    eyeLeftMesh.SetBlendShapeWeight(i, 0f);
            }

            if (eyeRightMesh != null && eyeRightMesh.sharedMesh != null)
            {
                int count = eyeRightMesh.sharedMesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                    eyeRightMesh.SetBlendShapeWeight(i, 0f);
            }

            _activeLastFrame.Clear();
            Debug.Log("[Face] Force reset all blendshapes");
        }

        private void BuildBlendshapeIndex()
        {
            _blendIndex.Clear();
            if (faceMesh == null || faceMesh.sharedMesh == null)
            {
                Debug.LogError("[Face] No face mesh!");
                return;
            }

            var mesh = faceMesh.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (name.Contains("."))
                    name = name.Substring(name.LastIndexOf('.') + 1);
                _blendIndex[name] = i;
            }

            _blendIndex.TryGetValue("mouthOpen", out _idx_mouthOpen);
            _blendIndex.TryGetValue("mouthSmile", out _idx_mouthSmile);
            _idx_eyesClosed = _blendIndex.TryGetValue("eyesClosed", out int ec) ? ec : -1;
            _idx_eyesLookUp = _blendIndex.TryGetValue("eyesLookUp", out int elu) ? elu : -1;
            _idx_eyesLookDown = _blendIndex.TryGetValue("eyesLookDown", out int eld) ? eld : -1;
            _idx_eyeBlinkLeft = _blendIndex.TryGetValue("eyeBlinkLeft", out int ebl) ? ebl : -1;
            _idx_eyeBlinkRight = _blendIndex.TryGetValue("eyeBlinkRight", out int ebr) ? ebr : -1;

            Debug.Log($"[Face] Indexed {_blendIndex.Count} face blendshapes");
        }

        // ADDED: build index for both eye meshes
        private void BuildEyeBlendshapeIndex()
        {
            BuildEyeMeshIndex(eyeLeftMesh, _eyeLeftIndex, "EyeLeft");
            BuildEyeMeshIndex(eyeRightMesh, _eyeRightIndex, "EyeRight");
        }

        private void BuildEyeMeshIndex(
            SkinnedMeshRenderer mesh,
            Dictionary<string, int> index,
            string debugName)
        {
            index.Clear();
            if (mesh == null || mesh.sharedMesh == null)
            {
                Debug.LogWarning($"[Face] No mesh for {debugName}");
                return;
            }

            var smr = mesh.sharedMesh;
            for (int i = 0; i < smr.blendShapeCount; i++)
            {
                string name = smr.GetBlendShapeName(i);
                if (name.Contains("."))
                    name = name.Substring(name.LastIndexOf('.') + 1);
                index[name] = i;
            }

            Debug.Log($"[Face] Indexed {index.Count} blendshapes on {debugName}");
        }
    }
}