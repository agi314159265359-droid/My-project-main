
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;
using System.Threading;

using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Component.Animating;
using OpenAI;
using GLTFast;
using ReadyPlayerMe.Core;
using UnityEngine.Networking;







namespace Mikk.Avatar.Expression
{

    /*
        public class AvatarAssigner : NetworkBehaviour
        {
            [Header("Flutter Package")]
            [SerializeField] private string flutterPackageName = "com.mikk.mikk";

            [Header("References")]
            [SerializeField] private Slider loadingBar;
            [SerializeField] private TextMeshProUGUI loadingText;
            [SerializeField] private RuntimeAnimatorController animatorController;

            [Header("Avatar Types")]
            [SerializeField] private UnityEngine.Avatar MaleAvatar;
            [SerializeField] private UnityEngine.Avatar FemaleAvatar;

            private GameObject loadedAvatar;
            private bool isLoading;
            private bool hasLoaded;
            private string loadedGlbFileName = "";
            private string avatarurls = "";

            // SyncVar with server-only write permission
            private readonly SyncVar<string> avatarurl_nw = new SyncVar<string>(
                new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers)
            );

            #region Lifecycle

            private void Awake()
            {
                avatarurl_nw.OnChange += OnAvatarUrlChanged;
            }

            private void OnDestroy()
            {
                avatarurl_nw.OnChange -= OnAvatarUrlChanged;
                CleanupAvatar();
            }

            #endregion

            #region Server API

            /// <summary>
            /// Called by SampleManager on SERVER only.
            /// Sets SyncVar → triggers OnChange on all clients.
            /// </summary>
            public void SetAvatarUrl(string _avatarUrl)
            {
                if (!IsServerInitialized)
                    return;

                if (string.IsNullOrEmpty(_avatarUrl))
                {
                    Debug.LogError("[AvatarAssigner] SetAvatarUrl: empty URL!");
                    return;
                }

                Debug.Log($"[AvatarAssigner] Server setting: {_avatarUrl}");
                avatarurl_nw.Value = _avatarUrl;
                // Don't load here - let OnChange handle it for ALL cases
            }

            #endregion

            #region Sync Handling

            /// <summary>
            /// Fires on ALL clients (and host) when SyncVar changes.
            /// Also fires on initial sync for late joiners.
            /// </summary>
            private void OnAvatarUrlChanged(string prev, string next, bool asServer)
            {
                Debug.Log($"[AvatarAssigner] SyncVar: '{prev}' → '{next}' (asServer: {asServer}, IsClient: {IsClientInitialized})");

                // Skip if dedicated server only (no client, no local files)
                if (asServer && !IsClientInitialized)
                    return;

                if (string.IsNullOrEmpty(next))
                    return;

                avatarurls = next;
                TryLoadAvatar(next);
            }

            /// <summary>
            /// Late joiner safety net.
            /// If SyncVar already has value when client starts.
            /// </summary>
            public override void OnStartClient()
            {
                base.OnStartClient();

                if (string.IsNullOrEmpty(avatarurl_nw.Value))
                    return;

                // Skip if OnChange already handled it
                if (hasLoaded && loadedGlbFileName == avatarurl_nw.Value)
                    return;

                if (isLoading)
                    return;

                Debug.Log($"[AvatarAssigner] OnStartClient: '{avatarurl_nw.Value}'");
                avatarurls = avatarurl_nw.Value;
                TryLoadAvatar(avatarurl_nw.Value);
            }

            #endregion

            #region Avatar Loading

            /// <summary>
            /// Single entry point - prevents ALL duplicate loads
            /// </summary>
            private void TryLoadAvatar(string fileName)
            {
                if (isLoading)
                {
                    Debug.Log($"[AvatarAssigner] Already loading, skip '{fileName}'");
                    return;
                }

                if (hasLoaded && loadedGlbFileName == fileName)
                {
                    Debug.Log($"[AvatarAssigner] Already loaded '{fileName}', skip");
                    return;
                }

                LoadAvatar(fileName);
            }

            private async void LoadAvatar(string fileName)
            {
                if (isLoading)
                    return;

                isLoading = true;
                ShowLoadingUI();

                if (loadedAvatar != null)
                {
                    CleanupAvatarOnly();
                    hasLoaded = false;
                }

                Debug.Log($"[AvatarAssigner] Loading: {fileName}");

                // Find the file
                string fullPath = FindAvatarFile(fileName);

                if (string.IsNullOrEmpty(fullPath))
                {
                    Debug.LogError($"[AvatarAssigner] Not found: {fileName}");
                    HideLoadingUI();
                    isLoading = false;
                    return;
                }

                FileInfo fileInfo = new FileInfo(fullPath);
                Debug.Log($"[AvatarAssigner] Found: {fullPath} ({fileInfo.Length / 1024:F2} KB)");

                // Load GLB
                var gltf = new GltfImport();
                bool success = await gltf.Load(fullPath);

                if (!success)
                {
                    Debug.LogError($"[AvatarAssigner] GLB load failed: {fullPath}");
                    HideLoadingUI();
                    isLoading = false;
                    return;
                }

                // Instantiate
                GameObject parent = new GameObject("LoadedAvatar");
                bool instantiated = await gltf.InstantiateMainSceneAsync(parent.transform);

                if (!instantiated)
                {
                    Debug.LogError("[AvatarAssigner] Instantiation failed!");
                    Destroy(parent);
                    HideLoadingUI();
                    isLoading = false;
                    return;
                }

                // Position
                parent.transform.SetParent(transform);
                parent.transform.SetSiblingIndex(2);
                parent.transform.localPosition = Vector3.zero;
                parent.transform.localRotation = Quaternion.Euler(0, -90, 0);
                parent.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);

                loadedAvatar = parent;
                loadedGlbFileName = fileName;
                hasLoaded = true;

                SetupAvatarComponents(parent);

                Debug.Log($"[AvatarAssigner] ✅ '{fileName}' loaded!");

                HideLoadingUI();
                isLoading = false;
            }

            /// <summary>
            /// Search Flutter paths first, then Unity path as fallback
            /// </summary>
            private string FindAvatarFile(string fileName)
            {
                string[] possibleDirs = new string[]
                {
                    Path.Combine($"/data/user/0/{flutterPackageName}/app_flutter", "avatars"),
                    Path.Combine($"/data/data/{flutterPackageName}/app_flutter", "avatars"),
                    Path.Combine(Application.persistentDataPath, "avatars"),
                };

                foreach (string dir in possibleDirs)
                {
                    if (!Directory.Exists(dir))
                        continue;

                    // Exact match
                    string exactPath = Path.Combine(dir, fileName);
                    if (File.Exists(exactPath))
                        return exactPath;

                    // Case-insensitive fallback
                    string[] files = Directory.GetFiles(dir, "*.glb");
                    foreach (string file in files)
                    {
                        if (Path.GetFileName(file).Equals(fileName, System.StringComparison.OrdinalIgnoreCase))
                            return file;
                    }
                }

                // Debug output
                Debug.LogError($"[AvatarAssigner] '{fileName}' not found anywhere:");
                foreach (string dir in possibleDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        string[] files = Directory.GetFiles(dir, "*.glb");
                        Debug.Log($"  {dir} ({files.Length} files):");
                        foreach (string f in files)
                            Debug.Log($"    - {Path.GetFileName(f)}");
                    }
                    else
                    {
                        Debug.Log($"  {dir} (NOT FOUND)");
                    }
                }

                return null;
            }

            #endregion

            #region Avatar Components

            private void SetupAvatarComponents(GameObject avatar)
            {
                Animator animator = avatar.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = avatar.AddComponent<Animator>();

                if (animator != null && animatorController != null)
                {
                    animator.runtimeAnimatorController = animatorController;
                }
                else
                {
                    var parentAnimator = GetComponent<Animator>();
                    if (parentAnimator != null && parentAnimator.runtimeAnimatorController != null)
                    {
                        animator.runtimeAnimatorController = parentAnimator.runtimeAnimatorController;
                        animator.avatar = parentAnimator.avatar;
                    }
                }

                var networkAnimator = GetComponent<NetworkAnimator>();
                if (networkAnimator != null && animator != null)
                    networkAnimator.SetAnimator(animator);

                if (avatar.GetComponent<OVRLipSyncContext>() == null)
                    avatar.AddComponent<OVRLipSyncContext>();

                if (avatar.GetComponent<EyesAnimationHandler>() == null)
                    avatar.AddComponent<EyesAnimationHandler>();

                if (avatar.GetComponent<LookAtMe>() == null)
                    avatar.AddComponent<LookAtMe>();

                var gpt = GetComponentInChildren<ChatGPT>();
                if (gpt != null)
                    gpt.enabled = true;

                var playaudio = GetComponentInChildren<Playaudio>();
                if (playaudio != null && animator != null)
                {
                    playaudio.enabled = true;

                    string lowerName = loadedGlbFileName.ToLower();

                    if (lowerName.Contains("male") && !lowerName.Contains("female"))
                    {
                        playaudio.ttvoice = "X0Kc6dUd5Kws5uwEyOnL";
                        animator.avatar = MaleAvatar;
                    }
                    else if (lowerName.Contains("female"))
                    {
                        playaudio.ttvoice = "ulZgFXalzbrnPUGQGs0S";
                        animator.avatar = FemaleAvatar;
                    }
                }

                Debug.Log("[AvatarAssigner] ✅ Components done!");
            }

            #endregion

            #region Loading UI

            private void ShowLoadingUI()
            {
                if (loadingBar != null)
                {
                    loadingBar.gameObject.SetActive(true);
                    loadingBar.value = 0f;
                    StartCoroutine(AnimateLoadingBar());
                }

                if (loadingText != null)
                {
                    loadingText.gameObject.SetActive(true);
                    loadingText.text = "Loading Avatar...";
                }
            }

            private void HideLoadingUI()
            {
                if (loadingBar != null)
                {
                    loadingBar.gameObject.SetActive(false);
                    StopAllCoroutines();
                }

                if (loadingText != null)
                    loadingText.gameObject.SetActive(false);
            }

            private IEnumerator AnimateLoadingBar()
            {
                while (loadingBar != null && loadingBar.gameObject.activeInHierarchy)
                {
                    loadingBar.value = Mathf.PingPong(Time.time * 0.5f, 1f);
                    yield return null;
                }
            }

            #endregion

            #region Cleanup

            private void CleanupAvatarOnly()
            {
                if (loadedAvatar != null)
                {
                    loadedAvatar.transform.SetParent(null);
                    Destroy(loadedAvatar);
                    loadedAvatar = null;
                }
            }

            public void CleanupAvatar()
            {
                CleanupAvatarOnly();
                HideLoadingUI();
                isLoading = false;
                hasLoaded = false;
                loadedGlbFileName = "";
            }



            // Keep for backward compatibility


            #endregion
        }

    }*/


