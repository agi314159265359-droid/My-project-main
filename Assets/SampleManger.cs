using FishNet.Object;
using FishNet.Connection;
using FishNet;
using UnityEngine;
using System.Collections.Generic;
using FirstGearGames.LobbyAndWorld.Lobbies;
using FirstGearGames.LobbyAndWorld.Clients;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;
using FirstGearGames.LobbyAndWorld.Lobbies.JoinCreateRoomCanvases;
using FishNet.Transporting;



namespace Mikk.Avatar.Expression
{

    public class SampleManger : NetworkBehaviour
    {

        #region Serialized
        [SerializeField] private Transform _spawnRegion1;
        [SerializeField] private Transform _spawnRegion2;
        [SerializeField] private NetworkObject _playerPrefab = null;
        #endregion

        #region Private
        private RoomDetails _roomDetails = null;
        private LobbyNetwork _lobbyNetwork = null;

        // ✅ Use AVATAR URL as persistent key
        private List<Transform> _availableSpawns = new List<Transform>();
        private Dictionary<string, Transform> _assignedSpawns = new Dictionary<string, Transform>(); // avatarURL -> spawn
        private Dictionary<string, NetworkObject> _playerAvatars = new Dictionary<string, NetworkObject>(); // avatarURL -> avatar
        private List<NetworkObject> _spawnedPlayerObjects = new List<NetworkObject>();
        #endregion

        #region Initialization
        private void Start()
        {
            _availableSpawns.Add(_spawnRegion1);
            _availableSpawns.Add(_spawnRegion2);
        }






        private void OnDestroy()
        {


            if (_lobbyNetwork != null)
            {
                _lobbyNetwork.OnClientStarted -= LobbyNetwork_OnClientStarted;
                _lobbyNetwork.OnClientLeftRoom -= LobbyNetwork_OnClientLeftRoom;
            }


        }

        public void FirstInitialize(RoomDetails roomDetails, LobbyNetwork lobbyNetwork)
        {
            _roomDetails = roomDetails;
            _lobbyNetwork = lobbyNetwork;
            _lobbyNetwork.OnClientStarted += LobbyNetwork_OnClientStarted;
            _lobbyNetwork.OnClientLeftRoom += LobbyNetwork_OnClientLeftRoom;
        }
        #endregion

        #region Event Handlers
        private void LobbyNetwork_OnClientStarted(RoomDetails roomDetails, NetworkObject client)
        {
            if (roomDetails != _roomDetails || client == null || client.Owner == null)
                return;

            //  Debug.Log($"[SampleManger] Client {client.Owner.ClientId} started");
            SpawnPlayer(client.Owner);
        }

