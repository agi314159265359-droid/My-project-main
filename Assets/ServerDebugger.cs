using FishNet;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using System.Linq;
using Mikk.Avatar.Expression;

public class ServerDebugger : MonoBehaviour
{
    private void Update()
    {
        if (!InstanceFinder.IsServerStarted)
            return;

        // Press D to dump server state
        if (Input.GetKeyDown(KeyCode.D))
        {
            DumpServerState();
        }
    }

    private void DumpServerState()
    {
        Debug.Log("========== SERVER STATE DUMP ==========");

        var serverManager = InstanceFinder.ServerManager;

        Debug.Log($"Total Connections: {serverManager.Clients.Count}");

        foreach (var client in serverManager.Clients.Values)
        {
            Debug.Log($"--- Client {client.ClientId} ---");
            Debug.Log($"  Objects owned: {client.Objects.Count}");

            foreach (var obj in client.Objects)
            {
                if (obj != null)
                {
                    Debug.Log($"    - {obj.name} (ObjectId: {obj.ObjectId}, IsSpawned: {obj.IsSpawned})");
                }
                else
                {
                    Debug.Log($"    - NULL OBJECT REFERENCE!");
                }
            }
        }

        // Check SampleManger state
        var sampleManger = FindAnyObjectByType<SampleManger>();
        if (sampleManger != null)
        {
            var playerAvatarsField = sampleManger.GetType()
                .GetField("_playerAvatars", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (playerAvatarsField != null)
            {
                var dict = playerAvatarsField.GetValue(sampleManger) as System.Collections.IDictionary;
                Debug.Log($"SampleManger._playerAvatars count: {dict?.Count ?? 0}");

                if (dict != null)
                {
                    foreach (var key in dict.Keys)
                    {
                        var conn = key as FishNet.Connection.NetworkConnection;
                        var value = dict[key] as NetworkObject;
                        Debug.Log($"  Client {conn?.ClientId}: Avatar = {value?.name ?? "NULL"}, IsSpawned = {value?.IsSpawned}");
                    }
                }
            }
        }

        // Check LobbyNetwork state
        var lobbyNetwork = FindAnyObjectByType<FirstGearGames.LobbyAndWorld.Lobbies.LobbyNetwork>();
        if (lobbyNetwork != null)
        {
            Debug.Log($"LobbyNetwork - Created Rooms: {lobbyNetwork.CreatedRooms.Count}");

            foreach (var room in lobbyNetwork.CreatedRooms)
            {
                Debug.Log($"  Room '{room.Name}': {room.MemberIds.Count} members, Started: {room.IsStarted}");
                foreach (var member in room.MemberIds)
                {
                    Debug.Log($"    - Member: {member?.Owner?.ClientId ?? -1}");
                }
            }
        }

        Debug.Log("========== END DUMP ==========");
    }
}