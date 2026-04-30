using System.Collections;
using UnityEngine;






namespace Mikk.Avatar
{
    public class BodyAnimationController : MonoBehaviour
    {
        [Header("Animator")]
        [SerializeField] public Animator animator;
        [SerializeField] private AvatarNetworkSync networkSync;


        [Header("Layer Settings")]
        [SerializeField] private string upperBodyLayerName = "UpperBody";
        [SerializeField] private float crossfadeDuration = 0.25f;
        [SerializeField] private string idleStateName = "Idle";

        [Header("Breathing Sway")]
        [SerializeField] public Transform spineBone;
        [SerializeField] public Transform spine1Bone;
        [SerializeField] public Transform spine2Bone;
        [SerializeField, Range(0f, 2f)] private float breathingSpeed = 0.9f;
        [SerializeField, Range(0f, 0.5f)] private float breathingDepth = 0.18f;
        [SerializeField, Range(0f, 0.5f)] private float swayIntensity = 0.08f;
        [SerializeField, Range(0f, 0.5f)] private float swaySpeed = 0.25f;

        [Header("Clip Gesture Settings")]
        [SerializeField, Range(0.5f, 4f)] private float gestureMinInterval = 1.0f;
        [SerializeField, Range(2f, 8f)] private float gestureMaxInterval = 3.0f;
        [SerializeField, Range(0f, 1f)] private float gestureChance = 0.65f;
        [SerializeField] private float MIN_AUDIO_FOR_GESTURE = 3.5f;

        [Header("Procedural Talking Gestures")]
        [SerializeField] public Transform leftArmBone;
        [SerializeField] public Transform rightArmBone;
        [SerializeField] public Transform leftForeArmBone;
        [SerializeField] public Transform rightForeArmBone;
        [SerializeField] public Transform leftHandBone;
        [SerializeField] public Transform rightHandBone;
        [SerializeField, Range(0f, 1f)] private float gestureBaseScale = 0.5f;
        [SerializeField, Range(0f, 1f)] private float gestureEmotionScale = 0.9f;
        [SerializeField, Range(0f, 2f)] private float gestureSpeed = 1.0f;
        [SerializeField, Range(0f, 0.5f)] private float gestureAsymmetry = 0.3f;

        [Header("Intent Gesture Settings")]
        [SerializeField, Range(0f, 1f)] private float intentBlendStrength = 0.75f;
        [SerializeField] private float hintDuration = 5f;

        // ── Clip gesture pools ────────────────────────────────────────────
        private static readonly string[] GesturesNeutral = { };
        private static readonly string[] GesturesSad = { "shrug" };
        private static readonly string[] GesturesAngry = { "head_shake_no" };
        private static readonly string[] GesturesConfused = { "shrug" };
        // ── Core state ────────────────────────────────────────────────────
        private int _upperBodyLayer = -1;
        private bool _isPlayingEmotionAnim = false;
        private bool _isTalking = false;
        private EmotionVAD _currentEmotion = EmotionVAD.Neutral;
        private float _talkingStartTime = 0f;

        private Coroutine _returnToIdleCoroutine;
        private Coroutine _talkingGestureCoroutine;

        // ── Breathing sway state ──────────────────────────────────────────
        private float _breathTime = 0f;
        private float _swayTimeX = 0f;
        private float _swayTimeZ = 0f;
        private Quaternion _spineRest;
        private Quaternion _spine1Rest;
        private Quaternion _spine2Rest;
        private bool _restCaptured = false;
        private float _suppressSwayTimer = 0f;

        // ── Gesture hint state ────────────────────────────────────────────
        private GestureHint _currentHint = GestureHint.None;
        private float _hintDecayTimer = 0f;

        // ── Procedural gesture state ──────────────────────────────────────
        private enum GesturePhase { Idle, Prepare, Stroke, Hold, Retract }

        private GesturePhase _leftPhase = GesturePhase.Idle;
        private GesturePhase _rightPhase = GesturePhase.Idle;
        private float _leftPhaseTimer = 0f;
        private float _rightPhaseTimer = 0f;
        private float _leftPhaseDur = 0f;
        private float _rightPhaseDur = 0f;
        private Vector3 _leftStrokeTarget;
        private Vector3 _rightStrokeTarget;
        private Vector3 _leftCurrent;
        private Vector3 _rightCurrent;
        private float _leftNextGesture = 0f;
        private float _rightNextGesture = 0f;
        private float _leftWristNoise = 0f;
        private float _rightWristNoise = 0f;