        private void LobbyNetwork_OnClientLeftRoom(RoomDetails arg1, NetworkObject arg2)
        {
            if (arg2 == null || arg2.Owner == null)
            {
                // Debug.LogError("[SampleManger] NULL object in OnClientLeftRoom");
                return;
            }

            NetworkConnection leavingConn = arg2.Owner;
            //  Debug.Log($"[SampleManger] ===== Client {leavingConn.ClientId} LEAVING =====");

            // ✅ Get avatar URL - THE PERSISTENT IDENTIFIER
            string avatarUrl = GetAvatarUrl(leavingConn);

            if (string.IsNullOrEmpty(avatarUrl))
            {
                Debug.LogError($"[SampleManger] ERROR: No avatar URL for Client {leavingConn.ClientId}");
                return;
            }

            //   Debug.Log($"[SampleManger] Avatar URL: '{avatarUrl}'");

            // ✅ Find avatar by AVATAR URL (not by ClientId!)
            NetworkObject avatarToRemove = null;

            if (_playerAvatars.TryGetValue(avatarUrl, out avatarToRemove))
            {
                //  Debug.Log($"[SampleManger] ✓ Found avatar for URL '{avatarUrl}', ObjectId: {avatarToRemove.ObjectId}");
            }
            else
            {
                //  Debug.LogWarning($"[SampleManger] ✗ No avatar in dictionary for URL '{avatarUrl}'");

                // Dump dictionary contents for debugging
                Debug.Log($"[SampleManger] Current avatars in dictionary: {_playerAvatars.Count}");
                foreach (var kvp in _playerAvatars)
                {
                    Debug.Log($"  - URL: '{kvp.Key}' → ObjectId: {kvp.Value?.ObjectId ?? -1}");
                }
            }

            // Release spawn point
            if (_assignedSpawns.TryGetValue(avatarUrl, out Transform spawn))
            {
                _availableSpawns.Add(spawn);
                _assignedSpawns.Remove(avatarUrl);
                Debug.Log($"[SampleManger] Released spawn for URL, available: {_availableSpawns.Count}");
            }

            // Cleanup avatar
            if (avatarToRemove != null)
            {
                //  Debug.Log($"[SampleManger] Cleaning up avatar, IsSpawned: {avatarToRemove.IsSpawned}");

                // Remove from dictionaries FIRST
                _playerAvatars.Remove(avatarUrl);
                _spawnedPlayerObjects.Remove(avatarToRemove);

                // Clean avatar assigner
                AvatarAssigner assigner = avatarToRemove.GetComponent<AvatarAssigner>();
                if (assigner != null)
                {
                    try
                    {
                        assigner.CleanupAvatar();
                        Debug.Log($"[SampleManger] CleanupAvatar completed");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[SampleManger] Error in CleanupAvatar: {ex.Message}");
                    }
                }

                // Despawn
                if (avatarToRemove.IsSpawned)
                {
                    try
                    {
                        //  Debug.Log($"[SampleManger] Despawning ObjectId: {avatarToRemove.ObjectId}");
                        avatarToRemove.Despawn();
                        //  Debug.Log($"[SampleManger] ✓ Despawn successful");
                    }
                    catch (System.Exception ex)
                    {
                        //  Debug.LogError($"[SampleManger] ✗ Despawn error: {ex.Message}");
                    }
                }
                else
                {
                    //  Debug.LogWarning($"[SampleManger] Avatar was already despawned");
                }
            }

            //  Debug.Log($"[SampleManger] ===== CLEANUP COMPLETE. Remaining avatars: {_playerAvatars.Count} =====");
        }
        #endregion

