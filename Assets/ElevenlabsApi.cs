using OpenAI;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class ElevenlabsApi : MonoBehaviour
{
    [Header("API Settings")]
    public string apiKey = "sk_c61754881a1ac0aee14838447dea9fe34d411c3168d5a0e3";
    public string voiceID = "m5qndnI7u4OAdXhH0Mr5"; // Example: "21m00Tcm4TlvDq8ikWAM"
    public ChatGPT chatGPT;

    public AudioSource audioSource;

    void Start()
    {

    }

    public void GetAudio(string text)
    {
        StartCoroutine(SendTTSRequest(text));
    }

    IEnumerator SendTTSRequest(string text)
    {
        string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceID}";

        // Create request body
        TTSRequestBody requestBody = new TTSRequestBody
        {
            text = text,
            model_id = "eleven_multilingual_v2",
            voice_settings = new VoiceSettings
            {
                stability = 0.5f,
                similarity_boost = 0.5f
            }
        };

        string jsonBody = JsonUtility.ToJson(requestBody);

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);

            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("xi-api-key", apiKey);
            webRequest.SetRequestHeader("accept", "audio/mpeg");


            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(webRequest);
                audioSource.clip = clip;
                audioSource.Play();
            }
            else
            {
                Debug.LogError($"Error: {webRequest.error}");
             //   chatGPT.GoSound(text);
            }


        }
    }

    [System.Serializable]
    public class TTSRequestBody
    {
        public string text;
        public string model_id;
        public VoiceSettings voice_settings;
    }

    [System.Serializable]
    public class VoiceSettings
    {
        public float stability;
        public float similarity_boost;
    }
}