    /*


        public class AvatarAssigner : NetworkBehaviour
        {
            [Header("GLB Settings")]
            [SerializeField] private string glbFileName = "male.glb";

            [Header("References")]
            [SerializeField] private Slider loadingBar;
            [SerializeField] private TextMeshProUGUI loadingText;
            [SerializeField] private RuntimeAnimatorController animatorController;

            [Header("Network")]
            [SerializeField] private string avatarurls;

            private GameObject loadedAvatar;
            private bool isLoading = false;
            private readonly SyncVar<string> avatarurl_nw = new SyncVar<string>();
            private string lastKnownUrl;

            [SerializeField] private UnityEngine.Avatar MaleAvatar;
            [SerializeField] private UnityEngine.Avatar FemaleAvatar;

            async void Start()
            {
                await LoadGLB();
            }

            private async System.Threading.Tasks.Task LoadGLB()
            {
                if (isLoading) return;

                isLoading = true;
                ShowLoadingUI();

                // ✅ FIXED: Use correct path for builds
                string path = GetGLBFilePath(glbFileName);

                Debug.Log($"[AvatarAssigner] ==================");
                Debug.Log($"[AvatarAssigner] Platform: {Application.platform}");
                Debug.Log($"[AvatarAssigner] Is Editor: {Application.isEditor}");
                Debug.Log($"[AvatarAssigner] dataPath: {Application.dataPath}");
                Debug.Log($"[AvatarAssigner] streamingAssetsPath: {Application.streamingAssetsPath}");
                Debug.Log($"[AvatarAssigner] Resolved path: {path}");
                Debug.Log($"[AvatarAssigner] File exists: {File.Exists(path)}");
                Debug.Log($"[AvatarAssigner] ==================");

                // ✅ Check if file exists
                if (!File.Exists(path))
                {
                    Debug.LogError($"[AvatarAssigner] ❌ GLB file not found at: {path}");
                    Debug.LogError($"[AvatarAssigner] Please place '{glbFileName}' in StreamingAssets folder!");

                    // List files in StreamingAssets for debugging
                    ListStreamingAssetsFiles();

                    HideLoadingUI();
                    isLoading = false;
                    return;
                }

                // Create a new GltfImport instance
                var gltf = new GltfImport();

                // Load the GLB file
                bool success = await gltf.Load(path);

                if (success)
                {
                    Debug.Log($"[AvatarAssigner] GLB loaded successfully, instantiating...");

                    // Create a parent GameObject for the GLB

                    // Instantiate the loaded model into the parent
                    bool instantiated = await gltf.InstantiateMainSceneAsync(transform);

                    if (instantiated)
                    {
                        Transform glbChild = transform.GetChild(transform.childCount - 1);



                        // Set sibling index to 2 (3rd child, 0-indexed)
                        glbChild.SetSiblingIndex(2);

                        // Reset local transform
                        glbChild.localPosition = Vector3.zero;
                        glbChild.localRotation = Quaternion.Euler(0, -90, 0);
                        glbChild.localScale = Vector3.one;

                        // Store reference
                        loadedAvatar = glbChild.gameObject;

                        // Setup avatar components
                        SetupAvatarComponents(glbChild.gameObject);


                        HideLoadingUI();
                    }
                    else
                    {
                        Debug.LogError("[AvatarAssigner] ❌ Failed to instantiate GLB model!");
                        HideLoadingUI();
                    }
                }
                else
                {
                    Debug.LogError("[AvatarAssigner] ❌ Failed to load GLB file from: " + path);
                    HideLoadingUI();
                }

                isLoading = false;
            }

            // ✅ NEW: Get correct path based on platform
            private string GetGLBFilePath(string fileName)
            {
    #if UNITY_EDITOR
                // In Editor: Try StreamingAssets first, then Assets/OG folder
                string streamingPath = Path.Combine(Application.streamingAssetsPath, fileName);

                if (File.Exists(streamingPath))
                {
                    Debug.Log($"[AvatarAssigner] Using StreamingAssets path (Editor)");
                    return streamingPath;
                }

                // Fallback to Assets/OG folder for editor
                string ogPath = Path.Combine(Application.dataPath, "OG", fileName);
                if (File.Exists(ogPath))
                {
                    Debug.Log($"[AvatarAssigner] Using Assets/OG path (Editor)");
                    return ogPath;
                }

                // Default to StreamingAssets
                Debug.LogWarning($"[AvatarAssigner] File not found, returning StreamingAssets path");
                return streamingPath;
    #else
            // In Build: Always use StreamingAssets
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            Debug.Log($"[AvatarAssigner] Using StreamingAssets path (Build)");
            return path;
    #endif
            }

            // ✅ NEW: List files in StreamingAssets for debugging
            private void ListStreamingAssetsFiles()
            {
                try
                {
                    string streamingPath = Application.streamingAssetsPath;
                    Debug.Log($"[AvatarAssigner] Listing files in: {streamingPath}");

                    if (Directory.Exists(streamingPath))
                    {
                        string[] files = Directory.GetFiles(streamingPath, "*.glb");
                        Debug.Log($"[AvatarAssigner] Found {files.Length} GLB files:");
                        foreach (string file in files)
                        {
                            FileInfo info = new FileInfo(file);
                            Debug.Log($"  - {Path.GetFileName(file)} ({info.Length / 1024}KB)");
                        }

                        if (files.Length == 0)
                        {
                            Debug.LogWarning($"[AvatarAssigner] No .glb files found in StreamingAssets!");
                        }
                    }
                    else
                    {
                        Debug.LogError($"[AvatarAssigner] StreamingAssets directory doesn't exist!");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[AvatarAssigner] Error listing files: {ex.Message}");
                }
            }

            public void SetAvatarUrl(string _avatarUrl)
            {
                avatarurls = _avatarUrl;
                avatarurl_nw.Value = _avatarUrl;
                lastKnownUrl = _avatarUrl;
            }

            private void SetupAvatarComponents(GameObject avatar)
            {
                Debug.Log("[AvatarAssigner] Setting up avatar components...");

                // ✅ Setup Animator
                Animator animator = avatar.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    // Try to add animator to the avatar root
                    animator = avatar.AddComponent<Animator>();


                    Debug.Log("[AvatarAssigner] Added Animator component");
                }
                else
                {
                    Debug.Log($"[AvatarAssigner] Found Animator on: {animator.gameObject.name}");
                }

                // Set Runtime Animator Controller
                if (animator != null)
                {
                    if (animatorController != null)
                    {
                        animator.runtimeAnimatorController = animatorController;
                        animator.avatar = MaleAvatar;
                        Debug.Log("[AvatarAssigner] ✅ Animator controller set");
                    }
                    else
                    {
                        // Try to get from parent
                        var parentAnimator = GetComponent<Animator>();
                        if (parentAnimator != null && parentAnimator.runtimeAnimatorController != null)
                        {
                            animator.runtimeAnimatorController = parentAnimator.runtimeAnimatorController;
                            animator.avatar = MaleAvatar;


                            Debug.Log("[AvatarAssigner] ✅ Animator controller copied from parent");
                        }
                        else
                        {
                            Debug.LogWarning("[AvatarAssigner] ⚠️ No animator controller assigned!");
                        }
                    }
                }

                // ✅ Setup Network Animator
                var networkAnimator = GetComponent<NetworkAnimator>();
                if (networkAnimator != null && animator != null)
                {
                    networkAnimator.SetAnimator(animator);
                    Debug.Log("[AvatarAssigner] ✅ NetworkAnimator set");
                }

                // ✅ Add OVRLipSyncContext
              *//*  var lipSync = avatar.GetComponent<OVRLipSyncContext>();
                if (lipSync == null)
                {
                    avatar.AddComponent<OVRLipSyncContext>();
                    Debug.Log("[AvatarAssigner] ✅ Added OVRLipSyncContext");
                }*//*

                // ✅ Add EyeAnimationHandler
               *//* var avatarexpress = avatar.GetComponent<AvatarExpressionController>();
                if (avatarexpress == null)
                {
                    avatar.AddComponent<AvatarExpressionController>();
                    avatar.AddComponent<LookAtMe>();
                    Debug.Log("[AvatarAssigner] ✅ Added EyeAnimationHandler");
                }*//*

                // ✅ Setup ChatGPT and Playaudio
                var facedriver = GetComponentInChildren<RealtimeFaceDriver>();
                var headMotionController = GetComponentInChildren<HeadMotionController>();
                var bodyanimationcontroller = GetComponentInChildren<BodyAnimationController>();
                var stremojiLipSync = GetComponentInChildren<StreamojiLipSyncBridge>();


                if (facedriver != null)
                {
                  facedriver.enabled = true;
                    facedriver.faceMesh = avatar.transform.GetChild(3).GetComponent<SkinnedMeshRenderer>();
                    facedriver.eyeLeftMesh = avatar.transform.GetChild(1).GetComponent<SkinnedMeshRenderer>();
                    facedriver.eyeRightMesh = avatar.transform.GetChild(2).GetComponent<SkinnedMeshRenderer>();

                }


                if (headMotionController != null)
                {
                    headMotionController.enabled = true;
                    headMotionController.headBone = FindChildRecursive(avatar.transform, "Head");
                    headMotionController.neckBone = FindChildRecursive(avatar.transform, "Neck");

                }

                if (bodyanimationcontroller != null)
                {
                    bodyanimationcontroller.enabled = true;
                    bodyanimationcontroller.animator = animator;
                    bodyanimationcontroller.spineBone = FindChildRecursive(avatar.transform, "Spine");
                    bodyanimationcontroller.spine1Bone = FindChildRecursive(avatar.transform, "Spine1");
                    bodyanimationcontroller.spine2Bone = FindChildRecursive(avatar.transform, "Spine2");
                    bodyanimationcontroller.leftArmBone = FindChildRecursive(avatar.transform, "LeftArm");
                    bodyanimationcontroller.rightArmBone = FindChildRecursive(avatar.transform, "RightArm");
                    bodyanimationcontroller.leftForeArmBone = FindChildRecursive(avatar.transform, "LeftForeArm");
                    bodyanimationcontroller.rightForeArmBone = FindChildRecursive(avatar.transform, "RightForeArm");
                    bodyanimationcontroller.leftHandBone = FindChildRecursive(avatar.transform, "LeftHand");
                    bodyanimationcontroller.rightHandBone = FindChildRecursive(avatar.transform, "RightHand");

                }


                if (stremojiLipSync != null)
                {
                    stremojiLipSync.enabled = true;
                    stremojiLipSync.skinnedMeshRenderer = avatar.transform.GetChild(3).GetComponent<SkinnedMeshRenderer>();

                }


                Debug.Log("[AvatarAssigner] ✅ Avatar setup complete!");
            }



            // ✅ Loading UI Methods
            private void ShowLoadingUI()
            {
                if (loadingBar != null)
                {
                    loadingBar.gameObject.SetActive(true);
                    loadingBar.value = 0f;
                    StartCoroutine(AnimateLoadingBar());
                }

                if (loadingText != null)
                {
                    loadingText.gameObject.SetActive(true);
                    loadingText.text = "Loading Avatar...";
                }

                Debug.Log("[AvatarAssigner] Loading UI shown");
            }

            private void HideLoadingUI()
            {
                if (loadingBar != null)
                {
                    loadingBar.gameObject.SetActive(false);
                    StopAllCoroutines();
                }

                if (loadingText != null)
                {
                    loadingText.gameObject.SetActive(false);
                }

                Debug.Log("[AvatarAssigner] Loading UI hidden");
            }

            private IEnumerator AnimateLoadingBar()
            {
                while (loadingBar != null && loadingBar.gameObject.activeInHierarchy)
                {
                    // Ping-pong animation for loading bar
                    loadingBar.value = Mathf.PingPong(Time.time * 0.5f, 1f);
                    yield return null;
                }
            }

            // ✅ Cleanup Method
            public void CleanupAvatar()
            {
                Debug.Log("[AvatarAssigner] Cleaning up avatar...");

                if (loadedAvatar != null)
                {
                    // Unparent first
                    if (loadedAvatar.transform.parent != null)
                    {
                        loadedAvatar.transform.SetParent(null);
                    }

                    // Destroy the avatar
                    if (Application.isPlaying)
                    {
                        Destroy(loadedAvatar);
                    }
                    else
                    {
                        DestroyImmediate(loadedAvatar);
                    }

                    loadedAvatar = null;
                    Debug.Log("[AvatarAssigner] ✅ Avatar cleaned up successfully");
                }
                else
                {
                    Debug.Log("[AvatarAssigner] No avatar to cleanup");
                }

                // Make sure loading UI is hidden
                HideLoadingUI();
                isLoading = false;
            }


            Transform FindChildRecursive(Transform parent, string targetName)
            {
                foreach (Transform child in parent)
                {
                    if (child.name == targetName) return child;

                    Transform result = FindChildRecursive(child, targetName);
                    if (result != null) return result;
                }
                return null;
            }

            // ✅ Public method to reload avatar
            public async void ReloadAvatar(string newGlbFileName)
            {
                Debug.Log($"[AvatarAssigner] Reloading avatar with: {newGlbFileName}");

                // Cleanup existing avatar
                CleanupAvatar();

                // Set new filename
                glbFileName = newGlbFileName;

                // Load new avatar
                await LoadGLB();
            }

            // ✅ Context Menu helpers
            [ContextMenu("Reload Current Avatar")]
            private void ReloadCurrentAvatar()
            {
                ReloadAvatar(glbFileName);
            }

            [ContextMenu("Cleanup Avatar")]
            private void CleanupAvatarMenu()
            {
                CleanupAvatar();
            }

            [ContextMenu("Show File Paths")]
            private void ShowFilePaths()
            {
                Debug.Log("=== FILE PATHS DEBUG ===");
                Debug.Log($"Platform: {Application.platform}");
                Debug.Log($"Is Editor: {Application.isEditor}");
                Debug.Log($"GLB Filename: {glbFileName}");
                Debug.Log($"");
                Debug.Log($"Application.dataPath: {Application.dataPath}");
                Debug.Log($"Application.streamingAssetsPath: {Application.streamingAssetsPath}");
                Debug.Log($"");

                string glbPath = GetGLBFilePath(glbFileName);
                Debug.Log($"GLB Path: {glbPath}");
                Debug.Log($"File Exists: {File.Exists(glbPath)}");

                if (File.Exists(glbPath))
                {
                    FileInfo info = new FileInfo(glbPath);
                    Debug.Log($"File Size: {info.Length / 1024}KB");
                }

                Debug.Log($"");
                ListStreamingAssetsFiles();
                Debug.Log("========================");
            }

            [ContextMenu("Debug Avatar Info")]
            private void DebugAvatarInfo()
            {
                Debug.Log("=== AVATAR ASSIGNER DEBUG INFO ===");
                Debug.Log($"GLB Filename: {glbFileName}");
                Debug.Log($"Is Loading: {isLoading}");
                Debug.Log($"Loaded Avatar: {(loadedAvatar != null ? loadedAvatar.name : "NULL")}");

                if (loadedAvatar != null)
                {
                    Debug.Log($"Avatar Active: {loadedAvatar.activeSelf}");
                    Debug.Log($"Avatar Parent: {(loadedAvatar.transform.parent != null ? loadedAvatar.transform.parent.name : "NULL")}");
                    Debug.Log($"Avatar Sibling Index: {loadedAvatar.transform.GetSiblingIndex()}");
                    Debug.Log($"Avatar Position: {loadedAvatar.transform.position}");
                    Debug.Log($"Avatar Local Position: {loadedAvatar.transform.localPosition}");
                    Debug.Log($"Avatar Rotation: {loadedAvatar.transform.rotation.eulerAngles}");
                    Debug.Log($"Avatar Local Rotation: {loadedAvatar.transform.localRotation.eulerAngles}");
                    Debug.Log($"Avatar Scale: {loadedAvatar.transform.localScale}");
                    Debug.Log($"Avatar Children: {loadedAvatar.transform.childCount}");

                    var animator = loadedAvatar.GetComponentInChildren<Animator>();
                    if (animator != null)
                    {
                        Debug.Log($"Animator Found: {animator.gameObject.name}");
                        Debug.Log($"Animator Avatar: {(animator.avatar != null ? animator.avatar.name : "NULL")}");
                        Debug.Log($"Animator Controller: {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "NULL")}");
                    }
                    else
                    {
                        Debug.Log("No Animator found!");
                    }

                    // Check renderers
                    var renderers = loadedAvatar.GetComponentsInChildren<Renderer>();
                    Debug.Log($"Total Renderers: {renderers.Length}");
                    foreach (var renderer in renderers)
                    {
                        Debug.Log($"  - {renderer.name}: Enabled={renderer.enabled}, Visible={renderer.isVisible}");
                    }
                }

                Debug.Log($"Animator Controller: {(animatorController != null ? animatorController.name : "NULL")}");
                Debug.Log($"Avatar URL: {avatarurls}");
                Debug.Log($"Avatar URL Network: {avatarurl_nw.Value}");
                Debug.Log($"Last Known URL: {lastKnownUrl}");
                Debug.Log("====================================");
            }

            private void OnDestroy()
            {
                // Cleanup when the script is destroyed
                CleanupAvatar();
            }
        }


    }*/

