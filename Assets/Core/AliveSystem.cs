using UnityEngine;



namespace Mikk.Avatar
{
    public class AliveSystem : MonoBehaviour
    {
        [Header("Blink")]
        [SerializeField] private float blinkMinInterval = 2.5f;
        [SerializeField] private float blinkMaxInterval = 5.5f;
        [SerializeField] private float blinkDuration = 0.12f;
        [SerializeField] private float doubleBlinkChance = 0.2f;

        [Header("Eye Gaze")]
        [SerializeField] private float gazeShiftMinInterval = 1.5f;
        [SerializeField] private float gazeShiftMaxInterval = 4f;
        [SerializeField] private float gazeHorizontalRange = 0.3f;   // ← INCREASED from 0.06
        [SerializeField] private float gazeVerticalRange = 0.15f;     // ← INCREASED from 0.06
        [SerializeField] private float gazeSmoothSpeed = 2.5f;

        [Header("Head Micro-Movement")]
        [SerializeField] private float headMicroIntensity = 2.5f;   // WAS 0.3f → NOW 2.5f
        [SerializeField] private float headMicroSpeed = 0.4f;  // WAS 0.5f → NOW 0.4f (slightly slower)


        [Header("Eyebrow Micro-Movement")]
        [SerializeField] private float browDriftIntensity = 0.06f;   // moderate
        [SerializeField] private float browDriftSpeed = 0.3f;    // slow, dreamy
        [SerializeField] private float browAsymmetry = 0.4f;    // left/right differ slightly



        // ═══ Brow State ═══
        private float _browNoiseTimeL;
        private float _browNoiseTimeR;

        // ═══ Output Properties ═══
        public float BrowInnerUpLeft { get; private set; }
        public float BrowInnerUpRight { get; private set; }


        // ═══ Blink State ═══
        private float _nextBlinkTime;
        private float _blinkProgress = -1f;
        private bool _doDoubleBlink;
        private int _blinkCount;

        // ═══ Gaze State ═══
        private float _nextGazeShift;
        private Vector2 _gazeTarget;
        private Vector2 _currentGaze;

        // ═══ Head State ═══
        private float _headNoiseTime;

        // ═══ Output Properties ═══
        public float BlinkLeft { get; private set; }
        public float BlinkRight { get; private set; }

        // Horizontal gaze: positive = look right, negative = look left
        // Vertical gaze: positive = look up, negative = look down
        public float GazeHorizontal { get; private set; }
        public float GazeVertical { get; private set; }

        // Individual eye outputs for ARKit blendshapes
        public float EyeLookUpLeft { get; private set; }
        public float EyeLookUpRight { get; private set; }
        public float EyeLookDownLeft { get; private set; }
        public float EyeLookDownRight { get; private set; }
        public float EyeLookInLeft { get; private set; }
        public float EyeLookInRight { get; private set; }
        public float EyeLookOutLeft { get; private set; }
        public float EyeLookOutRight { get; private set; }

        // Combined eye outputs for RPM blendshapes
        public float EyesLookUp { get; private set; }
        public float EyesLookDown { get; private set; }

        public Vector3 HeadMicroOffset { get; private set; }
        public bool IsBlinking => _blinkProgress >= 0;

        private void Start()
        {
            _nextBlinkTime = Time.time + Random.Range(1f, blinkMinInterval);
            _nextGazeShift = Time.time + Random.Range(0.5f, gazeShiftMinInterval);
            _headNoiseTime = Random.Range(0f, 100f);

            // Start with a random gaze target so eyes aren't dead center
            _gazeTarget = new Vector2(
                Random.Range(-gazeHorizontalRange * 0.3f, gazeHorizontalRange * 0.3f),
                Random.Range(-gazeVerticalRange * 0.3f, gazeVerticalRange * 0.3f)
            );

            // Offset so left and right brows never move in perfect sync
            _browNoiseTimeL = Random.Range(0f, 100f);
            _browNoiseTimeR = _browNoiseTimeL + Random.Range(8f, 20f);  // offset, not mirrored



        }

        public void Tick(float deltaTime)
        {
            UpdateBlink(deltaTime);
            UpdateGaze(deltaTime);
            UpdateHeadMicro(deltaTime);
            UpdateBrowDrift(deltaTime);  // ← ADD

        }


        private void UpdateBrowDrift(float dt)
        {
            _browNoiseTimeL += dt * browDriftSpeed;
            _browNoiseTimeR += dt * browDriftSpeed;

            // Perlin gives 0–1, remap to 0–intensity
            // We only drift upward (brows don't go below neutral in idle)
            float rawL = Mathf.PerlinNoise(_browNoiseTimeL, 13.7f);
            float rawR = Mathf.PerlinNoise(_browNoiseTimeR, 27.3f);

            // Remap 0–1 → 0–intensity, with soft bottom so brows spend
            // more time near neutral than at peak
            BrowInnerUpLeft = Mathf.Pow(rawL, 1.8f) * browDriftIntensity;
            BrowInnerUpRight = Mathf.Pow(rawR, 1.8f) * browDriftIntensity
                               * Mathf.Lerp(1f, 1f - browAsymmetry, rawR);
        }





