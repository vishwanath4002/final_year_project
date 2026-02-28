using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    public event EventHandler<LobbyEventArgs> OnJoinedLobby;
    public event EventHandler<LobbyEventArgs> OnLobbyUpdate;
    public event EventHandler<LobbyEventArgs> OnKickedFromLobby; //for later

    public class LobbyEventArgs : EventArgs { public Lobby lobby; }

    private Lobby joinedLobby;
    private float heartbeatTimer;
    private float lobbyPollTimer;
    private float lobbyTimerMax = 300f; // 5 Minutes
    private float currentLobbyTimer;
    private string playerName = "Player";

    private const string KEY_RELAY_CODE = "RelayCode";
    private const string KEY_START_TIME = "StartTime";
    private const string SCENE_NAME_GAME = "Scene_A";
    private const string SCENE_NAME_MENU = "LoginScene";

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        HandleLobbyHeartbeat();
        HandleLobbyPolling();
        HandleLobbyTimer(); // NEW: Check time every frame
    }

    public async void Authenticate(string playerName)
    {
        this.playerName = playerName;
        InitializationOptions options = new InitializationOptions();
        options.SetProfile(playerName);

        // 1. Initialize only if not already initialized
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            await UnityServices.InitializeAsync(options);
        }

        // 2. Sign in only if not already signed in
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        Debug.Log($"Signed in as {AuthenticationService.Instance.PlayerId}");

        // 3. Load the Lobby Scene automatically once logged in
        SceneManager.LoadScene("LobbyScene");
    }

    public async void QuickJoinOrCreate()
    {
        int maxRetries = 3;
        int currentTry = 0;

        // Loop to try joining multiple times
        while (currentTry < maxRetries)
        {
            try
            {
                Debug.Log($"Attempting to QuickJoin... (Try {currentTry + 1}/{maxRetries})");

                QuickJoinLobbyOptions options = new QuickJoinLobbyOptions
                {
                    Filter = new List<QueryFilter>
                    {
                        // NEW: Only join lobbies where the S1 string equals "India"
                        new QueryFilter(
                            field: QueryFilter.FieldOptions.S1,
                            op: QueryFilter.OpOptions.EQ,
                            value: "India")
                    },
                    Player = GetPlayer()
                };
                joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync(options);

                Debug.Log($"Successfully joined Lobby: {joinedLobby.Name}");
                OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
                return; // Exit the method since we joined successfully
            }
            catch (LobbyServiceException e)
            {
                // If the error is specifically that no open lobbies exist, wait and try again
                if (e.Reason == LobbyExceptionReason.NoOpenLobbies)
                {
                    Debug.Log("No open lobbies found yet. Waiting to retry...");
                    currentTry++;
                    await Task.Delay(2000); // Wait 2 seconds before the next attempt
                }
                else
                {
                    // If it's a different error (like a network drop), stop trying to join
                    Debug.LogWarning($"QuickJoin failed due to: {e.Message}");
                    break;
                }
            }
        }

        // If we exhausted all retries and STILL didn't find a lobby, create one
        Debug.Log("Retries exhausted. Creating a new public lobby...");
        CreateLobby("Public_Match", false);
    }

    public async void CreateLobby(string lobbyName, bool isPrivate)
    {
        try
        {
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = GetPlayer(),
                Data = new Dictionary<string, DataObject>
                {
                    { KEY_START_TIME, new DataObject(DataObject.VisibilityOptions.Member, DateTime.UtcNow.Ticks.ToString()) },
                    
                    // NEW: Add a public, indexed tag for your custom region
                    { "GameRegion", new DataObject(DataObject.VisibilityOptions.Public, "India", DataObject.IndexOptions.S1) }
                }
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, 4, options);
            joinedLobby = lobby;

            Debug.Log($"Created Lobby: {lobby.Name} in Region: India");
            OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = lobby });
        }
        catch (LobbyServiceException e) { Debug.Log(e); }
    }

    public async void StartGame()
    {
        if (IsLobbyHost())
        {
            try
            {
                Debug.Log("Host Starting Game...");

                // Disable UI so they don't double-click
                if (LobbyUI.Instance != null)
                    LobbyUI.Instance.gameObject.SetActive(false);

                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3);
                string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                var relayServerData = new RelayServerData(allocation, "dtls");
                NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

                // Start Host
                bool isHostStarted = NetworkManager.Singleton.StartHost();
                if (!isHostStarted)
                {
                    Debug.LogError("StartHost() failed! The server did not start.");
                    LeaveLobby();
                    return;
                }

                // Update Lobby so clients get the code
                UpdateLobbyOptions options = new UpdateLobbyOptions
                {
                    IsLocked = true,
                    Data = new Dictionary<string, DataObject>
                    {
                        { KEY_RELAY_CODE, new DataObject(DataObject.VisibilityOptions.Member, relayCode) }
                    }
                };
                await Lobbies.Instance.UpdateLobbyAsync(joinedLobby.Id, options);

                // IMMEDATELY load the scene. The network will pull the clients in automatically once they connect.
                NetworkManager.Singleton.SceneManager.LoadScene(SCENE_NAME_GAME, LoadSceneMode.Single);
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to Start Game: " + e);
                LeaveLobby();
            }
        }
    }

    private async void JoinRelay(string relayCode)
    {
        try
        {
            Debug.Log($"Client Joining Relay: {relayCode}");

            // Give visual feedback that the client is trying to connect
            if (LobbyUI.Instance != null)
            {
                // Disable the leave button so they don't break the connection process
                LobbyUI.Instance.gameObject.SetActive(false);
            }

            // 1. Join Allocation
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);

            // 2. Setup Transport
            var relayServerData = new RelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // 3. Start Client
            bool success = NetworkManager.Singleton.StartClient();

            if (!success)
            {
                Debug.LogError("Failed to start Network Client!");
                LeaveLobby(); // Kick them out if connection completely fails
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Relay Connection Failed: " + e);
            LeaveLobby(); // Kick them out so they don't stay frozen
        }
    }

    private async void HandleLobbyHeartbeat()
    {
        if (joinedLobby != null && IsLobbyHost())
        {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer < 0f)
            {
                heartbeatTimer = 15f;
                await LobbyService.Instance.SendHeartbeatPingAsync(joinedLobby.Id);
            }
        }
    }

    private async void HandleLobbyPolling()
    {
        if (joinedLobby != null)
        {
            lobbyPollTimer -= Time.deltaTime;
            if (lobbyPollTimer < 0f)
            {
                lobbyPollTimer = 2.5f;
                try
                {
                    Lobby lobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
                    joinedLobby = lobby;

                    // SAFETY NET: If the UI throws an error, catch it so the networking doesn't die!
                    try
                    {
                        OnLobbyUpdate?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
                    }
                    catch (Exception uiEx)
                    {
                        Debug.LogWarning("UI Update failed, but keeping network alive: " + uiEx);
                    }

                    // Network Check: Did host start the game?
                    if (joinedLobby.Data != null && joinedLobby.Data.ContainsKey(KEY_RELAY_CODE))
                    {
                        if (!IsLobbyHost())
                        {
                            JoinRelay(joinedLobby.Data[KEY_RELAY_CODE].Value);
                        }
                        joinedLobby = null;
                    }
                }
                catch (LobbyServiceException e)
                {
                    if (e.Reason == LobbyExceptionReason.RateLimited)
                    {
                        Debug.LogWarning("Hit Lobby API Rate Limit. Waiting...");
                        return;
                    }

                    Debug.LogError("Lobby closed or kicked: " + e.Message);
                    joinedLobby = null;
                    SceneManager.LoadScene(SCENE_NAME_MENU);
                }
            }
        }
    }
    // --- NEW: TIMER LOGIC ---
    private void HandleLobbyTimer()
    {
        if (joinedLobby != null && joinedLobby.Data != null && joinedLobby.Data.ContainsKey(KEY_START_TIME))
        {
            // Calculate time passed since creation
            long startTimeTicks = long.Parse(joinedLobby.Data[KEY_START_TIME].Value);
            DateTime startTime = new DateTime(startTimeTicks, DateTimeKind.Utc);
            float secondsPassed = (float)(DateTime.UtcNow - startTime).TotalSeconds;

            // Update local timer
            currentLobbyTimer = lobbyTimerMax - secondsPassed;

            // If time is up, kick player back to menu
            if (currentLobbyTimer <= 0f)
            {
                Debug.Log("Lobby Timed Out.");
                LeaveLobby();
            }
        }
    }

    public async void LeaveLobby()
    {
        if (joinedLobby != null)
        {
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
            }
            catch (Exception e) { Debug.Log(e); }

            joinedLobby = null;
            SceneManager.LoadScene(SCENE_NAME_MENU);
        }
    }

    public bool IsLobbyHost() => joinedLobby != null && joinedLobby.HostId == AuthenticationService.Instance.PlayerId;

    // UI needs this to show the countdown
    public float GetLobbyTimer() => currentLobbyTimer;

    private Player GetPlayer()
    {
        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) }
            }
        };
    }
}