    public class AvatarAssigner : NetworkBehaviour
    {
        [SerializeField] private string avatarurls;
        [SerializeField] private GameObject parentRef;
        [SerializeField] private Transform avatarspawnpostion;

        // Loading UI elements
        [SerializeField] private Slider loadingBar;
        [SerializeField] private TextMeshProUGUI loadingText;

        public float fakeDuration = 5f;

        // ✅ Default SyncVar (ServerOnly write, Observers read)
        private readonly SyncVar<string> avatarurl_nw = new SyncVar<string>();

        private GameObject avatar;
        private bool avatarloaded;
        private volatile bool isLoading;
        private CancellationTokenSource loadingTokenSource;
        private string lastKnownUrl;

        private const string PARENT = "ParentRef";

        private bool isBeingDestroyed = false;
        private bool hasBeenCleaned = false;

        [SerializeField] private UnityEngine.Avatar MaleAvatar;
        [SerializeField] private UnityEngine.Avatar FemaleAvatar;

        private void Start()
        {
            avatarloaded = false;
            isLoading = false;
            lastKnownUrl = "";

            // Hide loading UI initially
            if (loadingBar != null) loadingBar.gameObject.SetActive(false);
            if (loadingText != null) loadingText.gameObject.SetActive(false);

            // ✅ Subscribe to SyncVar changes (better than polling)
            avatarurl_nw.OnChange += OnAvatarUrlChanged;

            // Load initial avatar if URL is already set
            if (!string.IsNullOrEmpty(avatarurl_nw.Value))
            {
                LoadAvatar(avatarurl_nw.Value).Forget();
            }
        }

