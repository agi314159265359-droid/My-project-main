using System.Collections;
using UnityEngine;

namespace Mikk.Avatar
{


    public class HeadMotionController : MonoBehaviour
    {
        [Header("Bone References")]
        [SerializeField] public Transform headBone;
        [SerializeField] public Transform neckBone;

        [Header("Find Bones Automatically")]
        [SerializeField] private bool autoFindBones = true;

        [Header("Motion Settings")]
        [SerializeField] private float nodAngle = 12f;
        [SerializeField] private float shakeAngle = 15f;
        [SerializeField] private float tiltAngle = 12f;
        [SerializeField] private float motionSmoothSpeed = 6f;

        [Header("Micro Movement")]
        [SerializeField] private float microMovementStrength = 1f;

        [Header("Distribution")]
        [SerializeField, Range(0f, 1f)] private float neckShare = 0.4f;

        // State
        private bool _bonesFound;
        private Vector3 _additiveEuler = Vector3.zero;
        private Vector3 _targetAdditiveEuler = Vector3.zero;
        private Vector3 _microOffset = Vector3.zero;
        private Coroutine _gestureCoroutine;
        private bool _gestureActive;
        private Animator _animator;
        private float _suppressUntil;

        private void Start()
        {
            _animator = GetComponentInParent<Animator>();

            if (autoFindBones)
                FindBones();

            if (headBone != null)
            {
                _bonesFound = true;
                Debug.Log($"[Head] Head bone found: {headBone.name}");
            }
            else
            {
                Debug.LogError("[Head] No head bone found!");
            }

            if (neckBone != null)
                Debug.Log($"[Head] Neck bone found: {neckBone.name}");
        }

        private void LateUpdate()
        {
            if (!_bonesFound) return;

            bool isSuppressed = Time.time < _suppressUntil;

            if (isSuppressed)
            {
                Vector3 gentleMicro = _microOffset * 0.2f;
                _additiveEuler = Vector3.Lerp(_additiveEuler, gentleMicro, Time.deltaTime * motionSmoothSpeed);
            }
            else
            {
                Vector3 combinedTarget = _targetAdditiveEuler + _microOffset;
                _additiveEuler = Vector3.Lerp(_additiveEuler, combinedTarget, Time.deltaTime * motionSmoothSpeed);
            }

            ApplyRotation();
        }


        public void ApplyRotation()
        {
            if (headBone != null)
            {
                Quaternion animatorRotation = headBone.localRotation;
                float headShare = 1f - neckShare;
                Vector3 headEuler = _additiveEuler * headShare;
                Quaternion additiveRot = Quaternion.Euler(headEuler);
                headBone.localRotation = animatorRotation * additiveRot;
            }

            if (neckBone != null)
            {
                Quaternion animatorRotation = neckBone.localRotation;
                Vector3 neckEuler = _additiveEuler * neckShare;
                Quaternion additiveRot = Quaternion.Euler(neckEuler);
                neckBone.localRotation = animatorRotation * additiveRot;
            }
        }

        // ══════════════════════════════════════════════
        // PUBLIC API
        // ══════════════════════════════════════════════

        public void ApplyMicroOffset(Vector3 offset)
        {
            _microOffset = offset * microMovementStrength;
        }

        public void SuppressDuringBodyAnim(float duration)
        {
            StopCurrentGesture();
            _suppressUntil = Time.time + duration;
        }

        public void TriggerNod()
        {
            StopCurrentGesture();
            _gestureCoroutine = StartCoroutine(NodCoroutine());
        }

        public void TriggerShake()
        {
            StopCurrentGesture();
            _gestureCoroutine = StartCoroutine(ShakeCoroutine());
        }

        public void TriggerTilt(float direction)
        {
            StopCurrentGesture();
            _gestureCoroutine = StartCoroutine(TiltCoroutine(direction));
        }

        public void TriggerLookDown()
        {
            StopCurrentGesture();
            _gestureCoroutine = StartCoroutine(LookDownCoroutine());
        }

        public void TriggerTalkingGesture()
        {
            StopCurrentGesture();

            int choice = Random.Range(0, 5);

            switch (choice)
            {
                case 0:
                    _gestureCoroutine = StartCoroutine(TalkingNodCoroutine());
                    break;
                case 1:
                    _gestureCoroutine = StartCoroutine(TalkingTiltCoroutine());
                    break;
                case 2:
                    _gestureCoroutine = StartCoroutine(TalkingSwayCoroutine());
                    break;
                case 3:
                    _gestureCoroutine = StartCoroutine(TalkingEmphasisCoroutine());
                    break;
                case 4:
                    _gestureCoroutine = StartCoroutine(TalkingTiltAndNodCoroutine());
                    break;
            }
        }

