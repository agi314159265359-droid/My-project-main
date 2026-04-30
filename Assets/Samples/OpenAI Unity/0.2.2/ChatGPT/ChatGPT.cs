using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using FishNet.Component.Animating;
using System.Collections;
using System.Threading.Tasks;
using System.Threading;

using System;

using FishNet.Object;
using FirstGearGames.LobbyAndWorld.Lobbies;
using FirstGearGames.LobbyAndWorld.Global.Canvases;
using FirstGearGames.LobbyAndWorld.Global;
using System.Linq;

using static ElevenlabsApi;
using UnityEngine.Networking;
using System.IO;

using NAudio.Wave;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
using UnityEngine.Audio;
using Unity.Services.Relay.Models;
using UnityEngine.Events;
using static System.Net.Mime.MediaTypeNames;
using System.Net.Http;
using NLayer;
using Cysharp.Threading.Tasks;
using Random = UnityEngine.Random;
using Mikk.Avatar.Expression;




namespace OpenAI
{
    public class ChatGPT : NetworkBehaviour
    {




       // public bool isclintactive = false;
        string lastparam;
        Coroutine returntoidlecoruntine;
        bool toggle = false;



        private OpenAIApi openai = new OpenAIApi("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");
        private CancellationTokenSource cancellationTokenSource;

        Animator animator;
        



