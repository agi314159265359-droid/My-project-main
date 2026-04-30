
using UnityEngine;

public class EyeBoneLookAtTarget : MonoBehaviour
{
    private Transform lookTarget;

    [Header("Eye Movement")]
    public float rotationSpeed = 8f;
    public float maxAngle = 25f;

    [Header("Behavior")]
    public float minLookTime = 2f;
    public float maxLookTime = 4.5f;

    private Quaternion initialRotation;
    private bool isLooking = true;
    private float nextSwitchTime;

    void Start()
    {
        lookTarget = GameObject.Find("Lookat").transform;
        initialRotation = transform.localRotation;
        

        ScheduleNextSwitch();
    }

    void ScheduleNextSwitch()
    {
        nextSwitchTime = Time.time + Random.Range(minLookTime, maxLookTime);
        isLooking = !isLooking;
    }

    void LateUpdate()
    {
        if (!lookTarget) return;

        if (Time.time > nextSwitchTime)
            ScheduleNextSwitch();

        if (!isLooking) return;

        Vector3 dir = lookTarget.position - transform.position;
        Quaternion lookRot = Quaternion.LookRotation(dir);

        Quaternion clamped = Quaternion.RotateTowards(
            initialRotation,
            lookRot,
            maxAngle
        );

        // Micro eye movement (human-like)
        Vector3 micro = new Vector3(
            Mathf.Sin(Time.time * 1.3f) * 0.4f,
            Mathf.Cos(Time.time * 1.7f) * 0.4f,
            0f
        );

        Quaternion finalRotation = clamped * Quaternion.Euler(micro);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            finalRotation,
            Time.deltaTime * rotationSpeed
        );
    }
}
