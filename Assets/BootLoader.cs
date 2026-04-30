using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BootLoader : MonoBehaviour
{
    
    public Slider loadingBar;
    public TextMeshProUGUI jokeText;  // Assign in Inspector
    public float fakeDuration = 5f;   // Duration of fake loading
    public float jokeInterval = 2f;   // Time between jokes

    public String loadAvatarText = "Don't interupt until Avatar Load";
   

   

    private void Start()
    {
        Application.targetFrameRate = 30;
        QualitySettings.vSyncCount = 0;
      //  DontDestroyOnLoad(gameObject);
       
         StartCoroutine(LoopingProgressWithJokes());
    }

  

    IEnumerator LoopingProgressWithJokes()
    {
        float timer = 0f;
        float jokeTimer = 0f;

        // Set first joke
        jokeText.text = loadAvatarText;

        while (timer < fakeDuration)
        {
            loadingBar.value = Mathf.PingPong(Time.time * 1f, 1f);

            // Rotate jokes every few seconds
            jokeTimer += Time.deltaTime;
            if (jokeTimer >= jokeInterval)
            {
                jokeText.text = loadAvatarText;
                jokeTimer = 0f;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        SceneManager.LoadScene("Lobby");
    }

    







}