        private Quaternion _leftArmRest;
        private Quaternion _rightArmRest;
        private Quaternion _leftForeArmRest;
        private Quaternion _rightForeArmRest;
        private Quaternion _leftHandRest;
        private Quaternion _rightHandRest;
        private bool _armRestCaptured = false;

        // ═════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═════════════════════════════════════════════════════════════════

        private void Start()
        {
            if (animator == null) animator = GetComponent<Animator>();
            _upperBodyLayer = animator.GetLayerIndex(upperBodyLayerName);

            _swayTimeX = Random.Range(0f, 100f);
            _swayTimeZ = _swayTimeX + Random.Range(15f, 40f);
            _breathTime = Random.Range(0f, Mathf.PI * 2f);
        }

        private void LateUpdate()
        {
            if (!_restCaptured) TryCaptureRestPose();

            UpdateHintDecay();
            UpdateBreathingSway();
            UpdateProceduralGestures();
        }

        // ═════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═════════════════════════════════════════════════════════════════

        public void SetCurrentEmotion(EmotionVAD emotion)
        {
            _currentEmotion = emotion;
        }

        public void SetGestureHint(GestureHint hint)
        {
            _currentHint = hint;
            _hintDecayTimer = hintDuration;

            // ══════ NETWORK: Sync gesture hint ══════
            networkSync?.SyncGestureHint(hint);

            if (hint != GestureHint.None && _isTalking)
            {
                if (_leftPhase == GesturePhase.Idle)
                    _leftNextGesture = 0f;
                if (_rightPhase == GesturePhase.Idle)
                    _rightNextGesture = Mathf.Min(_rightNextGesture, 0.15f);
            }

            Debug.Log($"[Body] Gesture hint set: {hint}");
        }

        public void StartTalkingGestures()
        {
            if (_isTalking) return;
            _isTalking = true;
            _talkingStartTime = Time.time;

            if (_talkingGestureCoroutine != null)
                StopCoroutine(_talkingGestureCoroutine);

            _talkingGestureCoroutine = StartCoroutine(TalkingGestureLoop());
            Debug.Log("[Body] Talking gestures started");
        }

        public void StopTalkingGesturesGracefully()
        {
            if (!_isTalking) return;
            _isTalking = false;
            Debug.Log("[Body] Talking gestures graceful stop");
        }

        public void StopTalkingGesturesImmediate()
        {
            if (!_isTalking) return;
            _isTalking = false;

            if (_talkingGestureCoroutine != null)
            {
                StopCoroutine(_talkingGestureCoroutine);
                _talkingGestureCoroutine = null;
            }

            if (!_isPlayingEmotionAnim) ReturnToIdleImmediate();
            Debug.Log("[Body] Talking gestures stopped immediately");
        }

        public void PlayAnimation(string animationName)
        {
            if (string.IsNullOrEmpty(animationName) || animator == null) return;

            if (_talkingGestureCoroutine != null)
            {
                StopCoroutine(_talkingGestureCoroutine);
                _talkingGestureCoroutine = null;
            }

            if (_returnToIdleCoroutine != null)
                StopCoroutine(_returnToIdleCoroutine);

            _isPlayingEmotionAnim = true;
            _suppressSwayTimer = 1.5f;

            int layer = _upperBodyLayer >= 0 ? _upperBodyLayer : 0;
            animator.CrossFade(animationName, crossfadeDuration, layer);

            Debug.Log($"[Body] Emotion animation: {animationName}");
        }

        public void ReturnToIdle(float delay)
        {
            if (_returnToIdleCoroutine != null)
                StopCoroutine(_returnToIdleCoroutine);
            _returnToIdleCoroutine = StartCoroutine(ReturnToIdleCoroutine(delay));
        }

        public void InterruptAnimation()
        {
            if (_returnToIdleCoroutine != null)
                StopCoroutine(_returnToIdleCoroutine);

            if (_talkingGestureCoroutine != null)
            {
                StopCoroutine(_talkingGestureCoroutine);
                _talkingGestureCoroutine = null;
            }

            _isTalking = false;
            _isPlayingEmotionAnim = false;
            _currentHint = GestureHint.None;
            _hintDecayTimer = 0f;

            if (animator != null)
            {
                int layer = _upperBodyLayer >= 0 ? _upperBodyLayer : 0;
                animator.CrossFade(idleStateName, 0.2f, layer);
            }
        }

        public bool IsPlayingAnimation => _isPlayingEmotionAnim;

        // ═════════════════════════════════════════════════════════════════
        // HINT DECAY
        // ═════════════════════════════════════════════════════════════════

