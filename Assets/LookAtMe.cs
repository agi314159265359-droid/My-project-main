using UnityEngine;

public class LookAtMe : MonoBehaviour
{
    [Header("=== CONTROL POINT (Create under Camera) ===")]
    public Transform headNeckController;

    [Header("=== NECK ===")]
    [Range(0f, 1f)] public float neckWeight = 0.15f;
    public float neckPitchOffset = 0f;
    [Range(1f, 15f)] public float neckSmoothness = 5f;
    [Range(0f, 80f)] public float neckMaxAngle = 30f;

    [Header("=== HEAD ===")]
    [Range(0f, 1f)] public float headWeight = 0.6f;
    public float headPitchOffset = 0f;
    [Range(1f, 15f)] public float headSmoothness = 5f;
    [Range(0f, 80f)] public float headMaxAngle = 60f;

    [Header("=== DEBUG ===")]
    public bool showDebugRay = true;

    // Bones
    private Transform headBone;
    private Transform neckBone;

    // Smooth tracking
    private Quaternion neckCurrentRotation;
    private Quaternion headCurrentRotation;
    private bool firstFrame = true;

    void Start()
    {
        Animator anim = GetComponent<Animator>();
        headNeckController = GameObject.Find("headcontroller").transform;

        if (anim != null)
        {
            headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            neckBone = anim.GetBoneTransform(HumanBodyBones.Neck);
        }

       
    }

    void LateUpdate()
    {
        if (headNeckController == null) return;

        // Initialize smooth tracking
        if (firstFrame)
        {
            if (neckBone != null) neckCurrentRotation = neckBone.rotation;
            if (headBone != null) headCurrentRotation = headBone.rotation;
            firstFrame = false;
        }

        // NECK
        if (neckBone != null)
        {
            ApplyBoneRotation(neckBone, headNeckController.position,
                neckWeight, neckPitchOffset, neckSmoothness, neckMaxAngle,
                ref neckCurrentRotation);
        }

        // HEAD
        if (headBone != null)
        {
            ApplyBoneRotation(headBone, headNeckController.position,
                headWeight, headPitchOffset, headSmoothness, headMaxAngle,
                ref headCurrentRotation);
        }

        // Debug
        if (showDebugRay && headBone != null)
        {
            Debug.DrawLine(headBone.position, headNeckController.position, Color.green);
        }
    }

    void ApplyBoneRotation(Transform bone, Vector3 targetPos,
        float weight, float pitchOffset, float smooth, float maxAngle,
        ref Quaternion currentSmoothedRotation)
    {
        Quaternion animationRotation = bone.rotation;

        Vector3 direction = (targetPos - bone.position).normalized;
        if (direction.sqrMagnitude < 0.001f) return;

        Quaternion lookRotation = Quaternion.LookRotation(direction, Vector3.up);
        lookRotation *= Quaternion.Euler(pitchOffset, 0f, 0f);

        Quaternion targetRotation = Quaternion.Slerp(animationRotation, lookRotation, weight);

        float angleDiff = Quaternion.Angle(animationRotation, targetRotation);
        if (angleDiff > maxAngle)
        {
            float t = maxAngle / angleDiff;
            targetRotation = Quaternion.Slerp(animationRotation, targetRotation, t);
        }

        currentSmoothedRotation = Quaternion.Slerp(
            currentSmoothedRotation,
            targetRotation,
            Time.deltaTime * smooth
        );

        bone.rotation = currentSmoothedRotation;
    }
}