        public void ResetToNeutral()
        {
            StopCurrentGesture();
            _targetAdditiveEuler = Vector3.zero;
        }

        // ══════════════════════════════════════════════
        // EMOTIONAL GESTURES
        // ══════════════════════════════════════════════

        private IEnumerator NodCoroutine()
        {
            _gestureActive = true;

            for (int i = 0; i < 2; i++)
            {
                float elapsed = 0f;
                float duration = 0.2f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / duration;
                    float angle = Mathf.Sin(t * Mathf.PI) * nodAngle;
                    _targetAdditiveEuler = new Vector3(angle, 0, 0);
                    yield return null;
                }

                _targetAdditiveEuler = Vector3.zero;
                yield return new WaitForSeconds(0.1f);
            }

            _targetAdditiveEuler = Vector3.zero;
            _gestureActive = false;
            _gestureCoroutine = null;
        }

        private IEnumerator ShakeCoroutine()
        {
            _gestureActive = true;

            float elapsed = 0f;
            float duration = 0.7f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float decay = 1f - (t * 0.5f);
                float angle = Mathf.Sin(t * Mathf.PI * 3f) * shakeAngle * decay;
                _targetAdditiveEuler = new Vector3(0, angle, 0);
                yield return null;
            }

            _targetAdditiveEuler = Vector3.zero;
            _gestureActive = false;
            _gestureCoroutine = null;
        }

        private IEnumerator TiltCoroutine(float direction)
        {
            _gestureActive = true;

            float elapsed = 0f;
            float tiltDuration = 0.4f;
            float targetAngle = tiltAngle * Mathf.Sign(direction);

            while (elapsed < tiltDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / tiltDuration);
                _targetAdditiveEuler = new Vector3(0, 0, targetAngle * t);
                yield return null;
            }

            yield return new WaitForSeconds(0.8f);

            elapsed = 0f;
            float returnDuration = 0.5f;
            Vector3 startEuler = _targetAdditiveEuler;

            while (elapsed < returnDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / returnDuration);
                _targetAdditiveEuler = Vector3.Lerp(startEuler, Vector3.zero, t);
                yield return null;
            }

            _targetAdditiveEuler = Vector3.zero;
            _gestureActive = false;
            _gestureCoroutine = null;
        }

        private IEnumerator LookDownCoroutine()
        {
            _gestureActive = true;

            float downAngle = 8f; // Positive X = chin down on RPM

            float elapsed = 0f;
            float duration = 0.5f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                _targetAdditiveEuler = new Vector3(downAngle * t, 0, 0);
                yield return null;
            }

            yield return new WaitForSeconds(1.5f);

            elapsed = 0f;
            float returnDuration = 0.8f;
            Vector3 startEuler = _targetAdditiveEuler;

            while (elapsed < returnDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / returnDuration);
                _targetAdditiveEuler = Vector3.Lerp(startEuler, Vector3.zero, t);
                yield return null;
            }

            _targetAdditiveEuler = Vector3.zero;
            _gestureActive = false;
            _gestureCoroutine = null;
        }

        // ══════════════════════════════════════════════
        // TALKING GESTURES (casual conversation)
        // ══════════════════════════════════════════════

        private IEnumerator TalkingNodCoroutine()
        {
            _gestureActive = true;

            float elapsed = 0f;
            float duration = 0.4f;
            float angle = Random.Range(4f, 7f);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float a = Mathf.Sin(t * Mathf.PI) * angle;
                _targetAdditiveEuler = new Vector3(a, 0, 0);
                yield return null;
            }

            _targetAdditiveEuler = Vector3.zero;
            yield return new WaitForSeconds(Random.Range(0.3f, 0.8f));

            _gestureActive = false;
            _gestureCoroutine = null;
        }

        private IEnumerator TalkingTiltCoroutine()
        {
            _gestureActive = true;

            float direction = Random.value > 0.5f ? 1f : -1f;
            float angle = Random.Range(3f, 6f);

            float elapsed = 0f;
            float duration = 0.5f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                _targetAdditiveEuler = new Vector3(0, 0, angle * direction * t);
                yield return null;
            }

            yield return new WaitForSeconds(Random.Range(0.5f, 1.5f));

            elapsed = 0f;
            duration = 0.6f;
            Vector3 start = _targetAdditiveEuler;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                _targetAdditiveEuler = Vector3.Lerp(start, Vector3.zero, t);
                yield return null;
            }

            _targetAdditiveEuler = Vector3.zero;
            _gestureActive = false;
            _gestureCoroutine = null;
        }

        private IEnumerator TalkingSwayCoroutine()
        {
            _gestureActive = true;

            float duration = Random.Range(1.5f, 2.5f);
            float elapsed = 0f;
            float yawAmount = Random.Range(3f, 6f);
            float pitchAmount = Random.Range(1f, 3f);
            float speed = Random.Range(1.5f, 2.5f);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed * speed;
                float yaw = Mathf.Sin(t * Mathf.PI) * yawAmount;
                float pitch = Mathf.Sin(t * Mathf.PI * 2f) * pitchAmount;
                float envelope = Mathf.Sin(elapsed / duration * Mathf.PI);
                _targetAdditiveEuler = new Vector3(pitch, yaw, 0) * envelope;
                yield return null;
            }

            _targetAdditiveEuler = Vector3.zero;
            _gestureActive = false;
            _gestureCoroutine = null;
        }

        private IEnumerator TalkingEmphasisCoroutine()
        {
            _gestureActive = true;

            float elapsed = 0f;
            float duration = 0.15f;
            float angle = Random.Range(5f, 8f);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                _targetAdditiveEuler = new Vector3(angle * t, 0, 0);
                yield return null;
            }

            yield return new WaitForSeconds(0.1f);

            elapsed = 0f;
            duration = 0.4f;
            Vector3 start = _targetAdditiveEuler;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                _targetAdditiveEuler = Vector3.Lerp(start, Vector3.zero, t);
                yield return null;
            }

            _targetAdditiveEuler = Vector3.zero;
            _gestureActive = false;
            _gestureCoroutine = null;
        }

        private IEnumerator TalkingTiltAndNodCoroutine()
        {
            _gestureActive = true;

            float direction = Random.value > 0.5f ? 1f : -1f;
            float tiltAngleSmall = Random.Range(3f, 5f);
            float nodAngleSmall = Random.Range(3f, 5f);

            // Tilt
            float elapsed = 0f;
            float duration = 0.3f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                _targetAdditiveEuler = new Vector3(0, 0, tiltAngleSmall * direction * t);
                yield return null;
            }

            yield return new WaitForSeconds(0.2f);

            // Nod while tilted
            elapsed = 0f;
            duration = 0.3f;
            Vector3 tiltedPosition = _targetAdditiveEuler;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float nod = Mathf.Sin(t * Mathf.PI) * nodAngleSmall;
                _targetAdditiveEuler = tiltedPosition + new Vector3(nod, 0, 0);
                yield return null;
            }

            _targetAdditiveEuler = tiltedPosition;
            yield return new WaitForSeconds(0.3f);

            // Return
            elapsed = 0f;
            duration = 0.5f;
            Vector3 start = _targetAdditiveEuler;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                _targetAdditiveEuler = Vector3.Lerp(start, Vector3.zero, t);
                yield return null;
            }

            _targetAdditiveEuler = Vector3.zero;
            _gestureActive = false;
            _gestureCoroutine = null;
        }

        // ══════════════════════════════════════════════
        // BONE FINDING
        // ══════════════════════════════════════════════

        private void FindBones()
        {
            if (_animator != null)
            {
                if (headBone == null)
                    headBone = _animator.GetBoneTransform(HumanBodyBones.Head);
                if (neckBone == null)
                    neckBone = _animator.GetBoneTransform(HumanBodyBones.Neck);
            }

            if (headBone == null || neckBone == null)
            {
                Transform root = transform;
                while (root.parent != null) root = root.parent;

                var allTransforms = root.GetComponentsInChildren<Transform>(true);

                foreach (var t in allTransforms)
                {
                    string lower = t.name.ToLower();

                    if (headBone == null)
                    {
                        if ((lower == "head" || lower == "mixamorig:head") &&
                            t.GetComponent<SkinnedMeshRenderer>() == null &&
                            t.GetComponent<MeshRenderer>() == null)
                        {
                            headBone = t;
                        }
                    }

                    if (neckBone == null)
                    {
                        if ((lower == "neck" || lower == "mixamorig:neck") &&
                            t.GetComponent<SkinnedMeshRenderer>() == null)
                        {
                            neckBone = t;
                        }
                    }
                }
            }
        }

        private void StopCurrentGesture()
        {
            if (_gestureCoroutine != null)
            {
                StopCoroutine(_gestureCoroutine);
                _gestureCoroutine = null;
            }
            _gestureActive = false;
            _targetAdditiveEuler = Vector3.zero;
        }
    }
}