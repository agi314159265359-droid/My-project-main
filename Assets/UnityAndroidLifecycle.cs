using UnityEngine;
using FishNet;

/// <summary>
/// Bridges Android/Flutter lifecycle events to Unity lifecycle
/// This makes Unity behave like a standalone instance even when embedded in Flutter
/// </summary>
public class UnityAndroidLifecycle : MonoBehaviour
{
    private static UnityAndroidLifecycle _instance;
    private bool _isQuitting = false;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        Debug.Log("[UnityLifecycle] Initialized - ready to receive Flutter lifecycle events");
    }

    // ✅ Called from Flutter when app is paused (home button)
    public void OnFlutterPause()
    {
        Debug.Log("[UnityLifecycle] Flutter app PAUSED - triggering OnApplicationPause(true)");

        // Trigger Unity's normal pause behavior
        BroadcastMessage("OnApplicationPause", true, SendMessageOptions.DontRequireReceiver);

        // Optionally disconnect from network
        DisconnectFromNetwork();
    }

    // ✅ Called from Flutter when app resumes
    public void OnFlutterResume()
    {
        Debug.Log("[UnityLifecycle] Flutter app RESUMED - triggering OnApplicationPause(false)");

        // Trigger Unity's normal resume behavior
        BroadcastMessage("OnApplicationPause", false, SendMessageOptions.DontRequireReceiver);
    }

    // ✅ Called from Flutter when app is about to be killed
    public void OnFlutterQuit()
    {
        if (_isQuitting)
        {
            Debug.LogWarning("[UnityLifecycle] Already quitting, ignoring duplicate call");
            return;
        }

        _isQuitting = true;
        Debug.Log("[UnityLifecycle] ===== Flutter app QUITTING - triggering Unity cleanup =====");

        // ✅ Trigger Unity's normal quit behavior
        // This will call OnApplicationQuit() on all MonoBehaviours
        BroadcastMessage("OnApplicationQuit", SendMessageOptions.DontRequireReceiver);

        // ✅ Give Unity time to cleanup
        StartCoroutine(QuitSequence());
    }

    // ✅ Called from Flutter when app is detached/destroyed (force close)
    public void OnFlutterDestroy()
    {
        if (_isQuitting)
        {
            Debug.LogWarning("[UnityLifecycle] Already quitting, ignoring duplicate call");
            return;
        }

        _isQuitting = true;
        Debug.Log("[UnityLifecycle] ===== Flutter app DESTROYED - forcing Unity cleanup =====");

        // ✅ Force immediate cleanup
        ForceCleanup();

        // ✅ Trigger normal quit
        BroadcastMessage("OnApplicationQuit", SendMessageOptions.DontRequireReceiver);
    }

    private System.Collections.IEnumerator QuitSequence()
    {
        Debug.Log("[UnityLifecycle] Starting graceful quit sequence...");

        // Disconnect from network
        DisconnectFromNetwork();

        // Wait a bit for network cleanup
        yield return new WaitForSeconds(0.5f);

        Debug.Log("[UnityLifecycle] Quit sequence complete");
    }

    private void DisconnectFromNetwork()
    {
        Debug.Log("[UnityLifecycle] Disconnecting from network...");

        if (!InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted)
        {
            Debug.Log("[UnityLifecycle] Not connected to network, skipping disconnect");
            return;
        }

        // Stop client
        if (InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Started)
        {
            Debug.Log("[UnityLifecycle] Stopping client...");
            InstanceFinder.ClientManager.StopConnection();
        }

        // Stop server
      /*  if (InstanceFinder.ServerManager != null && InstanceFinder.ServerManager.Started)
        {
            Debug.Log("[UnityLifecycle] Stopping server...");
            InstanceFinder.ServerManager.StopConnection(true);
        }

        Debug.Log("[UnityLifecycle] Network disconnect complete");*/
    }

    private void ForceCleanup()
    {
        Debug.Log("[UnityLifecycle] Force cleanup - immediate disconnect");

        // Nuclear option - stop everything immediately
        if (InstanceFinder.NetworkManager != null)
        {
            try
            {
                InstanceFinder.NetworkManager.TransportManager.Transport.Shutdown();
                Debug.Log("[UnityLifecycle] Transport shutdown complete");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[UnityLifecycle] Error during transport shutdown: {ex.Message}");
            }
        }
    }

    // ✅ For debugging - log when Unity detects its own quit
    private void OnApplicationQuit()
    {
        Debug.Log("[UnityLifecycle] Unity's native OnApplicationQuit called");
        _isQuitting = true;
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        Debug.Log($"[UnityLifecycle] Unity's native OnApplicationPause called: {pauseStatus}");
    }
}