        // ✅ This replaces your MonitorUrlChanges() polling loop
        private void OnAvatarUrlChanged(string prev, string next, bool asServer)
        {
            if (!string.IsNullOrEmpty(next) && next != lastKnownUrl)
            {
                lastKnownUrl = next;
                LoadAvatar(next).Forget();
            }
        }

        private void ShowLoadingUI()
        {
            if (loadingBar != null)
            {
                loadingBar.gameObject.SetActive(true);
                StartCoroutine(AnimateLoadingBar());
            }
            if (loadingText != null)
            {
                loadingText.gameObject.SetActive(true);
                loadingText.text = "Loading Avatar...";
            }
        }

        private void HideLoadingUI()
        {
            if (loadingBar != null) loadingBar.gameObject.SetActive(false);
            if (loadingText != null) loadingText.gameObject.SetActive(false);
        }

        private IEnumerator AnimateLoadingBar()
        {
            while (loadingBar != null && loadingBar.gameObject.activeInHierarchy)
            {
                loadingBar.value = Mathf.PingPong(Time.time * 0.5f, 1f);
                yield return null;
            }
        }

        private async UniTaskVoid LoadAvatar(string url)
        {
            if (isLoading || string.IsNullOrEmpty(url)) return;

            // Don't reload if it's the same avatar and already loaded
            if (avatarloaded && avatarurls == url && avatar != null) return;

            avatarurls = url;

            // Cancel any existing load operation
            loadingTokenSource?.Cancel();
            loadingTokenSource = new CancellationTokenSource();

            avatarloaded = false;
            isLoading = true;

            // Show loading UI
            ShowLoadingUI();

            try
            {
                await LoadAvatarAsync(url, loadingTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Avatar loading cancelled");
                HideLoadingUI();
                isLoading = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Avatar loading failed: {ex.Message}");
                HideLoadingUI();
                isLoading = false;
            }
        }

        // ✅ NO ServerRpc needed - only called by server in SampleManager
        public void SetAvatarUrl(string _avatarUrl)
        {
            avatarurls = _avatarUrl;
            avatarurl_nw.Value = _avatarUrl;  // Server sets this
            lastKnownUrl = _avatarUrl;
        }

        private string GetAvatarUrl()
        {
            return avatarurl_nw.Value;
        }

        private async UniTask LoadAvatarAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                // Clean up existing avatar on main thread
                if (avatar != null)
                {
                    Destroy(avatar);
                    avatar = null;
                }

                parentRef.name = url;

                // Determine file type from URL
                string fileExtension = GetFileExtension(url);
                GameObject loadedAvatar = null;

                switch (fileExtension.ToLower())
                {
                    case ".glb":
                    case ".gltf":
                        loadedAvatar = await LoadGLTFAvatar(url, cancellationToken);
                        break;
                    case ".fbx":
                        loadedAvatar = await LoadFBXAvatar(url, cancellationToken);
                        break;
                    default:
                        Debug.LogError($"Unsupported file format: {fileExtension}");
                        HideLoadingUI();
                        return;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (loadedAvatar != null)
                {
                    avatar = loadedAvatar;

                    await UniTask.SwitchToMainThread();

                    OutfitGender gender = DetermineGender(url);

                    SetupAvatar(gender,avatar);
                    avatarloaded = true;

                    HideLoadingUI();

                    Debug.Log($"Avatar loaded successfully: {url}");
                }
                else
                {
                    Debug.LogError($"Avatar loading failed: {url}");
                    HideLoadingUI();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading avatar: {ex.Message}");
                HideLoadingUI();
                throw;
            }
            finally
            {
                isLoading = false;
            }
        }

        private async UniTask<GameObject> LoadGLTFAvatar(string url, CancellationToken cancellationToken)
        {
            try
            {
                byte[] data = await DownloadFile(url, cancellationToken);

                if (data == null || data.Length == 0)
                {
                    Debug.LogError("Downloaded GLTF data is empty");
                    return null;
                }

                var gltf = new GltfImport();

                bool success = await gltf.Load(data, cancellationToken: cancellationToken);

                if (!success)
                {
                    Debug.LogError("Failed to parse GLTF data");
                    return null;
                }

                // ✅ Instantiate directly to PlayerPrefab (transform)
                bool instantiated = await gltf.InstantiateMainSceneAsync(transform);

                if (!instantiated)
                {
                    Debug.LogError("Failed to instantiate GLTF scene");
                    return null;
                }

                // ✅ Get the newly added child (last one)
                Transform glbChild = transform.GetChild(transform.childCount - 1);

                // ✅ Set sibling index to position 2 (index 1)
                glbChild.SetSiblingIndex(2);

                // ✅ Reset local transform
                glbChild.localPosition = Vector3.zero;
                glbChild.localRotation = Quaternion.Euler(0,-90,0);
                glbChild.localScale = Vector3.one;

                Debug.Log($"Avatar loaded at sibling index 1 (position 2)");

                return glbChild.gameObject;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading GLTF avatar: {ex.Message}");
                return null;
            }
        }

        private async UniTask<GameObject> LoadFBXAvatar(string url, CancellationToken cancellationToken)
        {
            Debug.LogWarning("FBX runtime loading requires AssetBundles. Convert to GLB instead.");
            return null;
        }

        private async UniTask<byte[]> DownloadFile(string url, CancellationToken cancellationToken)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield();
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    return request.downloadHandler.data;
                }
                else
                {
                    Debug.LogError($"Download failed: {request.error}");
                    return null;
                }
            }
        }

        private string GetFileExtension(string url)
        {
            int queryIndex = url.IndexOf('?');
            if (queryIndex > 0)
            {
                url = url.Substring(0, queryIndex);
            }

            int dotIndex = url.LastIndexOf('.');
            if (dotIndex > 0 && dotIndex < url.Length - 1)
            {
                return url.Substring(dotIndex);
            }

            return ".glb";
        }

        private OutfitGender DetermineGender(string url)
        {
            if (url.ToLower().Contains("female"))
            {
                return OutfitGender.Feminine;
            }

            return OutfitGender.Masculine;
        }

        private void SetupAvatar(OutfitGender gender, GameObject avatar)
        {
            if (avatar == null) return;
            SetupAvatarAsync(gender,avatar).Forget();
        }

        private async UniTaskVoid SetupAvatarAsync(OutfitGender gender, GameObject avatar)
        {
            try
            {
                await UniTask.SwitchToMainThread();

                var animator = avatar.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    animator = avatar.AddComponent<Animator>();
                }

                if (animator != null)
                {
                    var sourceAnimator = transform.GetComponent<Animator>();
                    if (sourceAnimator != null)
                    {
                        animator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;
                    }
                }

                var networkAnimator = transform.GetComponent<NetworkAnimator>();
                if (networkAnimator != null && animator != null)
                {
                    networkAnimator.SetAnimator(animator);
                }


                var ovrlipsync = avatar.GetComponent<OVRLipSyncContext>();
                if (ovrlipsync == null)
                {
                    avatar.AddComponent<OVRLipSyncContext>();
                }

                if (gender == OutfitGender.Masculine)
                {
                   avatar.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);
                    animator.avatar = MaleAvatar;
                } else
                {
                    animator.avatar = FemaleAvatar;

                }

                parentRef.name = PARENT;


                var facedriver = GetComponentInChildren<RealtimeFaceDriver>();
                var headMotionController = GetComponentInChildren<HeadMotionController>();
                var bodyanimationcontroller = GetComponentInChildren<BodyAnimationController>();
                var stremojiLipSync = GetComponentInChildren<StreamojiLipSyncBridge>();
                var voicepipeline = GetComponentInChildren<VoicePipeline>();


                if (facedriver != null)
                {
                    facedriver.enabled = true;
                    facedriver.faceMesh = avatar.transform.GetChild(3).GetComponent<SkinnedMeshRenderer>();
                    facedriver.eyeLeftMesh = avatar.transform.GetChild(1).GetComponent<SkinnedMeshRenderer>();
                    facedriver.eyeRightMesh = avatar.transform.GetChild(2).GetComponent<SkinnedMeshRenderer>();

                }


                if (headMotionController != null)
                {
                    headMotionController.enabled = true;
                    headMotionController.headBone = FindChildRecursive(avatar.transform, "Head");
                    headMotionController.neckBone = FindChildRecursive(avatar.transform, "Neck");

                }

                if (bodyanimationcontroller != null)
                {
                    bodyanimationcontroller.enabled = true;
                    bodyanimationcontroller.animator = animator;
                    bodyanimationcontroller.spineBone = FindChildRecursive(avatar.transform, "Spine");
                    bodyanimationcontroller.spine1Bone = FindChildRecursive(avatar.transform, "Spine1");
                    bodyanimationcontroller.spine2Bone = FindChildRecursive(avatar.transform, "Spine2");
                    bodyanimationcontroller.leftArmBone = FindChildRecursive(avatar.transform, "LeftArm");
                    bodyanimationcontroller.rightArmBone = FindChildRecursive(avatar.transform, "RightArm");
                    bodyanimationcontroller.leftForeArmBone = FindChildRecursive(avatar.transform, "LeftForeArm");
                    bodyanimationcontroller.rightForeArmBone = FindChildRecursive(avatar.transform, "RightForeArm");
                    bodyanimationcontroller.leftHandBone = FindChildRecursive(avatar.transform, "LeftHand");
                    bodyanimationcontroller.rightHandBone = FindChildRecursive(avatar.transform, "RightHand");

                }


                if (stremojiLipSync != null)
                {
                    stremojiLipSync.enabled = true;
                    stremojiLipSync.skinnedMeshRenderer = avatar.transform.GetChild(3).GetComponent<SkinnedMeshRenderer>();

                }

                if(voicepipeline != null)
                {
                    voicepipeline.enabled = true;
                    StartCoroutine(zoomAvatar());
                    
                }



            }
            catch (Exception ex)
            {
                Debug.LogError($"Avatar setup failed: {ex.Message}");
                HideLoadingUI();
            }
        }

        private IEnumerator zoomAvatar()
        {
          var script = UnityEngine.Object.FindAnyObjectByType<CameraController>();

           yield return new WaitForSecondsRealtime(2);

            script.zoomINCamera("hsjj");


        }

        public async UniTask<bool> LoadAvatarAndWait(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            SetAvatarUrl(url);

            while (isLoading)
            {
                await UniTask.Delay(50);
            }

            return avatarloaded && avatarurls == url;
        }

        public bool IsAvatarLoading()
        {
            return isLoading;
        }

        public void CancelCurrentLoading()
        {
            loadingTokenSource?.Cancel();
            HideLoadingUI();
        }

        Transform FindChildRecursive(Transform parent, string targetName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == targetName) return child;

                Transform result = FindChildRecursive(child, targetName);
                if (result != null) return result;
            }
            return null;
        }

        public void CleanupAvatar()
        {
            if (hasBeenCleaned) return;
            hasBeenCleaned = true;

            try
            {
                loadingTokenSource?.Cancel();
                loadingTokenSource?.Dispose();
                loadingTokenSource = null;
            }
            catch { }

            isLoading = false;
            avatarloaded = false;
            lastKnownUrl = "";

            try
            {
                HideLoadingUI();
            }
            catch { }

            if (avatar != null)
            {
                try
                {
                    Destroy(avatar);
                    avatar = null;
                }
                catch { }
            }

            avatarurl_nw.Value = "";
        }

        private void OnDestroy()
        {
            isBeingDestroyed = true;

            avatarurl_nw.OnChange -= OnAvatarUrlChanged;

            if (hasBeenCleaned) return;

            try
            {
                loadingTokenSource?.Cancel();
                loadingTokenSource?.Dispose();
                loadingTokenSource = null;
            }
            catch { }

            isLoading = false;
            avatarloaded = false;

            try
            {
                HideLoadingUI();
            }
            catch { }

            if (avatar != null)
            {
                try
                {
                    if (Application.isPlaying)
                        Destroy(avatar);
                    else
                        DestroyImmediate(avatar);
                    avatar = null;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AvatarAssigner] Error destroying avatar: {ex.Message}");
                }
            }
        }
    }
}

    public enum OutfitGender
    {
        Masculine,
        Feminine
    }