        // ══════════════════════════════════════════════
        // BLINK
        // ══════════════════════════════════════════════

        private void UpdateBlink(float dt)
        {
            if (Time.time >= _nextBlinkTime && _blinkProgress < 0)
            {
                _blinkProgress = 0f;
                _blinkCount = 0;
                _doDoubleBlink = Random.value < doubleBlinkChance;
                _nextBlinkTime = Time.time + Random.Range(blinkMinInterval, blinkMaxInterval);
            }

            if (_blinkProgress >= 0)
            {
                _blinkProgress += dt / blinkDuration;

                float weight;
                if (_blinkProgress <= 0.35f)
                    weight = Mathf.SmoothStep(0, 1, _blinkProgress / 0.35f);
                else
                    weight = Mathf.SmoothStep(1, 0, (_blinkProgress - 0.35f) / 0.65f);

                BlinkLeft = weight;
                BlinkRight = weight;

                if (_blinkProgress >= 1f)
                {
                    _blinkCount++;
                    if (_doDoubleBlink && _blinkCount < 2)
                    {
                        _blinkProgress = 0f;
                    }
                    else
                    {
                        _blinkProgress = -1f;
                        BlinkLeft = 0;
                        BlinkRight = 0;
                    }
                }
            }
        }

        // ══════════════════════════════════════════════
        // GAZE
        // ══════════════════════════════════════════════

        private void UpdateGaze(float dt)
        {
            if (Time.time >= _nextGazeShift)
            {
                // Pick new gaze target
                _gazeTarget = new Vector2(
                    Random.Range(-gazeHorizontalRange, gazeHorizontalRange),
                    Random.Range(-gazeVerticalRange, gazeVerticalRange)
                );

                _nextGazeShift = Time.time + Random.Range(gazeShiftMinInterval, gazeShiftMaxInterval);
            }

            // Smooth interpolation to target
            _currentGaze = Vector2.Lerp(_currentGaze, _gazeTarget, dt * gazeSmoothSpeed);

            GazeHorizontal = _currentGaze.x;
            GazeVertical = _currentGaze.y;

            // ═══ Convert to ARKit individual eye blendshapes ═══
            //
            // ARKit eye look system:
            // Looking RIGHT → eyeLookOutLeft + eyeLookInRight
            // Looking LEFT  → eyeLookInLeft + eyeLookOutRight
            // Looking UP    → eyeLookUpLeft + eyeLookUpRight
            // Looking DOWN  → eyeLookDownLeft + eyeLookDownRight

            // Horizontal
            if (_currentGaze.x > 0)
            {
                // Looking right
                EyeLookOutLeft = _currentGaze.x;
                EyeLookInRight = _currentGaze.x;
                EyeLookInLeft = 0;
                EyeLookOutRight = 0;
            }
            else
            {
                // Looking left
                EyeLookInLeft = -_currentGaze.x;
                EyeLookOutRight = -_currentGaze.x;
                EyeLookOutLeft = 0;
                EyeLookInRight = 0;
            }

            // Vertical
            if (_currentGaze.y > 0)
            {
                // Looking up
                EyeLookUpLeft = _currentGaze.y;
                EyeLookUpRight = _currentGaze.y;
                EyeLookDownLeft = 0;
                EyeLookDownRight = 0;

                // RPM combined
                EyesLookUp = _currentGaze.y;
                EyesLookDown = 0;
            }
            else
            {
                // Looking down
                EyeLookDownLeft = -_currentGaze.y;
                EyeLookDownRight = -_currentGaze.y;
                EyeLookUpLeft = 0;
                EyeLookUpRight = 0;

                // RPM combined
                EyesLookDown = -_currentGaze.y;
                EyesLookUp = 0;
            }
        }

        // ══════════════════════════════════════════════
        // HEAD MICRO
        // ══════════════════════════════════════════════

        private void UpdateHeadMicro(float dt)
        {
            _headNoiseTime += dt * headMicroSpeed;

            HeadMicroOffset = new Vector3(
                (Mathf.PerlinNoise(_headNoiseTime, 0f) - 0.5f) * 2f * headMicroIntensity,
                (Mathf.PerlinNoise(0f, _headNoiseTime) - 0.5f) * 2f * headMicroIntensity,
                (Mathf.PerlinNoise(_headNoiseTime, _headNoiseTime) - 0.5f) * headMicroIntensity * 0.5f
            );
        }
    }
}