        #region Spawning
        private void SpawnPlayer(NetworkConnection conn)
        {
            Debug.Log($"[SampleManger] ===== SPAWNING for Client {conn.ClientId} =====");

            string avatarUrl = GetAvatarUrl(conn);

            if (string.IsNullOrEmpty(avatarUrl))
            {
                Debug.LogError($"[SampleManger] ERROR: Cannot spawn without avatar URL");
                return;
            }

            Debug.Log($"[SampleManger] Avatar URL: '{avatarUrl}'");

            // Check if avatar URL already has an avatar
            if (_playerAvatars.TryGetValue(avatarUrl, out NetworkObject existingAvatar))
            {
                Debug.LogWarning($"[SampleManger] ⚠ Avatar URL '{avatarUrl}' already has avatar, cleaning up");

                if (existingAvatar != null && existingAvatar.IsSpawned)
                {
                    existingAvatar.Despawn();
                }

                _playerAvatars.Remove(avatarUrl);
                _spawnedPlayerObjects.Remove(existingAvatar);
            }

            // Release old spawn
            if (_assignedSpawns.TryGetValue(avatarUrl, out Transform oldSpawn))
            {
                _availableSpawns.Add(oldSpawn);
                _assignedSpawns.Remove(avatarUrl);
            }

            // Get spawn point
            if (_availableSpawns.Count == 0)
            {
                Debug.LogError("[SampleManger] ERROR: No spawn points!");
                return;
            }

            Transform spawnPoint = _availableSpawns[0];
            _availableSpawns.RemoveAt(0);
            _assignedSpawns[avatarUrl] = spawnPoint;

            // Spawn avatar
            NetworkObject nob = Instantiate(_playerPrefab, spawnPoint.position, Quaternion.identity);

            // ✅ CRITICAL FIX: Move to game scene so ALL clients can see it
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(nob.gameObject, gameObject.scene);

            Debug.Log($"[SampleManger] Avatar scene: {nob.gameObject.scene.name}, Manager scene: {gameObject.scene.name}");

            // Spawn for specific connection
            base.Spawn(nob.gameObject, conn);

            // Store
            _playerAvatars[avatarUrl] = nob;
            _spawnedPlayerObjects.Add(nob);

            Debug.Log($"[SampleManger] ✓ Avatar spawned: ObjectId: {nob.ObjectId}, IsSpawned: {nob.IsSpawned}");
            Debug.Log($"[SampleManger] Total avatars: {_playerAvatars.Count}");

            // Set avatar URL
            AvatarAssigner assigner = nob.GetComponent<AvatarAssigner>();
            if (assigner != null)
            {
                assigner.SetAvatarUrl(avatarUrl);
                Debug.Log($"[SampleManger] Set avatar URL on AvatarAssigner");
            }

            Debug.Log($"[SampleManger] ===== SPAWN COMPLETE =====");






        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Gets avatar URL for a connection - PERSISTENT identifier
        /// </summary>
        private string GetAvatarUrl(NetworkConnection conn)
        {
            if (conn == null)
            {
                Debug.LogError("[SampleManger] GetAvatarUrl: Connection is null");
                return null;
            }

            ClientInstance ci = ClientInstance.ReturnClientInstance(conn);
            if (ci == null)
            {
                Debug.LogError($"[SampleManger] GetAvatarUrl: No ClientInstance for Client {conn.ClientId}");
                return null;
            }

            string avatarUrl = ci.PlayerSettings.GetAvatarurl();

            if (string.IsNullOrEmpty(avatarUrl))
            {
                Debug.LogError($"[SampleManger] GetAvatarUrl: ClientInstance exists but avatar URL is empty for Client {conn.ClientId}");
                return null;
            }

            return avatarUrl;
        }

        public bool TryGetPlayerAvatar(NetworkConnection conn, out NetworkObject playerAvatar)
        {
            string avatarUrl = GetAvatarUrl(conn);

            if (!string.IsNullOrEmpty(avatarUrl))
            {
                if (_playerAvatars.TryGetValue(avatarUrl, out playerAvatar))
                {
                    //      Debug.Log($"[SampleManger] TryGetPlayerAvatar: Found for URL");
                    return playerAvatar != null;
                }
            }

            //  Debug.LogWarning($"[SampleManger] TryGetPlayerAvatar: Not found for Client {conn?.ClientId}");
            playerAvatar = null;
            return false;
        }

        private void OnRemoteConnectionState(NetworkConnection conn, FishNet.Transporting.RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Stopped)
            {
                Debug.Log($"[SampleManger] Client {conn.ClientId} disconnected, cleaning up");

                string avatarUrl = GetAvatarUrl(conn);
                if (!string.IsNullOrEmpty(avatarUrl) && _playerAvatars.ContainsKey(avatarUrl))
                {
                    // Clean up their avatar
                    if (_playerAvatars.TryGetValue(avatarUrl, out NetworkObject avatar))
                    {
                        if (avatar != null && avatar.IsSpawned)
                        {
                            avatar.Despawn();
                        }
                        _playerAvatars.Remove(avatarUrl);
                    }

                    // Release spawn point
                    if (_assignedSpawns.ContainsKey(avatarUrl))
                    {
                        _availableSpawns.Add(_assignedSpawns[avatarUrl]);
                        _assignedSpawns.Remove(avatarUrl);
                    }
                }
            }
        }



        #endregion
    }



}










