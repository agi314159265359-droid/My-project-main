using UnityEngine;

public class MyperformanceManager : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
   
        void Start()
        {
            QualitySettings.vSyncCount = 0;         // Disable vsync for manual FPS control
            Application.targetFrameRate = 30;       // Adjust as needed
            Debug.Log("FPS and VSync configured.");
        }

    

   
   
}