/*public class AvatarAssigner : NetworkBehaviour
{
    [SerializeField] private string avatarurls;
    [SerializeField] private GameObject parentRef;
    [SerializeField] private Transform avatarspawnpostion;

    // Loading UI elements
    [SerializeField] private Slider loadingBar;
    [SerializeField] private TextMeshProUGUI loadingText;

    public float fakeDuration = 5f;
    private readonly SyncVar<string> avatarurl_nw = new SyncVar<string>();
    private GameObject avatar;
    private bool avatarloaded;
    private volatile bool isLoading;
    private CancellationTokenSource loadingTokenSource;
    private string lastKnownUrl;

    private const string PARENT = "ParentRef";

    private bool isBeingDestroyed = false;
    private bool hasBeenCleaned = false;



    private void Start()
    {
        avatarloaded = false;
        isLoading = false;
        lastKnownUrl = "";

        // Hide loading UI initially
        if (loadingBar != null) loadingBar.gameObject.SetActive(false);
        if (loadingText != null) loadingText.gameObject.SetActive(false);

        // Load initial avatar if URL is already set
        if (!string.IsNullOrEmpty(avatarurl_nw.Value))
        {
            LoadAvatar(avatarurl_nw.Value).Forget();
        }

        // Start URL monitoring
        MonitorUrlChanges().Forget();
    }

    private async UniTaskVoid MonitorUrlChanges()
    {
        while (this != null && gameObject != null)
        {
            try
            {
                string currentUrl = GetAvatarUrl();

                if (!string.IsNullOrEmpty(currentUrl) && currentUrl != lastKnownUrl)
                {
                    lastKnownUrl = currentUrl;
                    LoadAvatar(currentUrl).Forget();
                }

                await UniTask.Delay(100, cancellationToken: this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ShowLoadingUI()
    {
        if (loadingBar != null)
        {
            loadingBar.gameObject.SetActive(true);
            StartCoroutine(AnimateLoadingBar());
        }
        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(true);
            loadingText.text = "AI Avatar Initialization...";
        }
    }

    private void HideLoadingUI()
    {
        if (loadingBar != null) loadingBar.gameObject.SetActive(false);
        if (loadingText != null) loadingText.gameObject.SetActive(false);
    }

    private IEnumerator AnimateLoadingBar()
    {
        float timer = 0f;

        while (timer < fakeDuration)
        {
            loadingBar.value = Mathf.PingPong(Time.time * 1f, 1f);

            
           // timer += Time.deltaTime;
            yield return null;
        }


    }

    private async UniTaskVoid LoadAvatar(string url)
    {
        if (isLoading || string.IsNullOrEmpty(url)) return;

        // Don't reload if it's the same avatar and already loaded
        if (avatarloaded && avatarurls == url) return;

        avatarurls = url;

        // Cancel any existing load operation
        loadingTokenSource?.Cancel();
        loadingTokenSource = new CancellationTokenSource();

        avatarloaded = false;

        // Show loading UI
        ShowLoadingUI();

        try
        {
            await LoadAvatarAsync(url, loadingTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Avatar loading cancelled");
            HideLoadingUI();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Avatar loading failed: {ex.Message}");
            HideLoadingUI();
        }
    }

    public void SetAvatarUrl(string _avatarUrl)
    {
        avatarurls = _avatarUrl;
        avatarurl_nw.Value = _avatarUrl;
        lastKnownUrl = _avatarUrl;
    }

    private string GetAvatarUrl()
    {
        return avatarurl_nw.Value;
    }

    private async UniTask LoadAvatarAsync(string url, CancellationToken cancellationToken)
    {
        isLoading = true;

        try
        {
            // Clean up existing avatar on main thread
            if (avatar != null)
            {
                Destroy(avatar);
                avatar = null;
            }

            var avatarLoader = new AvatarObjectLoader();
            var loadingTask = CreateAvatarLoadingTask(avatarLoader, url, cancellationToken);

            parentRef.name = url;
            avatarLoader.LoadAvatar(url);

            var result = await loadingTask;

            cancellationToken.ThrowIfCancellationRequested();

            if (result.success && result.avatar != null)
            {
                avatar = result.avatar;

                await UniTask.SwitchToMainThread();
                SetupAvatar(result.gender);
                avatarloaded = true;

                // Hide loading UI when avatar is loaded
                HideLoadingUI();

              //  Debug.Log($"Avatar loaded successfully: {url}");
            }
            else
            {
                Debug.LogError($"Avatar loading failed: {url}");
                HideLoadingUI();
            }
        }
        finally
        {
            isLoading = false;
        }
    }

    private async UniTask<(bool success, GameObject avatar, OutfitGender gender)> CreateAvatarLoadingTask(
        AvatarObjectLoader avatarLoader, string url, CancellationToken cancellationToken)
    {
        var tcs = new UniTaskCompletionSource<(bool, GameObject, OutfitGender)>();

        avatarLoader.OnCompleted += (_, args) =>
        {
            if (args?.Avatar != null)
            {
                tcs.TrySetResult((true, args.Avatar, args.Metadata.OutfitGender));
            }
            else
            {
                tcs.TrySetResult((false, null, OutfitGender.Masculine));
            }
        };

        avatarLoader.OnFailed += (_, args) =>
        {
            tcs.TrySetResult((false, null, OutfitGender.Masculine));
        };

        cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled();
        });

        try
        {
            return await tcs.Task.Timeout(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            Debug.LogWarning($"Avatar loading timed out: {url}");
            return (false, null, OutfitGender.Masculine);
        }
    }

    private void SetupAvatar(OutfitGender gender)
    {
        if (avatar == null) return;
        SetupAvatarAsync(gender).Forget();
    }

    private async UniTaskVoid SetupAvatarAsync(OutfitGender gender)
    {
        try
        {
            await UniTask.RunOnThreadPool(() =>
            {
                // CPU-intensive setup can go here
            });

            await UniTask.SwitchToMainThread();

            var animator = avatar.GetComponent<Animator>();
            if (animator != null)
            {
                var sourceAnimator = transform.GetComponent<Animator>();
                if (sourceAnimator != null)
                {
                    animator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;
                }
            }

            var networkAnimator = transform.GetComponent<NetworkAnimator>();
            if (networkAnimator != null && animator != null)
            {
                networkAnimator.SetAnimator(animator);
            }

            avatar.AddComponent<OVRLipSyncContext>();
            avatar.AddComponent<EyeAnimationHandler>();

            var gpt = transform.GetComponentInChildren<ChatGPT>();
            var playaudio = transform.GetComponentInChildren<Playaudio>();
            if (gpt != null && playaudio != null)
            {
                gpt.enabled = true;
                playaudio.enabled = true;

                playaudio.ttvoice = gender == OutfitGender.Masculine ?
                    "X0Kc6dUd5Kws5uwEyOnL" : "ulZgFXalzbrnPUGQGs0S";
            }

            if (gender == OutfitGender.Masculine)
            {
                avatar.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);
            }

            if (parentRef.name == avatarurls)
            {
                avatar.transform.parent = transform;
                avatar.transform.SetPositionAndRotation(avatarspawnpostion.position, avatarspawnpostion.rotation);
            }

            parentRef.name = PARENT;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Avatar setup failed: {ex.Message}");
            HideLoadingUI();
        }
    }

    public async UniTask<bool> LoadAvatarAndWait(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        SetAvatarUrl(url);

        while (isLoading)
        {
            await UniTask.Delay(50);
        }

        return avatarloaded && avatarurls == url;
    }

    public bool IsAvatarLoading()
    {
        return isLoading;
    }

    public void CancelCurrentLoading()
    {
        loadingTokenSource?.Cancel();
        HideLoadingUI();
    }


    public void CleanupAvatar()
    {
      //  Debug.Log($"[AvatarAssigner] CleanupAvatar called");

        // Just cancel and cleanup, no fancy logic
        try
        {
            loadingTokenSource?.Cancel();
            loadingTokenSource?.Dispose();
            loadingTokenSource = null;
        }
        catch { }

        isLoading = false;
        avatarloaded = false;
        lastKnownUrl = "";

        try
        {
            HideLoadingUI();
        }
        catch { }

        if (avatar != null)
        {
            try
            {
                Destroy(avatar);
                avatar = null;
            }
            catch { }
        }

        avatarurl_nw.Value = "";




    }







    private void OnDestroy()
    {


        isBeingDestroyed = true;

        Debug.Log($"[AvatarAssigner] OnDestroy - Owner: {(base.Owner != null ? base.Owner.ClientId.ToString() : "null")}, HasBeenCleaned: {hasBeenCleaned}");

        // If already cleaned up, skip most of this
        if (hasBeenCleaned)
        {
            Debug.Log($"[AvatarAssigner] OnDestroy skipped - already cleaned");
            return;
        }

        // Cancel all async operations
        try
        {
            loadingTokenSource?.Cancel();
            loadingTokenSource?.Dispose();
            loadingTokenSource = null;
        }
        catch { *//* Ignore errors during destroy *//* }

        // Reset state
        isLoading = false;
        avatarloaded = false;

        // Hide UI
        try
        {
            HideLoadingUI();
        }
        catch { *//* Ignore errors during destroy *//* }

        // Only destroy avatar GameObject if we're not already being destroyed by network
        if (avatar != null)
        {
            try
            {
                if (Application.isPlaying)
                    Destroy(avatar);
                else
                    DestroyImmediate(avatar);
                avatar = null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AvatarAssigner] Error destroying avatar in OnDestroy: {ex.Message}");
            }
        }
    }*/