        private void UpdateHintDecay()
        {
            if (_hintDecayTimer > 0f)
            {
                _hintDecayTimer -= Time.deltaTime;
                if (_hintDecayTimer <= 0f)
                {
                    _currentHint = GestureHint.None;
                    _hintDecayTimer = 0f;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // CLIP GESTURE LOOP — unchanged
        // ═════════════════════════════════════════════════════════════════

        private IEnumerator TalkingGestureLoop()
        {
            while (_isTalking)
            {
                float interval = Random.Range(gestureMinInterval, gestureMaxInterval);
                yield return new WaitForSeconds(interval);

                if (!_isTalking)
                {
                    ReturnToIdleIfNeeded();
                    _talkingGestureCoroutine = null;
                    yield break;
                }

                float audioAge = Time.time - _talkingStartTime;
                if (audioAge < MIN_AUDIO_FOR_GESTURE)
                {
                    Debug.Log($"[Body] Clip skipped — audio {audioAge:F1}s old");
                    continue;
                }

                if (_isPlayingEmotionAnim) continue;
                if (Random.value > gestureChance) continue;

                string gesture = PickGesture(_currentEmotion);
                if (gesture == null) continue;

                int layer = _upperBodyLayer >= 0 ? _upperBodyLayer : 0;
                animator.CrossFade(gesture, crossfadeDuration, layer);
                Debug.Log($"[Body] Clip gesture: {gesture}");

                float hold = Random.Range(1.2f, 2.5f);
                yield return new WaitForSeconds(hold);

                if (_isTalking && !_isPlayingEmotionAnim)
                    animator.CrossFade(idleStateName, crossfadeDuration, layer);
            }

            ReturnToIdleIfNeeded();
            _talkingGestureCoroutine = null;
            Debug.Log("[Body] TalkingGestureLoop ended");
        }

        private string PickGesture(EmotionVAD vad)
        {
            string[] pool;

            if (vad.Valence < -0.3f && vad.Dominance > 0.2f) pool = GesturesAngry;
            else if (vad.Valence < -0.2f && vad.Arousal < 0.1f) pool = GesturesSad;
            else if (vad.Dominance < -0.2f && Mathf.Abs(vad.Valence) < 0.3f) pool = GesturesConfused;
            else pool = GesturesNeutral;

            if (pool.Length == 0) return null;
            return pool[Random.Range(0, pool.Length)];
        }

        // ═════════════════════════════════════════════════════════════════
        // PROCEDURAL TALKING GESTURES — UPDATED WITH HINT AWARENESS
        // ═════════════════════════════════════════════════════════════════

        private void UpdateProceduralGestures()
        {
            if (!_armRestCaptured) return;
            if (leftArmBone == null || rightArmBone == null) return;

            // Suppress during clip-based animations
            if (_suppressSwayTimer > 0f)
            {
                _leftCurrent = Vector3.Lerp(_leftCurrent, Vector3.zero, Time.deltaTime * 3f);
                _rightCurrent = Vector3.Lerp(_rightCurrent, Vector3.zero, Time.deltaTime * 3f);
                ApplyArmRotations();
                return;
            }

            // ── Compute intensity from VAD ────────────────────────────────
            float emotionBoost = 0f;
            if (_isTalking)
            {
                float arousalBoost = Mathf.Clamp01((_currentEmotion.Arousal + 1f) / 2f);
                float valenceBoost = Mathf.Abs(_currentEmotion.Valence);
                emotionBoost = Mathf.Lerp(0f, 1f, (arousalBoost + valenceBoost) / 2f);
            }

            // Hints boost minimum intensity so gestures are visible
            float hintBoost = (_currentHint != GestureHint.None && _hintDecayTimer > 0f)
                ? 0.15f : 0f;

            float intensity = Mathf.Lerp(
                _isTalking ? gestureBaseScale * 0.4f + hintBoost : 0.05f,
                gestureEmotionScale,
                emotionBoost
            );

            // Ensure minimum gesture visibility when hint is active
            if (_currentHint != GestureHint.None && _hintDecayTimer > 0f)
                intensity = Mathf.Max(intensity, gestureBaseScale * 0.6f);

            float dt = Time.deltaTime * gestureSpeed;

            // ── Update each arm independently ─────────────────────────────
            UpdateArmCycle(
                ref _leftPhase, ref _leftPhaseTimer, ref _leftPhaseDur,
                ref _leftNextGesture, ref _leftStrokeTarget, ref _leftCurrent,
                intensity, dt, isLeft: true
            );

            UpdateArmCycle(
                ref _rightPhase, ref _rightPhaseTimer, ref _rightPhaseDur,
                ref _rightNextGesture, ref _rightStrokeTarget, ref _rightCurrent,
                intensity * (1f - gestureAsymmetry * 0.5f), dt, isLeft: false
            );

            // ── Wrist follow-through ──────────────────────────────────────
            UpdateWristFollow(dt, intensity);

            ApplyArmRotations();
        }

        private void UpdateArmCycle(
            ref GesturePhase phase,
            ref float timer,
            ref float phaseDur,
            ref float nextGesture,
            ref Vector3 strokeTarget,
            ref Vector3 current,
            float intensity,
            float dt,
            bool isLeft)
        {
            timer += dt;

            switch (phase)
            {
                case GesturePhase.Idle:
                    nextGesture -= dt;

                    // Tiny alive drift in idle
                    float idleNoise = isLeft
                        ? Mathf.PerlinNoise(Time.time * 0.3f, 0f)
                        : Mathf.PerlinNoise(0f, Time.time * 0.3f);
                    Vector3 idleDrift = new Vector3(
                        (idleNoise - 0.5f) * 2f * 0.03f * intensity,
                        0f,
                        (idleNoise - 0.5f) * 0.8f * intensity
                    );
                    current = Vector3.Lerp(current, idleDrift, dt * 2f);

                    if (nextGesture <= 0f && _isTalking)
                    {
                        strokeTarget = PickStrokeTarget(intensity, isLeft);
                        phaseDur = GetPrepareDuration();
                        timer = 0f;
                        phase = GesturePhase.Prepare;
                    }
                    break;

                case GesturePhase.Prepare:
                    current = Vector3.Lerp(current, strokeTarget * 0.3f, dt * 4f);
                    if (timer >= phaseDur)
                    {
                        phaseDur = GetStrokeDuration();
                        timer = 0f;
                        phase = GesturePhase.Stroke;
                    }
                    break;

                case GesturePhase.Stroke:
                    float strokeSpeed = GetStrokeSpeed();
                    current = Vector3.Lerp(current, strokeTarget, dt * strokeSpeed);
                    if (timer >= phaseDur)
                    {
                        phaseDur = GetHoldDuration();
                        timer = 0f;
                        phase = GesturePhase.Hold;
                    }
                    break;

                case GesturePhase.Hold:
                    float holdNoise = isLeft
                        ? Mathf.PerlinNoise(Time.time * 1.5f, 10f)
                        : Mathf.PerlinNoise(10f, Time.time * 1.5f);

                    // Hint-specific hold behavior
                    Vector3 holdVariation;
                    if (_currentHint == GestureHint.Negation && _hintDecayTimer > 0f)
                    {
                        // Lateral oscillation for "no no" wave
                        float wave = Mathf.Sin(Time.time * 4f) * intensity * 3f;
                        holdVariation = strokeTarget + new Vector3(0f, wave, 0f);
                    }
                    else if (_currentHint == GestureHint.Listing && _hintDecayTimer > 0f)
                    {
                        // Sweep across during hold for counting
                        float sweep = Mathf.Sin(Time.time * 1.5f) * intensity * 5f;
                        holdVariation = strokeTarget + new Vector3(0f, sweep, 0f);
                    }
                    else
                    {
                        holdVariation = strokeTarget +
                            new Vector3(
                                (holdNoise - 0.5f) * 0.02f,
                                0f,
                                (holdNoise - 0.5f) * 0.02f);
                    }

                    current = Vector3.Lerp(current, holdVariation, dt * 3f);

                    if (timer >= phaseDur)
                    {
                        phaseDur = GetRetractDuration();
                        timer = 0f;
                        phase = GesturePhase.Retract;
                    }
                    break;

                case GesturePhase.Retract:
                    float retractSpeed = GetRetractSpeed();
                    current = Vector3.Lerp(current, Vector3.zero, dt * retractSpeed);
                    if (timer >= phaseDur && current.magnitude < 0.01f)
                    {
                        current = Vector3.zero;

                        if (_isTalking)
                        {
                            float arousalFactor = Mathf.Clamp01(_currentEmotion.Arousal);

                            // If hint is still active, shorter interval for follow-up
                            if (_currentHint != GestureHint.None && _hintDecayTimer > 0f)
                            {
                                nextGesture = Random.Range(0.3f, 0.8f);
                            }
                            else
                            {
                                float minInt = Mathf.Lerp(gestureMinInterval,
                                                        gestureMinInterval * 0.5f, arousalFactor);
                                float maxInt = Mathf.Lerp(gestureMaxInterval,
                                                        gestureMaxInterval * 0.6f, arousalFactor);
                                nextGesture = Random.Range(minInt, maxInt);
                            }
                        }
                        else
                        {
                            nextGesture = Random.Range(4f, 8f);
                        }

                        phase = GesturePhase.Idle;
                    }
                    break;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // HINT-AWARE TIMING
        // ═════════════════════════════════════════════════════════════════

        private float GetPrepareDuration()
        {
            if (_currentHint == GestureHint.None || _hintDecayTimer <= 0f)
                return Random.Range(0.15f, 0.3f);

            switch (_currentHint)
            {
                case GestureHint.Emphasis:
                case GestureHint.Dismissal:
                    return Random.Range(0.08f, 0.15f);    // fast onset

                case GestureHint.Calming:
                case GestureHint.Thinking:
                    return Random.Range(0.3f, 0.5f);      // slow, deliberate

                case GestureHint.Celebrating:
                    return Random.Range(0.1f, 0.18f);     // quick burst

                default:
                    return Random.Range(0.15f, 0.3f);
            }
        }

        private float GetStrokeDuration()
        {
            if (_currentHint == GestureHint.None || _hintDecayTimer <= 0f)
                return Random.Range(0.2f, 0.5f);

            switch (_currentHint)
            {
                case GestureHint.Emphasis:
                    return Random.Range(0.12f, 0.25f);    // sharp beat

                case GestureHint.Calming:
                    return Random.Range(0.4f, 0.7f);      // slow press

                case GestureHint.Pointing:
                    return Random.Range(0.2f, 0.35f);     // direct

                case GestureHint.Uncertainty:
                    return Random.Range(0.3f, 0.5f);      // open spread

                case GestureHint.Dismissal:
                    return Random.Range(0.1f, 0.2f);      // quick flick

                default:
                    return Random.Range(0.2f, 0.5f);
            }
        }

        private float GetStrokeSpeed()
        {
            if (_currentHint == GestureHint.None || _hintDecayTimer <= 0f)
                return 6f;

            switch (_currentHint)
            {
                case GestureHint.Emphasis:
                case GestureHint.Dismissal:
                case GestureHint.Celebrating:
                    return 9f;     // snappy

                case GestureHint.Calming:
                case GestureHint.Thinking:
                    return 3.5f;   // gentle

                case GestureHint.Negation:
                    return 7f;     // firm

                default:
                    return 6f;
            }
        }

        private float GetHoldDuration()
        {
            float arousalReduce = Mathf.Clamp01(_currentEmotion.Arousal) * 0.3f;

            if (_currentHint == GestureHint.None || _hintDecayTimer <= 0f)
                return Random.Range(0.3f, 0.8f) - arousalReduce;

            float holdBase;
            switch (_currentHint)
            {
                case GestureHint.Greeting:
                    holdBase = Random.Range(0.5f, 1.0f);     // held, welcoming
                    break;

                case GestureHint.Emphasis:
                    holdBase = Random.Range(0.1f, 0.25f);    // brief beat
                    break;

                case GestureHint.Uncertainty:
                    holdBase = Random.Range(0.6f, 1.2f);     // held open shrug
                    break;

                case GestureHint.Thinking:
                    holdBase = Random.Range(0.8f, 1.5f);     // contemplative
                    break;

                case GestureHint.Dismissal:
                    holdBase = Random.Range(0.05f, 0.15f);   // barely holds
                    break;

                case GestureHint.Calming:
                    holdBase = Random.Range(0.6f, 1.0f);     // sustained press
                    break;

                case GestureHint.Pointing:
                    holdBase = Random.Range(0.4f, 0.8f);     // held direction
                    break;

                case GestureHint.Listing:
                    holdBase = Random.Range(0.4f, 0.7f);     // each item
                    break;

                case GestureHint.Negation:
                    holdBase = Random.Range(0.5f, 0.9f);     // waving no
                    break;

                case GestureHint.Celebrating:
                    holdBase = Random.Range(0.3f, 0.6f);
                    break;

                case GestureHint.Storytelling:
                    holdBase = Random.Range(0.4f, 0.8f);
                    break;

                default:
                    holdBase = Random.Range(0.3f, 0.8f);
                    break;
            }

            return holdBase - arousalReduce;
        }

        private float GetRetractDuration()
        {
            if (_currentHint == GestureHint.None || _hintDecayTimer <= 0f)
                return Random.Range(0.25f, 0.5f);

            switch (_currentHint)
            {
                case GestureHint.Dismissal:
                    return Random.Range(0.15f, 0.25f);     // quick away

                case GestureHint.Calming:
                case GestureHint.Thinking:
                    return Random.Range(0.4f, 0.7f);       // slow return

                default:
                    return Random.Range(0.25f, 0.5f);
            }
        }

        private float GetRetractSpeed()
        {
            if (_currentHint == GestureHint.None || _hintDecayTimer <= 0f)
                return 3.5f;

            switch (_currentHint)
            {
                case GestureHint.Dismissal:
                case GestureHint.Emphasis:
                    return 5f;

                case GestureHint.Calming:
                case GestureHint.Thinking:
                    return 2.5f;

                default:
                    return 3.5f;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // STROKE TARGET — HINT AWARE
        // ═════════════════════════════════════════════════════════════════

        private Vector3 PickStrokeTarget(float intensity, bool isLeft)
        {
            if (_currentHint != GestureHint.None && _hintDecayTimer > 0f)
            {
                Vector3 intentTarget = GetIntentTarget(intensity, isLeft);
                Vector3 vadTarget = GetVADTarget(intensity, isLeft);

                // Blend fades as hint decays
                float hintStrength = Mathf.Clamp01(_hintDecayTimer / hintDuration);
                float blend = intentBlendStrength * hintStrength;

                Vector3 blended = Vector3.Lerp(vadTarget, intentTarget, blend);

                // Variation
                float noise = intensity * 2.5f;
                blended.x += Random.Range(-noise, noise);
                blended.z += Random.Range(-noise * 0.7f, noise * 0.7f);

                return blended;
            }

            return GetVADTarget(intensity, isLeft);
        }

        private Vector3 GetIntentTarget(float intensity, bool isLeft)
        {
            float side = isLeft ? -1f : 1f;
            float scale = intensity * 25f;

            switch (_currentHint)
            {
                case GestureHint.Greeting:
                    // Right hand: main wave/lift. Left: subtle mirror
                    if (!isLeft)
                        return new Vector3(
                            -scale * 0.1f,
                            scale * 0.08f * side,
                            scale * 0.2f * side
                        );
                    else
                        return Vector3.zero; 

/*                        return new Vector3(
                            -scale * 0.1f,
                            0f,
                            scale * 0.15f * side
                        );*/

                case GestureHint.Question:
                    // Both palms up, open — asking
                    return new Vector3(
                        -scale * 0.5f,
                        scale * 0.2f * side,
                        scale * 0.5f * side
                    );

                case GestureHint.Affirmation:
                    // Small confirming forward push
                    return new Vector3(
                        -scale * 0.55f,
                        scale * 0.05f * side,
                        scale * 0.15f * side
                    );

                case GestureHint.Negation:
                    // Lateral block / wave-off
                    return new Vector3(
                        -scale * 0.25f,
                        scale * 0.45f * side,
                        scale * 0.4f * side
                    );

                case GestureHint.Emphasis:
                    // Downward beat / chop
                    return new Vector3(
                        -scale * 0.55f,
                        scale * 0.1f * side,
                        -scale * 0.2f * side
                    );

                case GestureHint.Uncertainty:
                    // Wide palms up shrug
                    return new Vector3(
                        -scale * 0.3f,
                        scale * 0.5f * side,
                        scale * 0.65f * side
                    );

                case GestureHint.Calming:
                    // Palms down pressing
                    return new Vector3(
                        -scale * 0.45f,
                        scale * 0.25f * side,
                        -scale * 0.25f * side
                    );

                case GestureHint.Pointing:
                    // One hand forward deictic
                    if (!isLeft)
                        return new Vector3(
                            -scale * 0.85f,
                            scale * 0.05f * side,
                            scale * 0.25f * side
                        );
                    else
                        return new Vector3(
                            -scale * 0.15f,
                            0f,
                            scale * 0.1f * side
                        );

                case GestureHint.Listing:
                    // Sequential sweep
                    float listOffset = Mathf.Sin(Time.time * 1.5f) * scale * 0.3f;
                    return new Vector3(
                        -scale * 0.5f,
                        listOffset * side,
                        scale * 0.35f * side
                    );

                case GestureHint.Offering:
                    // Extend hand forward, palm up
                    if (!isLeft)
                        return new Vector3(
                            -scale * 0.7f,
                            scale * 0.1f * side,
                            scale * 0.35f * side
                        );
                    else
                        return new Vector3(
                            -scale * 0.1f,
                            0f,
                            scale * 0.1f * side
                        );

                case GestureHint.Dismissal:
                    // Quick outward flick
                    return new Vector3(
                        -scale * 0.2f,
                        scale * 0.6f * side,
                        scale * 0.1f * side
                    );

                case GestureHint.Explaining:
                    // Hands shaping ideas — moderate forward, moving
                    float explainPhase = Mathf.Sin(Time.time * 2f) * 0.15f;
                    return new Vector3(
                        -scale * (0.5f + explainPhase),
                        scale * 0.15f * side,
                        scale * (0.3f + explainPhase) * side
                    );

                case GestureHint.Celebrating:
                    // Fist pump / raised arms
                    return new Vector3(
                        -scale * 0.3f,
                        scale * 0.2f * side,
                        scale * 0.7f * side
                    );

                case GestureHint.Requesting:
                    // Gentle beckoning — hands toward self
                    return new Vector3(
                        -scale * 0.35f,
                        scale * 0.1f * side,
                        scale * 0.3f * side
                    );

                case GestureHint.Storytelling:
                    // Animated descriptive — varies over time
                    float storyX = Mathf.PerlinNoise(Time.time * 0.8f, isLeft ? 0f : 50f);
                    float storyZ = Mathf.PerlinNoise(isLeft ? 0f : 50f, Time.time * 0.8f);
                    return new Vector3(
                        -scale * (0.3f + storyX * 0.4f),
                        scale * (storyX - 0.5f) * 0.3f * side,
                        scale * storyZ * 0.5f * side
                    );

                case GestureHint.Thinking:
                    // Minimal movement — near chin/face contemplation
                    if (!isLeft)
                        return new Vector3(
                            -scale * 0.2f,
                            -scale * 0.1f * side,
                            scale * 0.35f * side
                        );
                    else
                        return new Vector3(
                            -scale * 0.05f,
                            0f,
                            scale * 0.05f * side
                        );

                default:
                    return Vector3.zero;
            }
        }

        private Vector3 GetVADTarget(float intensity, bool isLeft)
        {
            float side = isLeft ? -1f : 1f;
            float v = _currentEmotion.Valence;
            float a = _currentEmotion.Arousal;
            float d = _currentEmotion.Dominance;
            float scale = intensity * 25f;

            float baseX = -scale * 0.6f;
            float baseY = 0f;
            float baseZ = scale * 0.4f * side;

            if (v > 0.2f) { baseX += -scale * v * 0.4f; baseZ += scale * v * 0.3f * side; }
            if (a > 0.3f) { baseX += -scale * a * 0.3f; baseZ += scale * a * 0.2f * side; }
            if (d > 0.3f) { baseX += -scale * d * 0.2f; baseY = scale * d * 0.15f * side; }
            if (v < -0.2f) { baseX *= 0.5f; baseZ *= 0.6f; }

            baseX += Random.Range(-scale * 0.15f, scale * 0.15f);
            baseZ += Random.Range(-scale * 0.1f, scale * 0.1f);

            return new Vector3(baseX, baseY, baseZ);
        }

        // ═════════════════════════════════════════════════════════════════
        // WRIST, APPLY, BREATHING — unchanged from your original
        // ═════════════════════════════════════════════════════════════════

        private void UpdateWristFollow(float dt, float intensity)
        {
            if (leftHandBone == null || rightHandBone == null) return;

            _leftWristNoise += dt * 0.8f;
            _rightWristNoise += dt * 0.8f;

            float leftWristX = _leftCurrent.x * 0.3f +
                                 (Mathf.PerlinNoise(_leftWristNoise, 5f) - 0.5f) * intensity * 8f;
            float rightWristX = _rightCurrent.x * 0.3f +
                                 (Mathf.PerlinNoise(_rightWristNoise, 5f) - 0.5f) * intensity * 8f;
            float leftWristZ = (Mathf.PerlinNoise(_leftWristNoise + 30f, 0f) - 0.5f) * intensity * 6f;
            float rightWristZ = (Mathf.PerlinNoise(_rightWristNoise + 30f, 0f) - 0.5f) * intensity * 6f;

            // Hint-specific wrist behavior
            if (_currentHint == GestureHint.Question && _hintDecayTimer > 0f)
            {
                // Extra palm-up rotation for questioning
                leftWristX += intensity * 5f;
                rightWristX += intensity * 5f;
            }
            else if (_currentHint == GestureHint.Calming && _hintDecayTimer > 0f)
            {
                // Palms-down for calming
                leftWristX -= intensity * 6f;
                rightWristX -= intensity * 6f;
            }
            else if (_currentHint == GestureHint.Offering && _hintDecayTimer > 0f)
            {
                // Palm up, slight outward rotation
                if (!false) // right hand dominant
                    rightWristX += intensity * 8f;
            }

            leftHandBone.localRotation = _leftHandRest *
                Quaternion.Euler(leftWristX, 0f, leftWristZ);
            rightHandBone.localRotation = _rightHandRest *
                Quaternion.Euler(rightWristX, 0f, rightWristZ);
        }

        private void ApplyArmRotations()
        {
            if (leftArmBone != null)
                leftArmBone.localRotation = _leftArmRest * Quaternion.Euler(_leftCurrent);
            if (rightArmBone != null)
                rightArmBone.localRotation = _rightArmRest * Quaternion.Euler(_rightCurrent);

            if (leftForeArmBone != null)
                leftForeArmBone.localRotation = _leftForeArmRest *
                    Quaternion.Euler(_leftCurrent * 0.45f);
            if (rightForeArmBone != null)
                rightForeArmBone.localRotation = _rightForeArmRest *
                    Quaternion.Euler(_rightCurrent * 0.45f);
        }

        // ═════════════════════════════════════════════════════════════════
        // BREATHING SWAY — unchanged
        // ═════════════════════════════════════════════════════════════════

        private void TryCaptureRestPose()
        {
            if (spineBone == null || spine1Bone == null || spine2Bone == null) return;

            _spineRest = spineBone.localRotation;
            _spine1Rest = spine1Bone.localRotation;
            _spine2Rest = spine2Bone.localRotation;
            _restCaptured = true;

            if (leftArmBone != null) _leftArmRest = leftArmBone.localRotation;
            if (rightArmBone != null) _rightArmRest = rightArmBone.localRotation;
            if (leftForeArmBone != null) _leftForeArmRest = leftForeArmBone.localRotation;
            if (rightForeArmBone != null) _rightForeArmRest = rightForeArmBone.localRotation;
            if (leftHandBone != null) _leftHandRest = leftHandBone.localRotation;
            if (rightHandBone != null) _rightHandRest = rightHandBone.localRotation;

            if (leftArmBone != null && rightArmBone != null)
            {
                _armRestCaptured = true;
                _leftNextGesture = Random.Range(0.5f, 1.5f);
                _rightNextGesture = Random.Range(1.2f, 2.5f);
                _leftWristNoise = Random.Range(0f, 100f);
                _rightWristNoise = _leftWristNoise + Random.Range(20f, 50f);
            }

            Debug.Log("[Body] Rest pose captured");
        }

        private void UpdateBreathingSway()
        {
            if (!_restCaptured) return;
            if (spineBone == null || spine1Bone == null || spine2Bone == null) return;

            if (_suppressSwayTimer > 0f)
            {
                _suppressSwayTimer -= Time.deltaTime;
                return;
            }

            float dt = Time.deltaTime;
            _breathTime += dt * breathingSpeed;
            _swayTimeX += dt * swaySpeed;
            _swayTimeZ += dt * swaySpeed;

            float breathCycle = Mathf.Sin(_breathTime);
            float breathTilt = breathCycle * breathingDepth;

            float swayX = (Mathf.PerlinNoise(_swayTimeX, 7.3f) - 0.5f) * 2f * swayIntensity;
            float swayZ = (Mathf.PerlinNoise(3.1f, _swayTimeZ) - 0.5f) * 2f * swayIntensity * 0.5f;

            spineBone.localRotation = _spineRest * Quaternion.Euler(breathTilt * 2f, 0f, swayX * 0.5f);
            spine1Bone.localRotation = _spine1Rest * Quaternion.Euler(breathTilt * 1.5f, 0f, swayX);
            spine2Bone.localRotation = _spine2Rest * Quaternion.Euler(breathTilt, 0f, swayZ);
        }

        // ═════════════════════════════════════════════════════════════════
        // HELPERS — unchanged
        // ═════════════════════════════════════════════════════════════════

        private void ReturnToIdleIfNeeded()
        {
            if (!_isPlayingEmotionAnim && animator != null)
            {
                int layer = _upperBodyLayer >= 0 ? _upperBodyLayer : 0;
                animator.CrossFade(idleStateName, crossfadeDuration, layer);
            }
        }

        private void ReturnToIdleImmediate()
        {
            if (animator == null) return;
            int layer = _upperBodyLayer >= 0 ? _upperBodyLayer : 0;
            animator.CrossFade(idleStateName, crossfadeDuration, layer);
        }

        private IEnumerator ReturnToIdleCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);

            if (animator != null)
            {
                int layer = _upperBodyLayer >= 0 ? _upperBodyLayer : 0;
                animator.CrossFade(idleStateName, crossfadeDuration, layer);
            }

            _isPlayingEmotionAnim = false;
            _returnToIdleCoroutine = null;

            if (_isTalking)
                _talkingGestureCoroutine = StartCoroutine(TalkingGestureLoop());

            Debug.Log("[Body] Returned to idle");
        }
    }
}