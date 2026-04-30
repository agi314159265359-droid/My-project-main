using UnityEngine;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

using FishNet;


using FishNet.Transporting.UTP;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance;
    public string JoinCode { get; private set; }
    [SerializeField] private int _maxPlayers = 2;

    private FishyUnityTransport _transport;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        _transport = FindAnyObjectByType<FishyUnityTransport>();
        
    }

    private async void Start()
    {
        await InitializeUnityServices();
    }

    private async Task InitializeUnityServices()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }


    private void Update()
    {
        
    }




    public async Task<string> CreateRelay()
    {
        try
        {
            // Create Relay allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(_maxPlayers);
            JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // Configure FishyUnityTransport with Relay data
            _transport.SetRelayServerData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            // Start FishNet server
            InstanceFinder.ServerManager.StartConnection();
            Debug.Log($"Relay Join Code: {JoinCode}");

            return JoinCode;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Relay Error: {e.Message}");
            return null;
        }
    }

    public async void JoinRelay(string joinCode)
    {
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            _transport.SetRelayServerData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            // Start FishNet client
            InstanceFinder.ClientManager.StartConnection();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Relay Join Error: {e.Message}");
        }
    }

}