/*#region Serialized
[Header("Spawning")]
/// <summary>
/// Region players may spawn.
/// </summary>
[Tooltip("Region players may spawn.")]
[SerializeField]
private Transform _spawnRegion1;

[SerializeField]
private Transform _spawnRegion2;




/// <summary>
/// Prefab to spawn.
/// </summary>
[Tooltip("Prefab to spawn.")]
[SerializeField]
private NetworkObject _playerPrefab = null;
/// <summary>
/// DeathDummy to spawn.
/// </summary>

#endregion

/// <summary>
/// RoomDetails for this game. Only available on the server.
/// </summary>
private RoomDetails _roomDetails = null;
/// <summary>
/// LobbyNetwork.
/// </summary>
private LobbyNetwork _lobbyNetwork = null;
/// <summary>
/// Becomes true once someone has won.
/// </summary>
//   private bool _winner = false;
/// <summary>
/// Currently spawned player objects. Only exist on the server.
/// </summary>
/// 

private List<Transform> _availoablespwans = new List<Transform>();
private Dictionary<NetworkConnection, Transform> _assignSpawns = new Dictionary<NetworkConnection, Transform>();



private List<NetworkObject> _spawnedPlayerObjects = new List<NetworkObject>();
private Dictionary<NetworkConnection, NetworkObject> _playerAvatars = new Dictionary<NetworkConnection, NetworkObject>();

private Dictionary<int, NetworkObject> _playerAvatarsByObjectId = new Dictionary<int, NetworkObject>();
private Dictionary<NetworkConnection, int> _connectionToObjectId = new Dictionary<NetworkConnection, int>();




#region Initialization and Deinitialization.


private void Awake()
{
    if (InstanceFinder.ServerManager != null)
    {
        InstanceFinder.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
    }
}


private void Start()
{




    _availoablespwans.Add(_spawnRegion1);
    _availoablespwans.Add(_spawnRegion2);













}


private void OnDestroy()
{
    if (_lobbyNetwork != null)
    {
        _lobbyNetwork.OnClientJoinedRoom -= LobbyNetwork_OnClientStarted;
        _lobbyNetwork.OnClientLeftRoom -= LobbyNetwork_OnClientLeftRoom;
    }

    if (InstanceFinder.ServerManager != null)
    {
        InstanceFinder.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
    }

    *//* if (_lobbyNetwork != null)
     {
         _lobbyNetwork.OnClientJoinedRoom -= LobbyNetwork_OnClientStarted;
         _lobbyNetwork.OnClientLeftRoom -= LobbyNetwork_OnClientLeftRoom;

     }

     if (InstanceFinder.ServerManager != null)
     {
         InstanceFinder.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
     }*//*
}








/// <summary>
/// Initializes this script for use.
/// </summary>
public void FirstInitialize(RoomDetails roomDetails, LobbyNetwork lobbyNetwork)
{
    _roomDetails = roomDetails;
    _lobbyNetwork = lobbyNetwork;
    _lobbyNetwork.OnClientStarted += LobbyNetwork_OnClientStarted;
    _lobbyNetwork.OnClientLeftRoom += LobbyNetwork_OnClientLeftRoom;
}

/// <summary>
/// Called when a client leaves the room.
/// </summary>
/// <param name="arg1"></param>
/// <param name="arg2"></param>
private void LobbyNetwork_OnClientLeftRoom(RoomDetails arg1, NetworkObject arg2)
{

    if (arg2 == null || arg2.Owner == null)
    {
        Debug.LogError("[SampleManger] NULL object in OnClientLeftRoom!");
        return;
    }

    NetworkConnection leavingConn = arg2.Owner;
    Debug.Log($"[SampleManger] Client {leavingConn.ClientId} leaving room");

    // ✅ FIX: Find avatar by checking Owner match, not just dictionary key
    NetworkObject avatarToRemove = null;
    NetworkConnection keyToRemove = null;

    // Search through all avatars to find the one owned by this connection
    foreach (var kvp in _playerAvatars)
    {
        if (kvp.Value != null && kvp.Value.Owner == leavingConn)
        {
            avatarToRemove = kvp.Value;
            keyToRemove = kvp.Key;
            Debug.Log($"[SampleManger] Found avatar: {kvp.Value.name}, DictKey: {kvp.Key.ClientId}, Owner: {leavingConn.ClientId}");
            break;
        }
    }

    // Also check if the leaving connection is directly in the dictionary
    if (avatarToRemove == null && _playerAvatars.TryGetValue(leavingConn, out var directAvatar))
    {
        avatarToRemove = directAvatar;
        keyToRemove = leavingConn;
        Debug.Log($"[SampleManger] Found avatar by direct lookup");
    }

    // Release spawn point
    Transform spawnToRelease = null;
    NetworkConnection spawnKey = null;

    foreach (var kvp in _assignSpawns)
    {
        if (kvp.Key == leavingConn || (avatarToRemove != null && kvp.Value == avatarToRemove.transform.parent))
        {
            spawnToRelease = kvp.Value;
            spawnKey = kvp.Key;
            break;
        }
    }

    if (spawnToRelease != null && spawnKey != null)
    {
        _availoablespwans.Add(spawnToRelease);
        _assignSpawns.Remove(spawnKey);
        Debug.Log($"[SampleManger] Released spawn point, available: {_availoablespwans.Count}");
    }

    // Despawn avatar
    if (avatarToRemove != null && keyToRemove != null)
    {
        Debug.Log($"[SampleManger] Removing avatar, IsSpawned: {avatarToRemove.IsSpawned}");

        if (avatarToRemove.IsSpawned)
        {
            var assigner = avatarToRemove.GetComponent<AvatarAssigner>();
            if (assigner != null)
            {
                assigner.CancelCurrentLoading();
            }

            avatarToRemove.Despawn();
        }

        _playerAvatars.Remove(keyToRemove);
    }
    else
    {
        Debug.LogWarning($"[SampleManger] No avatar found for Client {leavingConn.ClientId}");

        // Clean up any null entries
        var nullKeys = _playerAvatars.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in nullKeys)
        {
            _playerAvatars.Remove(key);
        }
    }

    // Clean up spawned objects list
    _spawnedPlayerObjects.RemoveAll(obj => obj == null || obj == avatarToRemove || obj.Owner == leavingConn);

    Debug.Log($"[SampleManger] Cleanup complete. Remaining avatars: {_playerAvatars.Count}");












    *//*   if(_assignSpawns.TryGetValue(arg2.Owner,out Transform releasespawn)  )
       {
           _availoablespwans.Add(releasespawn);
           _assignSpawns.Remove(arg2.Owner);
       }


       _playerAvatars.Remove(arg2.Owner);
*//*



     //Destroy all of clients objects, except their client instance.
     *//*for (int i = 0; i < _spawnedPlayerObjects.Count; i++)
     {
         NetworkObject entry = _spawnedPlayerObjects[i];
         //Entry is null. Remove and iterate next.
         if (entry == null)
         {
             _spawnedPlayerObjects.RemoveAt(i);
             i--;
             continue;
         }

         //If same connection to client (owner) as client instance of leaving player.
         if (_spawnedPlayerObjects[i].Owner == arg2.Owner)
         {
             //Destroy entry then remove from collection.
             entry.Despawn();
             _spawnedPlayerObjects.RemoveAt(i);
             i--;
         }

     }*//*
 }

 /// <summary>
 /// Called when a client starts a game.
 /// </summary>
 /// <param name="roomDetails"></param>
 /// <param name="client"></param>
 private void LobbyNetwork_OnClientStarted(RoomDetails roomDetails, NetworkObject client)
 {


     //Not for this room.
     if (roomDetails != _roomDetails)
         return;
     //NetIdent is null or not a player.
     if (client == null || client.Owner == null)
         return;



     SpawnPlayer(client.Owner);
 }
 #endregion

 #region Death.


 #region Spawning.
 /// <summary>
 /// Spawns a player at a random position for a connection.
 /// </summary>
 /// <param name="conn"></param>
 private void SpawnPlayer(NetworkConnection conn)
 {

     Debug.Log($"[SampleManger] SpawnPlayer for client {conn.ClientId}");

     // ✅ FIX: Check if this connection OR any avatar owned by this connection exists
     NetworkConnection oldKey = null;
     foreach (var kvp in _playerAvatars)
     {
         if (kvp.Key == conn || (kvp.Value != null && kvp.Value.Owner == conn))
         {
             Debug.LogWarning($"[SampleManger] Client {conn.ClientId} already has avatar (key: {kvp.Key.ClientId})! Cleaning up...");

             if (kvp.Value != null && kvp.Value.IsSpawned)
             {
                 kvp.Value.Despawn();
             }

             oldKey = kvp.Key;
             break;
         }
     }

     if (oldKey != null)
     {
         _playerAvatars.Remove(oldKey);

         // Release old spawn point
         if (_assignSpawns.ContainsKey(oldKey))
         {
             _availoablespwans.Add(_assignSpawns[oldKey]);
             _assignSpawns.Remove(oldKey);
         }
     }

     if (_availoablespwans.Count == 0)
     {
         Debug.LogError("[SampleManger] No available spawn points!");
         return;
     }

     // Assign spawn point
     Transform assignedspawn = _availoablespwans[0];
     _availoablespwans.RemoveAt(0);
     _assignSpawns[conn] = assignedspawn;

     // Spawn avatar
     NetworkObject nob = Instantiate(_playerPrefab, assignedspawn.position, Quaternion.identity);
     UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(nob.gameObject, gameObject.scene);

     base.Spawn(nob.gameObject, conn);

     // Track spawned objects - use CURRENT connection
     _spawnedPlayerObjects.Add(nob);
     _playerAvatars[conn] = nob; // ✅ Use current connection as key

     Debug.Log($"[SampleManger] Spawned avatar ObjectId: {nob.ObjectId} for Client {conn.ClientId}, total avatars: {_playerAvatars.Count}");

     // Set avatar URL
     ClientInstance ci = ClientInstance.ReturnClientInstance(conn);
     if (ci != null)
     {
         string avatarurl = ci.PlayerSettings.GetAvatarurl();
         var assigner = nob.GetComponent<AvatarAssigner>();
         if (assigner != null)
         {
             assigner.SetAvatarUrl(avatarurl);
         }
     }










     *//*

         if(_availoablespwans.Count == 0)
             {
                 return;
             }


             Transform assignedspawn = _availoablespwans[0];
             _availoablespwans.RemoveAt(0);
             _assignSpawns[conn] = assignedspawn;




            NetworkObject nob = Instantiate<NetworkObject>(_playerPrefab, assignedspawn.position, Quaternion.identity);
             UnitySceneManager.MoveGameObjectToScene(nob.gameObject, gameObject.scene);
           _spawnedPlayerObjects.Add(nob);
             base.Spawn(nob.gameObject, conn);


             _playerAvatars[conn] = nob;

             ///Assign avatarurl to it
             ClientInstance ci = ClientInstance.ReturnClientInstance(nob.Owner);
             string  avatarurl = ci.PlayerSettings.GetAvatarurl();


             nob.GetComponent<AvatarAssigner>().SetAvatarUrl(avatarurl);
     *//*












 }



 /// <summary>
 /// teleports a NetworkObject to a position.
 /// </summary>
 /// <param name="ident"></param>
 /// <param name="position"></param>
 *//*    [ObserversRpc]
     private void ObserversTeleport(NetworkObject ident, Vector3 position)
     {
         ident.transform.position = position;
     }*//*

 /// <summary>
 /// Draw spawn region.
 /// </summary>
 *//*  private void OnDrawGizmosSelected()
   {
       Gizmos.DrawWireCube(transform.position, _spawnRegion);
   }*//*
 #endregion



 public bool TryGetPlayerAvatar(NetworkConnection conn, out NetworkObject playerAvatar)
 {
     if (_playerAvatars.TryGetValue(conn, out playerAvatar))
     {
         return playerAvatar != null;
     }

     // If not found, search by Owner (in case connection changed)
     foreach (var kvp in _playerAvatars)
     {
         if (kvp.Value != null && kvp.Value.Owner == conn)
         {
             playerAvatar = kvp.Value;
             return true;
         }
     }

     playerAvatar = null;
     return false;



 }


 private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, FishNet.Transporting.RemoteConnectionStateArgs args)
 {
   // When a connection fully disconnects, clean up any references
 if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Stopped)
     {
         Debug.Log($"[SampleManger] Connection {conn.ClientId} stopped, cleaning up");

         // Clean up by checking if any avatar is owned by this connection
         var keysToRemove = new List<NetworkConnection>();

         foreach (var kvp in _playerAvatars)
         {
             if (kvp.Key == conn || (kvp.Value != null && kvp.Value.Owner == conn))
             {
                 keysToRemove.Add(kvp.Key);
             }
         }

         foreach (var key in keysToRemove)
         {
             _playerAvatars.Remove(key);
         }

         // Clean up spawn assignments
         if (_assignSpawns.ContainsKey(conn))
         {
             _availoablespwans.Add(_assignSpawns[conn]);
             _assignSpawns.Remove(conn);
         }
     }







 }

}
#endregion*/