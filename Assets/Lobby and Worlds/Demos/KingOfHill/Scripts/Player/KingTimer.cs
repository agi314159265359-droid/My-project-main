using FishNet.Object;
using FishNet.Object.Synchronizing;
using System;
using TMPro;
using UnityEngine;

public class KingTimer : NetworkBehaviour
{
    /// <summary>
    /// Called when timer hits 0.
    /// </summary>
    public event Action<NetworkObject> OnTimerComplete;
    /// <summary>
    /// Text to show time remaining.
    /// </summary>
    [Tooltip("Text to show time remaining.")]
    [SerializeField]
    private TextMeshProUGUI _timeText;
    /// <summary>
    /// Transform to rotate towards camera.
    /// </summary>
    [Tooltip("Transform to rotate towards camera.")]
    [SerializeField]
    private Transform _facingTransform;
    /// <summary>
    /// How high to be above the player.
    /// </summary>
    [Tooltip("How high to be above the player.")]
    [SerializeField]
    private float _offset = 0.65f;

    /// <summary>
    /// Time left to become king of the plane.
    /// </summary>
    private readonly SyncVar<float> _timeLeft = new SyncVar<float>(50f);

    /// <summary>
    /// True if was initialized.
    /// </summary>
    private bool _initialized;
    /// <summary>
    /// True if timer is running.
    /// </summary>
    private bool _running;
    /// <summary>
    /// Offset to parent in world space.
    /// </summary>
    private Vector3 _worldOffset;

    private void Awake()
    {
        _timeLeft.OnChange += On_TimeLeft;
    }
    private void OnDestroy()
    {
        _timeLeft.OnChange -= On_TimeLeft;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        //transform.SetParent(null);
        _running = true;
        _initialized = true;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _running = false;
    }

    private void Update()
    {
        if (_initialized && base.NetworkObject == null)
        {
            Destroy(gameObject);
            return;
        }

        if (!_running || !_initialized)
            return;

        if (base.IsClientStarted)
        {
            //Keep just above graphics.
            transform.position = transform.parent.position + new Vector3(0f, 1f, 0f);
            Camera c = Camera.main;
            if (c != null)
                _facingTransform.LookAt(transform.position + c.transform.rotation * Vector3.forward, c.transform.rotation * Vector3.up);
        }
        if (base.IsServerStarted)
        {
            if (_running)
            {
                _timeLeft.Value = Mathf.Max(_timeLeft.Value - Time.deltaTime, 0f);
                if (_timeLeft.Value <= 0f)
                    TimerComplete();
            }
        }
    }

    /// <summary>
    /// Stops the timer.
    /// </summary>
    public void StopTimer()
    {
        _running = false;
    }
    /// <summary>
    /// Stops the timer.
    /// </summary>
    private void TimerComplete()
    {
        StopTimer();
        OnTimerComplete?.Invoke(base.NetworkObject);
    }

    /// <summary>
    /// Called when server updates Timeleft.
    /// </summary>
    /// <param name="prev"></param>
    /// <param name="next"></param>
    private void On_TimeLeft(float prev, float next, bool asServer)
    {
        if (asServer)
            return;
        
        int result = Mathf.RoundToInt(next);
        _timeText.text = result.ToString();
    }
}