        private Dictionary<string, AnimationResponse> emotionCache = new Dictionary<string, AnimationResponse>
{
    // Basic Greetings
    {"hi", new AnimationResponse { emotion_type = "greeting", emotion_intensity = 0.7f, message_length_category = "short", animation_variant = "0" }},
    {"hello", new AnimationResponse { emotion_type = "greeting", emotion_intensity = 0.7f, message_length_category = "short", animation_variant = "1" }},
    {"hey", new AnimationResponse { emotion_type = "greeting", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "0" }},
    {"namaste", new AnimationResponse { emotion_type = "greeting", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "1" }},
    {"namaskar", new AnimationResponse { emotion_type = "greeting", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "0" }},
    {"hii", new AnimationResponse { emotion_type = "greeting", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "1" }},
    {"helo", new AnimationResponse { emotion_type = "greeting", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "0" }},
    
    // Hindi Greetings
    {"हैलो", new AnimationResponse { emotion_type = "greeting", emotion_intensity = 0.7f, message_length_category = "short", animation_variant = "0" }},
    {"नमस्ते", new AnimationResponse { emotion_type = "greeting", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "1" }},
    {"हाय", new AnimationResponse { emotion_type = "greeting", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "0" }},
    
    // Basic Responses
    {"ok", new AnimationResponse { emotion_type = "neutral", emotion_intensity = 0f, message_length_category = "short", animation_variant = "none" }},
    {"okay", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "0" }},
    {"k", new AnimationResponse { emotion_type = "neutral", emotion_intensity = 0f, message_length_category = "short", animation_variant = "none" }},
    {"kk", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.2f, message_length_category = "short", animation_variant = "0" }},
    {"yes", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.5f, message_length_category = "short", animation_variant = "1" }},
    {"no", new AnimationResponse { emotion_type = "disagree", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "0" }},
    {"nope", new AnimationResponse { emotion_type = "disagree", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "1" }},
    {"nah", new AnimationResponse { emotion_type = "disagree", emotion_intensity = 0.2f, message_length_category = "short", animation_variant = "0" }},
    

    // Hindi Basic Responses
    {"हाँ", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.5f, message_length_category = "short", animation_variant = "0" }},
    {"हां", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.5f, message_length_category = "short", animation_variant = "1" }},
    {"नहीं", new AnimationResponse { emotion_type = "disagree", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "0" }},
    {"ठीक है", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "1" }},
    {"ठीक", new AnimationResponse { emotion_type = "neutral", emotion_intensity = 0.1f, message_length_category = "short", animation_variant = "none" }},
    {"अच्छा", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "0" }},
    
    // Hinglish Common
    {"theek hai", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "0" }},
    {"thik hai", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "1" }},
    {"haan", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "0" }},
    {"han", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "1" }},
    {"nahi", new AnimationResponse { emotion_type = "disagree", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "0" }},
    {"nahin", new AnimationResponse { emotion_type = "disagree", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "1" }},
    {"achha", new AnimationResponse { emotion_type = "thinking", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "0" }},
    {"acha", new AnimationResponse { emotion_type = "thinking", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "1" }},
    
    // Expressions & Reactions
    {"wow", new AnimationResponse { emotion_type = "surprised", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "0" }},
    {"omg", new AnimationResponse { emotion_type = "astounded", emotion_intensity = 0.9f, message_length_category = "short", animation_variant = "1" }},
    {"lol", new AnimationResponse { emotion_type = "happy", emotion_intensity = 0.7f, message_length_category = "short", animation_variant = "0" }},
    {"haha", new AnimationResponse { emotion_type = "happy", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "1" }},
    {"hehe", new AnimationResponse { emotion_type = "happy", emotion_intensity = 0.5f, message_length_category = "short", animation_variant = "0" }},
    {"hmm", new AnimationResponse { emotion_type = "thinking", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "1" }},
    {"hmmmm", new AnimationResponse { emotion_type = "thinking", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "0" }},
    {"ohh", new AnimationResponse { emotion_type = "surprised", emotion_intensity = 0.5f, message_length_category = "short", animation_variant = "1" }},
    {"ohhh", new AnimationResponse { emotion_type = "surprised", emotion_intensity = 0.7f, message_length_category = "short", animation_variant = "0" }},
    {"uff", new AnimationResponse { emotion_type = "exhausted", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "1" }},
    {"argh", new AnimationResponse { emotion_type = "angry", emotion_intensity = 0.7f, message_length_category = "short", animation_variant = "0" }},
    
    // Hindi Expressions
    {"वाह", new AnimationResponse { emotion_type = "astounded", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "0" }},
    {"अरे", new AnimationResponse { emotion_type = "surprised", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "1" }},
    {"हे भगवान", new AnimationResponse { emotion_type = "astounded", emotion_intensity = 0.9f, message_length_category = "short", animation_variant = "0" }},
    {"बहुत बढ़िया", new AnimationResponse { emotion_type = "happy", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "0" }},
    
    // Hinglish Expressions
    {"wah", new AnimationResponse { emotion_type = "astounded", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "1" }},
    {"are", new AnimationResponse { emotion_type = "surprised", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "0" }},
    {"are yaar", new AnimationResponse { emotion_type = "confused", emotion_intensity = 0.5f, message_length_category = "short", animation_variant = "1" }},
    {"yaar", new AnimationResponse { emotion_type = "neutral", emotion_intensity = 0.2f, message_length_category = "short", animation_variant = "0" }},
    {"bhai", new AnimationResponse { emotion_type = "neutral", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "1" }},
    {"dude", new AnimationResponse { emotion_type = "neutral", emotion_intensity = 0.2f, message_length_category = "short", animation_variant = "0" }},
    {"yaar kya", new AnimationResponse { emotion_type = "confused", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "1" }},
    {"kya baat", new AnimationResponse { emotion_type = "astounded", emotion_intensity = 0.7f, message_length_category = "short", animation_variant = "0" }},
    {"kya yaar", new AnimationResponse { emotion_type = "confused", emotion_intensity = 0.5f, message_length_category = "short", animation_variant = "1" }},
    
    // Modern Slang
    {"bruh", new AnimationResponse { emotion_type = "confused", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "0" }},
    {"fr", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "1" }},
    {"frfr", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "0" }},
    {"bet", new AnimationResponse { emotion_type = "confident", emotion_intensity = 0.7f, message_length_category = "short", animation_variant = "1" }},
    {"cap", new AnimationResponse { emotion_type = "disagree", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "0" }},
    {"no cap", new AnimationResponse { emotion_type = "confident", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "1" }},
    {"slay", new AnimationResponse { emotion_type = "confident", emotion_intensity = 0.9f, message_length_category = "short", animation_variant = "0" }},
    {"periodt", new AnimationResponse { emotion_type = "confident", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "1" }},
    {"sheesh", new AnimationResponse { emotion_type = "astounded", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "0" }},
    {"bussin", new AnimationResponse { emotion_type = "happy", emotion_intensity = 0.8f, message_length_category = "short", animation_variant = "1" }},
    {"vibes", new AnimationResponse { emotion_type = "happy", emotion_intensity = 0.6f, message_length_category = "short", animation_variant = "0" }},
    {"mood", new AnimationResponse { emotion_type = "agree", emotion_intensity = 0.5f, message_length_category = "short", animation_variant = "1" }},
    
    // Questions
    {"kya", new AnimationResponse { emotion_type = "confused", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "0" }},
    {"what", new AnimationResponse { emotion_type = "confused", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "1" }},
    {"why", new AnimationResponse { emotion_type = "confused", emotion_intensity = 0.5f, message_length_category = "short", animation_variant = "0" }},
    {"how", new AnimationResponse { emotion_type = "thinking", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "1" }},
    {"kaise", new AnimationResponse { emotion_type = "thinking", emotion_intensity = 0.4f, message_length_category = "short", animation_variant = "0" }},
    {"kyun", new AnimationResponse { emotion_type = "confused", emotion_intensity = 0.5f, message_length_category = "short", animation_variant = "1" }},
    {"kab", new AnimationResponse { emotion_type = "thinking", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "0" }},
    {"when", new AnimationResponse { emotion_type = "thinking", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "1" }},
    
   
    
    // Farewells
    {"bye", new AnimationResponse { emotion_type = "neutral", emotion_intensity = 0.3f, message_length_category = "short", animation_variant = "0" }} };





  



     //   [SerializeField] private AudioSource recivervoicesource;



        [Header("API Settings")]
        public string apiKey = "sk_a88aa74875d1b2688354190c176c2cb1232ebae805fb59b4";
        public string voiceID = "9BWtsMINqrJLrRacOk9x"; // Example: "21m00Tcm4TlvDq8ikWAM"

        private AvatarExpressionController expressionController;

















        private void Start()
        {


            animator = gameObject.transform.parent.GetChild(2).GetComponent<Animator>();
            expressionController = gameObject.transform.parent.GetChild(2).GetComponent<AvatarExpressionController>();

            if (expressionController != null)
            {
                // Subscribe to expression events for network sync
                expressionController.OnExpressionReady += HandleExpressionReady;
            }

            lastparam = "Blend";


           


           

            

            
           
           


        }




        private void HandleExpressionReady(FullExpressionData data)
        {
            // Send over network using ChatGPT's existing NetworkBehaviour
            if (IsSpawned)
            {
                SendExpressionRPC(data);
            }
        }





        public void StartAI(string text)
        {

            expressionController.OnChatMessageReceived(text);


            //  var speakTask = synthesizer.StartSpeakingTextAsync(text);
            //   StartCoroutine(SpeakRoutine(speakTask));

            /*  if (emotionCache.TryGetValue(text.ToLower().Trim(), out var cachedResponse))
              {
                  TriggerAnimation(cachedResponse);
                  return;
              }

              SendAiAsync(text).Forget();*/


        }

        [ServerRpc(RequireOwnership = false)]
        private void SendExpressionRPC(FullExpressionData data)
        {
            ReceiveExpressionRPC(data);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ReceiveExpressionRPC(FullExpressionData data)
        {
            if (expressionController != null)
            {
                expressionController.ApplyExpressionFromNetwork(data);
            }
        }

        private async UniTaskVoid SendAiAsync(string text)
        {
            try
            {
                await SendText(text);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("AI request was cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"AI request failed: {ex.Message}");
                TriggerAnimation(GetDefaultAnimation());
            }



        }

      

        








        private async UniTask SendText(string userMessage)
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();

            // Add timeout protection
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationTokenSource.Token,
                timeoutCts.Token
            );






            string systemPrompt = @"
You are a game animation logic assistant working for a Unity 3D game. Based on a player's chat message (which can be in Hindi, English, or Hinglish), output a structured JSON response with the following:

1. emotion_type: The general emotion expressed in the chat message. Choose one from:
   [""neutral"", ""happy"", ""sad"", ""angry"", ""surprised"", ""fearful"", ""astounded"", ""thinking"", ""confused"", ""contempt"", ""disgusted"", ""exhausted"", ""greeting"", ""confident"", ""cool"", ""agree"", ""disagree"", ""sleepy"", ""teasing"", ""apologetic"", ""flirty""]

2. emotion_intensity: A number between 0 and 1 representing the strength of the emotion.
   - A mildly angry message → 0.3
   - A very angry message → 1.0
   - A casual neutral message → 0.0

3. message_length_category: Categorize the message as either:
   - ""short"" → very brief, generic replies like ""ok"", ""hi"", ""ठीक है"", ""yes"", ""no"", ""hmm""
   - ""long"" → any message that expresses a full idea, action, or intent — even if short in character count (e.g., “मैंने खाना खा लिया”, “I’m on my way”, “Don’t do that”)

4. animation_variant: Randomly pick one of [""0"", ""1"", ""none""] to allow subtle variation in avatar response.

---

Interpret tone and emotion even when sarcasm, exaggeration, or slang are used.



Handle modern Gen Z slang, emoji, memes, abbreviations, and expressive forms. Examples:
- “slay” → ""confident""
- “sheesh” → ""astounded""
- “bruh” → ""confused"" or ""disagree""
- “I’m dead 💀” → ""exhausted"" or ""astounded""
- “😭” → ""sad""
- “rizz” → ""flirty""

If a message uses *ALL CAPS, **repeated letters* (e.g., “NOOOO”), or *strong punctuation* (e.g., “?! 😱”), increase emotion_intensity.

Important clarification:

- Only assign `emotion_type` based on the **user's actual expressed emotion**, not simply the emotion words in the message.
- If the message is talking *about* an emotion, action, or thought (e.g., “What are you thinking about?”, “Why are you sad?”) — but the speaker is not directly expressing that emotion themselves — then treat it as `""neutral""` unless there is strong evidence otherwise.






Now process the following message:
";


            var messages = new List<ChatMessage>
    {
        new ChatMessage() { Role = "system", Content = systemPrompt },
        new ChatMessage() { Role = "user", Content = userMessage }
    };

            try
            {
                var request = new CreateChatCompletionRequest()
                {
                    Model = "gpt-4o-mini",
                    Messages = messages,
                    Temperature = 0.3f
                    

                };

                var completionTask = openai.CreateChatCompletion(request);
                linkedCts.Token.ThrowIfCancellationRequested();

                var completionResponse = await completionTask.ConfigureAwait(false);
                linkedCts.Token.ThrowIfCancellationRequested();

                if (completionResponse.Choices == null || completionResponse.Choices.Count == 0)
                {
                    Debug.LogWarning("No choices received from OpenAI.");
                    await UniTask.SwitchToMainThread();
                    TriggerAnimation(GetDefaultAnimation());
                    return;
                }

                string replyContent = completionResponse.Choices[0].Message.Content;
                Debug.Log("GPT Raw Response:\n" + replyContent);

                var animData = await UniTask.RunOnThreadPool(() =>
                {
                    try
                    {
                        string cleanedJson = CleanGPTJson(replyContent);
                        return JsonConvert.DeserializeObject<AnimationResponse>(cleanedJson);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to parse GPT response JSON: {ex.Message}");
                        return null;
                    }
                }, cancellationToken: linkedCts.Token);

                if (animData != null)
                {
                    TriggerAnimation(animData);
                }
                else
                {
                    TriggerAnimation(GetDefaultAnimation());
                }
            }
            catch (OperationCanceledException) when (cancellationTokenSource.Token.IsCancellationRequested)
            {
                Debug.Log("Request cancelled by new request");
                throw; // Re-throw to be handled by ExecuteAIRequest
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                Debug.LogWarning("Request timed out");
                TriggerAnimation(GetDefaultAnimation());
                throw; // Re-throw to be handled by ExecuteAIRequest
            }




            /*var messages = new List<ChatMessage>
                {
                    new ChatMessage()
                    {
                        Role = "system",
                        Content = systemPrompt
                    },
                    new ChatMessage()
                    {
                        Role = "user",
                        Content = userMessage
                    }
                };

                        try
                        {
                            var completionResponse = await openai.CreateChatCompletion(new CreateChatCompletionRequest()
                            {
                                Model = "gpt-4o",
                                Messages = messages,
                            });

                            if (completionResponse.Choices != null && completionResponse.Choices.Count > 0)
                            {
                                string replyContent = completionResponse.Choices[0].Message.Content;
                                Debug.Log("GPT Raw Response:\n" + replyContent);

                                string cleanedJson = CleanGPTJson(replyContent);

                                try
                                {
                                    var animData = await Task.Run(() =>
                                    {
                                        string cleanedJson = CleanGPTJson(replyContent);
                                        return JsonConvert.DeserializeObject<AnimationResponse>(cleanedJson);
                                    });

                                    TriggerAnimation(animData);

                                    // var animData = JsonConvert.DeserializeObject<AnimationResponse>(cleanedJson);
                                    // TriggerAnimation(animData);



                                }
                                catch
                                {
                                    Debug.LogWarning("Failed to parse GPT response JSON.");
                                  //  return GetDefaultAnimation();
                                }
                            }
                            else
                            {
                                Debug.LogWarning("No choices received from OpenAI.");
                               // return GetDefaultAnimation();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError("OpenAI API Error: " + ex.Message);
                           // return GetDefaultAnimation();
                        }*/











        }



        private string CleanGPTJson(string raw)
        {
            // Remove markdown-style code block formatting
            raw = raw.Replace("```json", "").Replace("```", "").Trim();

            // Strip anything before the first { if GPT includes explanation text
            int startIndex = raw.IndexOf('{');
            int endIndex = raw.LastIndexOf('}');
            if (startIndex >= 0 && endIndex > startIndex)
            {
                raw = raw.Substring(startIndex, endIndex - startIndex + 1);
            }

            return raw;
        }

      public void TriggerAnimation(AnimationResponse data)
        {
           
            sendAnimationRPCOverNetwork(data);
            ExecuteAnimation(data);
           }



        private AnimationResponse GetDefaultAnimation()
        {
            return new AnimationResponse
            {
                emotion_type = "neutral",
                emotion_intensity = 0.0f,
                message_length_category = "short",
                animation_variant = "none"
            };
        }


        



        [ServerRpc(RequireOwnership =false)]
        private void sendAnimationRPCOverNetwork(AnimationResponse data)
        {

            PlayAnimationonClient(data);
           
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void PlayAnimationonClient(AnimationResponse data){

            ExecuteAnimation(data);
}

        private void ExecuteAnimation(AnimationResponse data)
        {

            if (data.message_length_category == "short")
            {
                var emotions = new List<string> {"sad", "angry", "surprised", "fearful", "astounded", "thinking", "confused", "contempt", "disgusted", "exhausted", "confident", "agree", "disagree", "sleepy", "teasing", "apologetic", "flirty" };

                if (data.emotion_type == "happy")
                {
                    if (returntoidlecoruntine != null)
                    {

                        StopCoroutine(returntoidlecoruntine);
                    }


                    animator.SetFloat(lastparam, 0);
                    animator.SetFloat("happyfloat", data.emotion_intensity);
                    animator.CrossFade(data.emotion_type + "_" + data.animation_variant, 0.2f);
                    animator.CrossFade(data.emotion_type + "_" + data.animation_variant, 0.2f, animator.GetLayerIndex("faceLayer"));
                    lastparam = data.emotion_type + "float";



                    returntoidlecoruntine = StartCoroutine(ReturntoIdleAfter(3f));


                }
                else if (data.emotion_type == "greeting")
                {

                    if (returntoidlecoruntine != null)
                    {

                        StopCoroutine(returntoidlecoruntine);
                    }

                    animator.SetFloat(lastparam, 0);
                    animator.SetFloat("greetingfloat", data.emotion_intensity);
                    // networkanimator.SetTrigger("greet");
                    animator.CrossFade(data.emotion_type + "_" + data.animation_variant, 0.2f);
                    animator.CrossFade(data.emotion_type + "_" + data.animation_variant, 0.2f, animator.GetLayerIndex("faceLayer"));
                    lastparam = data.emotion_type + "float";

                    returntoidlecoruntine = StartCoroutine(ReturntoIdleAfter(3f));




                } else if(data.emotion_type == "neutral")
                {
                    if (returntoidlecoruntine != null)
                    {

                        StopCoroutine(returntoidlecoruntine);
                    }
                    toggle = !toggle;
                    animator.SetFloat(lastparam, 0);
                    animator.SetFloat("neutralfloat", toggle ? 1f : 0f);
                    animator.CrossFade(data.emotion_type + "_" + data.animation_variant, 0.2f);
                    animator.CrossFade(data.emotion_type + "_" + data.animation_variant, 0.2f, animator.GetLayerIndex("faceLayer"));
                    lastparam = data.emotion_type + "float";

                    returntoidlecoruntine = StartCoroutine(ReturntoIdleAfter(3f));


                }






                else if (emotions.Contains(data.emotion_type))
                {

                    if (returntoidlecoruntine != null)
                    {

                        StopCoroutine(returntoidlecoruntine);
                    }


                    animator.SetFloat(lastparam, 0);
                    animator.SetFloat(data.emotion_type + "float", data.emotion_intensity);
                    animator.CrossFade(data.emotion_type + "_" + data.animation_variant, 0.2f);
                    animator.CrossFade(data.emotion_type + "_" + data.animation_variant, 0.2f, animator.GetLayerIndex("faceLayer"));

                    lastparam = data.emotion_type + "float";

                    returntoidlecoruntine = StartCoroutine(ReturntoIdleAfter(3f));

                }


            }
            else if (data.message_length_category == "long")
            {
                var emotions = new List<string> {"neutral", "happy", "sad", "angry", "surprised", "fearful", "astounded", "thinking", "confused", "contempt", "disgusted", "exhausted", "greeting", "confident", "agree", "disagree", "sleepy", "teasing", "apologetic", "flirty" };

                if (emotions.Contains(data.emotion_type))
                {
                    if (returntoidlecoruntine != null)
                    {

                        StopCoroutine(returntoidlecoruntine);
                    }

                    animator.SetFloat(lastparam, 0);
                    animator.SetFloat(data.emotion_type + "float", data.emotion_intensity);
                    animator.CrossFade(data.emotion_type + "_" + data.animation_variant, 0.2f);
                    animator.CrossFade(data.emotion_type + "_" + data.animation_variant, 0.2f, animator.GetLayerIndex("faceLayer"));
                    lastparam = data.emotion_type + "float";



                    returntoidlecoruntine = StartCoroutine(ReturntoIdleAfter(5f));


                }


            }



        }

        private IEnumerator ReturntoIdleAfter(float v)
        {
            yield return new WaitForSeconds(v);
            animator.CrossFade("New State", 0.3f, animator.GetLayerIndex("ArmLayer"));
        }





      




       

       









        private IEnumerator getAnimationName(string v)
        {
            yield return new WaitForSecondsRealtime(1);
            animator.SetBool(v, true);
         

        }

        void Update()
        {
            



}


     





       /* void CheckForKeys()
        {
            if (enableVisemeTestKeys)
            {
                for (int i = 0; i < OVRLipSync.VisemeCount; ++i)
                {
                    CheckVisemeKey(visemeTestKeys[i], i, 1);
                }
            }

            CheckLaughterKey();
        }*/

       


       /* void CheckVisemeKey(KeyCode key, int viseme, int amount)
        {
            if (Input.GetKeyDown(key))
            {
                lipsyncContext.SetVisemeBlend(visemeToBlendTargets[viseme], amount);
            }
            if (Input.GetKeyUp(key))
            {
                lipsyncContext.SetVisemeBlend(visemeToBlendTargets[viseme], 0);
            }
        }*/

      /*  void CheckLaughterKey()
        {
            if (Input.GetKeyDown(laughterKey))
            {
                lipsyncContext.SetLaughterBlend(1);
            }
            if (Input.GetKeyUp(laughterKey))
            {
                lipsyncContext.SetLaughterBlend(0);
            }
        }*/


        










        void OnDestroy()
        {
            if (expressionController != null)
            {
                expressionController.OnExpressionReady -= HandleExpressionReady;
            }



        }


        [System.Serializable]
        public class AnimationResponse
        {
            public string emotion_type;
            public float emotion_intensity;
            public string message_length_category;
            public string animation_variant;
        }


     
















    }








    






    }







