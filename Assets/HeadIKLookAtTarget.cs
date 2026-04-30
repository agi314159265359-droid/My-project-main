using UnityEngine;


[RequireComponent(typeof(Animator))]
public class HeadIKLookAtTarget : MonoBehaviour
{
    private Transform lookTarget;

    [Header("Weights")]
    public float lookWeight = 1f;
    public float bodyWeight = 0.2f;
    public float headWeight = 0.7f;

    [Header("Behavior")]
    public float minLookTime = 2.5f;
    public float maxLookTime = 5f;

    private Animator animator;
    private bool isLooking = true;
    private float nextSwitchTime;

    void Start()
    {

        animator = GetComponent<Animator>();
        lookTarget = GameObject.Find("Lookat").transform;
        ScheduleNextSwitch();
    }

    void ScheduleNextSwitch()
    {
        nextSwitchTime = Time.time + Random.Range(minLookTime, maxLookTime);
        isLooking = !isLooking;
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (!lookTarget) return;

        if (Time.time > nextSwitchTime)
            ScheduleNextSwitch();

        float weight = isLooking ? lookWeight : 0f;

        animator.SetLookAtWeight(weight, bodyWeight, headWeight, 0f);
        animator.SetLookAtPosition(lookTarget.position);
    }
}