/*  loadingTokenSource?.Cancel();
  loadingTokenSource?.Dispose();

  HideLoadingUI();

  if (avatar != null)
  {
      if (Application.isPlaying)
          Destroy(avatar);
      else
          DestroyImmediate(avatar);
  }*/



















/*public class AvatarAssigner : NetworkBehaviour
{
    [SerializeField] private string avatarurls;
    [SerializeField] private GameObject parentRef;
    [SerializeField] private Transform avatarspawnpostion;


    private readonly SyncVar<string> avatarurl_nw = new SyncVar<string>();
    private GameObject avatar;
    private bool avatarloaded;
    private volatile bool isLoading;
    private CancellationTokenSource loadingTokenSource;
    private string lastKnownUrl; // Track URL changes manually

    private const string PARENT = "ParentRef";

    private void Start()
    {
        avatarloaded = false;
        isLoading = false;
        lastKnownUrl = "";

        // Load initial avatar if URL is already set
        if (!string.IsNullOrEmpty(avatarurl_nw.Value))
        {
            LoadAvatar(avatarurl_nw.Value).Forget();
        }

        // Start URL monitoring
        MonitorUrlChanges().Forget();
    }

    // Replace Update with efficient monitoring
    private async UniTaskVoid MonitorUrlChanges()
    {
        while (this != null && gameObject != null)
        {
            try
            {
                string currentUrl = GetAvatarUrl();

                if (!string.IsNullOrEmpty(currentUrl) && currentUrl != lastKnownUrl)
                {
                    lastKnownUrl = currentUrl;
                    LoadAvatar(currentUrl).Forget();
                }

                // Check every 100ms instead of every frame
                await UniTask.Delay(100, cancellationToken: this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException)
            {
                break; // Component destroyed
            }
        }
    }

    private async UniTaskVoid LoadAvatar(string url)
    {
        if (isLoading || string.IsNullOrEmpty(url)) return;

        // Don't reload if it's the same avatar and already loaded
        if (avatarloaded && avatarurls == url) return;

        avatarurls = url;

        // Cancel any existing load operation
        loadingTokenSource?.Cancel();
        loadingTokenSource = new CancellationTokenSource();

        avatarloaded = false;

        try
        {
            await LoadAvatarAsync(url, loadingTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Avatar loading cancelled");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Avatar loading failed: {ex.Message}");
        }
    }

    public void SetAvatarUrl(string _avatarUrl)
    {
        avatarurls = _avatarUrl;
        avatarurl_nw.Value = _avatarUrl;
        lastKnownUrl = _avatarUrl;
    }

    private string GetAvatarUrl()
    {
        return avatarurl_nw.Value;
    }

    private async UniTask LoadAvatarAsync(string url, CancellationToken cancellationToken)
    {
        isLoading = true;

        try
        {
            // Clean up existing avatar on main thread
            if (avatar != null)
            {
                Destroy(avatar);
                avatar = null;
            }

            // Create avatar loader
            var avatarLoader = new AvatarObjectLoader();

            // Use TaskCompletionSource for proper async handling
            var loadingTask = CreateAvatarLoadingTask(avatarLoader, url, cancellationToken);

            // Set parent name and start loading
            parentRef.name = url;
            avatarLoader.LoadAvatar(url);

            // Wait for loading to complete (non-blocking)
            var result = await loadingTask;

            // Check if cancelled
            cancellationToken.ThrowIfCancellationRequested();

            if (result.success && result.avatar != null)
            {
                avatar = result.avatar;

                // Setup avatar on main thread
                await UniTask.SwitchToMainThread();
                SetupAvatar(result.gender);
                avatarloaded = true;

                Debug.Log($"Avatar loaded successfully: {url}");
            }
            else
            {
                Debug.LogError($"Avatar loading failed: {url}");
            }
        }
        finally
        {
            isLoading = false;
        }
    }

    // Convert callback-based loading to proper async
    private async UniTask<(bool success, GameObject avatar, OutfitGender gender)> CreateAvatarLoadingTask(
        AvatarObjectLoader avatarLoader, string url, CancellationToken cancellationToken)
    {
        var tcs = new UniTaskCompletionSource<(bool, GameObject, OutfitGender)>();

        // Handle completion
        avatarLoader.OnCompleted += (_, args) =>
        {
            if (args?.Avatar != null)
            {
                tcs.TrySetResult((true, args.Avatar, args.Metadata.OutfitGender));
            }
            else
            {
                tcs.TrySetResult((false, null, OutfitGender.Masculine));
            }
        };

        // Handle failure
        avatarLoader.OnFailed += (_, args) =>
        {
            tcs.TrySetResult((false, null, OutfitGender.Masculine));
        };

        // Handle cancellation
        cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled();
        });

        try
        {
            // Wait for loading with timeout
            return await tcs.Task.Timeout(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            Debug.LogWarning($"Avatar loading timed out: {url}");
            return (false, null, OutfitGender.Masculine);
        }

    }

    private void SetupAvatar(OutfitGender gender)
    {
        if (avatar == null) return;

        // Process setup on background thread where possible
        SetupAvatarAsync(gender).Forget();
    }

    private async UniTaskVoid SetupAvatarAsync(OutfitGender gender)
    {
        try
        {
            // Heavy setup operations on background thread
            await UniTask.RunOnThreadPool(() =>
            {
                // Any CPU-intensive setup can go here
                // For now, we'll do most setup on main thread since it involves Unity objects
            });

            // Switch back to main thread for Unity operations
            await UniTask.SwitchToMainThread();

            // Common setup for both genders
            var animator = avatar.GetComponent<Animator>();
            if (animator != null)
            {
                var sourceAnimator = transform.GetComponent<Animator>();
                if (sourceAnimator != null)
                {
                    animator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;
                }
            }

            var networkAnimator = transform.GetComponent<NetworkAnimator>();
            if (networkAnimator != null && animator != null)
            {
                networkAnimator.SetAnimator(animator);
            }

            avatar.AddComponent<OVRLipSyncContext>();
            avatar.AddComponent<EyeAnimationHandler>();

            var gpt = transform.GetComponentInChildren<ChatGPT>();
            var playaudio = transform.GetComponentInChildren<Playaudio>();
            if (gpt != null && playaudio != null)
            {
                gpt.enabled = true;
                playaudio.enabled = true;
                

                // Gender-specific voice settings
                playaudio.ttvoice = gender == OutfitGender.Masculine ?
                    "X0Kc6dUd5Kws5uwEyOnL" : "ulZgFXalzbrnPUGQGs0S";
            }

            // Scale adjustment for masculine avatars
            if (gender == OutfitGender.Masculine)
            {
                avatar.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);
            }

            // Position avatar
            if (parentRef.name == avatarurls)
            {
                avatar.transform.parent = transform;
                avatar.transform.SetPositionAndRotation(avatarspawnpostion.position, avatarspawnpostion.rotation);
            }

            parentRef.name = PARENT;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Avatar setup failed: {ex.Message}");
        }
    }

    // Optional: Add methods for manual control
    public async UniTask<bool> LoadAvatarAndWait(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        SetAvatarUrl(url);

        // Wait for loading to complete
        while (isLoading)
        {
            await UniTask.Delay(50);
        }

        return avatarloaded && avatarurls == url;
    }

    public bool IsAvatarLoading()
    {
        return isLoading;
    }

    public void CancelCurrentLoading()
    {
        loadingTokenSource?.Cancel();
    }

    private void OnDestroy()
    {
        // Cancel any ongoing operations
        loadingTokenSource?.Cancel();
        loadingTokenSource?.Dispose();

        if (avatar != null)
        {
            if (Application.isPlaying)
                Destroy(avatar);
            else
                DestroyImmediate(avatar);
        }
    }
}*/





















