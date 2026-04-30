using FirstGearGames.LobbyAndWorld.Lobbies.JoinCreateRoomCanvases;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Linq;
using UnityEngine;
using static OVRLipSync;

public class LipSyncNetworkBridge : NetworkBehaviour
{
    [Tooltip("Skinned Mesh Rendered target to be driven by Oculus Lipsync")]
    public SkinnedMeshRenderer skinnedMeshRenderer = null;

    [Tooltip("Blendshape index to trigger for each viseme.")]
    public int[] visemeToBlendTargets = Enumerable.Range(0, OVRLipSync.VisemeCount).ToArray();

    [Tooltip("Enable using the test keys defined below to manually trigger each viseme.")]
    public bool enableVisemeTestKeys = false;

    public KeyCode[] visemeTestKeys =
    {
        KeyCode.BackQuote, KeyCode.Tab, KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T,
        KeyCode.Y, KeyCode.U, KeyCode.I, KeyCode.O, KeyCode.P, KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.Backslash
    };

    [Tooltip("Test key used to manually trigger laughter")]
    public KeyCode laughterKey = KeyCode.CapsLock;

    [Tooltip("Blendshape index to trigger for laughter")]
    public int laughterBlendTarget = OVRLipSync.VisemeCount;

    [Range(0.0f, 1.0f)]
    public float laughterThreshold = 0.5f;

    [Range(0.0f, 3.0f)]
    public float laughterMultiplier = 1.5f;

    [Range(1, 100)]
    public int smoothAmount = 70;

    private OVRLipSyncContextBase lipsyncContext = null;

    public bool isclientactive = false;
   

    public delegate void VisemeDataUpdated(float[] visemes);
    public delegate void LaughterDataUpdated(float laughter);

    public event System.Action<float[]> OnVisemeDataUpdated;
    public event System.Action<float> OnLaughterDataUpdated;

    void Start()
    {
        skinnedMeshRenderer = gameObject.transform.parent.GetChild(5).GetComponentInChildren<SkinnedMeshRenderer>();

        if (skinnedMeshRenderer == null)
        {
            Debug.LogError("Please set the target Skinned Mesh Renderer!");
            return;
        }

        lipsyncContext = transform.parent.GetChild(5).GetComponent<OVRLipSyncContextBase>();
        if (lipsyncContext == null)
        {
            Debug.LogError("No OVRLipSyncContext component found!");
        }
        else
        {
            lipsyncContext.Smoothing = smoothAmount;
        }

         


    }

    void Update()
    {
        if (lipsyncContext != null && skinnedMeshRenderer != null)
        {
            OVRLipSync.Frame frame = lipsyncContext.GetCurrentPhonemeFrame();
            if (frame != null)
            {
                SetVisemeToMorphTarget(frame);
                SetLaughterToMorphTarget(frame);

                OnVisemeDataUpdated?.Invoke(frame.Visemes);
                OnLaughterDataUpdated?.Invoke(frame.laughterScore);
            }

            CheckForKeys();

            if (smoothAmount != lipsyncContext.Smoothing)
            {
                lipsyncContext.Smoothing = smoothAmount;
            }
        }
    }

    void CheckForKeys()
    {
        if (enableVisemeTestKeys)
        {
            for (int i = 0; i < OVRLipSync.VisemeCount; ++i)
            {
                CheckVisemeKey(visemeTestKeys[i], i, 1);
            }
        }

        CheckLaughterKey();
    }

    void SetVisemeToMorphTarget(OVRLipSync.Frame frame)
    {




        for (int i = 0; i < visemeToBlendTargets.Length; i++)
        {
            if (visemeToBlendTargets[i] != -1)
            {
                if (IsOwner) // Only update if we own the object
                {
                    skinnedMeshRenderer.SetBlendShapeWeight(visemeToBlendTargets[i], frame.Visemes[i] * 1.0f);


                    if (isclientactive == true)
                    {

                        SetVisemeDataOnNetwork(frame.Visemes);



                    }

                        

                    
                }
            }
        }
    }

    void SetLaughterToMorphTarget(OVRLipSync.Frame frame)
    {
        if (laughterBlendTarget != -1)
        {
            float laughterScore = frame.laughterScore;
            laughterScore = laughterScore < laughterThreshold ? 0.0f : laughterScore - laughterThreshold;
            laughterScore = Mathf.Min(laughterScore * laughterMultiplier, 1.0f);
            laughterScore *= 1.0f / laughterThreshold;

            if (IsOwner) // Only update if we own the object
            {
                skinnedMeshRenderer.SetBlendShapeWeight(laughterBlendTarget, laughterScore);


                if (isclientactive == true)
                {

                    SetLaughterDataOnNetwork(laughterScore);


                }
                

                  
            }
        }
    }

    [ServerRpc]
    void SetVisemeDataOnNetwork(float[] visemes)
    {
        SetVisemeDataOnClients(visemes);
       
    }

    [ObserversRpc]
    void SetVisemeDataOnClients(float[] visemes)
    {

       


        if (!IsOwner)
        {

            ApplyVisemeData(visemes);
        }

    }

    void ApplyVisemeData(float[] visemes)
    {
        for (int i = 0; i < visemeToBlendTargets.Length; i++)
        {
            if (visemeToBlendTargets[i] != -1)
            {
                skinnedMeshRenderer.SetBlendShapeWeight(visemeToBlendTargets[i], visemes[i] * 1.0f);
            }
        }
    }

    [ServerRpc]
    void SetLaughterDataOnNetwork(float laughter)
    {
        SetLaughterDataOnClients(laughter);
      
    }

    [ObserversRpc]
    void SetLaughterDataOnClients(float laughter)
    {



        if (!IsOwner)
        {

            skinnedMeshRenderer.SetBlendShapeWeight(laughterBlendTarget, laughter);
        }

      
        
    }


    void CheckVisemeKey(KeyCode key, int viseme, int amount)
    {
        if (Input.GetKeyDown(key))
        {
            lipsyncContext.SetVisemeBlend(visemeToBlendTargets[viseme], amount);
        }
        if (Input.GetKeyUp(key))
        {
            lipsyncContext.SetVisemeBlend(visemeToBlendTargets[viseme], 0);
        }
    }

    void CheckLaughterKey()
    {
        if (Input.GetKeyDown(laughterKey))
        {
            lipsyncContext.SetLaughterBlend(1);
        }
        if (Input.GetKeyUp(laughterKey))
        {
            lipsyncContext.SetLaughterBlend(0);
        }
    }
}