/*public class AvatarAssigner : NetworkBehaviour
{

    [SerializeField]
    private string avatarurls;
    [SerializeField]
    private GameObject parentRef;

    [SerializeField]
    private Transform avatarspawnpostion;

    [SerializeField]
    private string faceMapperAssetName = "Facemap";


    private readonly SyncVar<string> avatarurl_nw = new SyncVar<string>();

    private GameObject avatar;

    private bool avatarloaded;

    private const string PARENT = "ParentRef";



    private void Start()
    {
        avatarloaded = false;
    }


    private void FixedUpdate()
    {
        if (avatarloaded) return;

        if (avatarurls == null) return;

        if (!GetAvatarUrl().Equals(avatarurls))
        {
            avatarurls = GetAvatarUrl();
        }

        StartCoroutine(LoadAvatarAsync(GetAvatarUrl()));

       avatarloaded = true;
    }



    public void SetAvatarUrl(string _avatarUrl)
    {
        avatarurls = _avatarUrl;
        avatarurl_nw.Value = _avatarUrl;
    }

    private string GetAvatarUrl()
    {
        return avatarurl_nw.Value;
    }




    private  IEnumerator LoadAvatarAsync(string v)
    {

         


        var avatarLoader = new AvatarObjectLoader();

        avatarLoader.OnCompleted += (_, args) =>
        {
            avatar = args.Avatar;


            if (args.Metadata.OutfitGender == OutfitGender.Masculine)
            {
                var animator = avatar.GetComponent<Animator>();
                animator.runtimeAnimatorController = transform.GetComponent<Animator>().runtimeAnimatorController;
                var networkaniamtor = transform.GetComponent<NetworkAnimator>();
                networkaniamtor.SetAnimator(animator);
                avatar.AddComponent<OVRLipSyncContext>();
                avatar.AddComponent<EyeAnimationHandler>();
               // avatar.AddComponent<AnimationHandler>();
              //  avatar.AddComponent<FaceActor>();
                var gpt = transform.GetComponentInChildren<ChatGPT>();
                gpt.enabled = true;
                gpt._voiceId = "X0Kc6dUd5Kws5uwEyOnL";
                gpt.isclintactive = true;
              
                avatar.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);

                 
               


            }
            else
            {
                var animator = avatar.GetComponent<Animator>();
                animator.runtimeAnimatorController = transform.GetComponent<Animator>().runtimeAnimatorController;
                var networkaniamtor = transform.GetComponent<NetworkAnimator>();
                networkaniamtor.SetAnimator(animator);
                avatar.AddComponent<OVRLipSyncContext>();
                avatar.AddComponent<EyeAnimationHandler>();

               //  avatar.AddComponent<AnimationHandler>();
                var gpt = transform.GetComponentInChildren<ChatGPT>();
                gpt.enabled = true;
                gpt._voiceId = "ulZgFXalzbrnPUGQGs0S";
                gpt.isclintactive = true;







            }


         
                  if(parentRef.name == v)
            {
                avatar.transform.parent = transform;
                avatar.transform.SetPositionAndRotation(avatarspawnpostion.position, avatarspawnpostion.rotation);
               


            }
               
               

            


            parentRef.name = PARENT;




        };


        avatarLoader.LoadAvatar(v);
         parentRef.name = v;
     
        yield return new WaitUntil(() => !avatarloaded);






    }

   

    private void OnDestroy()
    {
       

        StopAllCoroutines();
        if (avatar != null)
        {

            if (Application.isPlaying)
            {
                Destroy(avatar);



            }   else{
                DestroyImmediate(avatar);

            }







           
        }

    }


}*/
