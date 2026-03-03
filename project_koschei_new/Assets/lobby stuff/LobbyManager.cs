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
    public event EventHandler OnKickedFromLobby;

    public class LobbyEventArgs : EventArgs { public Lobby lobby; }

    private Lobby joinedLobby;
    private float heartbeatTimer;
    private float lobbyPollTimer;
    private float lobbyTimerMax = 300f;
    private float currentLobbyTimer;
    private string playerName = "Player";

    // Prevents HandleLobbyPolling from firing JoinRelay multiple times
    private bool isJoiningRelay = false;

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
        HandleLobbyTimer();
    }

    // ----------------------------------------------------------------
    // Authenticate
    // ----------------------------------------------------------------

    public async void Authenticate(string playerName)
    {
        this.playerName = playerName;
        InitializationOptions options = new InitializationOptions();
        options.SetProfile(playerName);

        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync(options);

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            Debug.Log($"[LobbyManager] Signed in as {AuthenticationService.Instance.PlayerId}");
            SceneManager.LoadScene("LobbyScene");
        }
        catch (Exception e)
        {
            Debug.LogError("[LobbyManager] Authentication failed: " + e);
        }
    }

    // ----------------------------------------------------------------
    // Quick Join or Create
    // ----------------------------------------------------------------

    public async void QuickJoinOrCreate()
    {
        int maxRetries = 3;
        int currentTry = 0;

        while (currentTry < maxRetries)
        {
            try
            {
                Debug.Log($"[LobbyManager] QuickJoin attempt {currentTry + 1}/{maxRetries}...");

                QuickJoinLobbyOptions options = new QuickJoinLobbyOptions
                {
                    Filter = new List<QueryFilter>
                    {
                        new QueryFilter(
                            field: QueryFilter.FieldOptions.S1,
                            op: QueryFilter.OpOptions.EQ,
                            value: "India")
                    },
                    Player = GetPlayer()
                };

                joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync(options);

                Debug.Log($"[LobbyManager] Joined lobby: {joinedLobby.Name}");
                isJoiningRelay = false;
                OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
                return;
            }
            catch (LobbyServiceException e)
            {
                if (e.Reason == LobbyExceptionReason.NoOpenLobbies)
                {
                    Debug.Log("[LobbyManager] No open lobbies. Retrying...");
                    currentTry++;
                    await Task.Delay(2000);
                }
                else
                {
                    Debug.LogWarning("[LobbyManager] QuickJoin failed: " + e.Message);
                    break;
                }
            }
        }

        Debug.Log("[LobbyManager] Retries exhausted -- creating new lobby.");
        CreateLobby("Public_Match", false);
    }

    // ----------------------------------------------------------------
    // Create Lobby
    // ----------------------------------------------------------------

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
                    { "GameRegion", new DataObject(DataObject.VisibilityOptions.Public, "India", DataObject.IndexOptions.S1) }
                }
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, 4, options);
            joinedLobby = lobby;
            isJoiningRelay = false;

            Debug.Log($"[LobbyManager] Created lobby: {lobby.Name}");
            OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = lobby });
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("[LobbyManager] CreateLobby failed: " + e);
        }
    }

    // ----------------------------------------------------------------
    // Start Game (Host only)
    // ----------------------------------------------------------------

    public async void StartGame()
    {
        if (!IsLobbyHost()) return;

        try
        {
            Debug.Log("[LobbyManager] Host starting game...");

            if (LobbyUI.Instance != null)
                LobbyUI.Instance.SetUIInteractable(false);

            // 1. Create Relay allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3);
            string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            Debug.Log($"[LobbyManager] Relay code: {relayCode}");

            // 2. Setup transport
            var relayServerData = new RelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // 3. Start host
            bool isHostStarted = NetworkManager.Singleton.StartHost();
            if (!isHostStarted)
            {
                Debug.LogError("[LobbyManager] StartHost() failed!");
                if (LobbyUI.Instance != null)
                    LobbyUI.Instance.SetUIInteractable(true);
                LeaveLobby();
                return;
            }

            // 4. Push relay code to lobby so clients can pick it up via polling
            UpdateLobbyOptions updateOptions = new UpdateLobbyOptions
            {
                IsLocked = true,
                Data = new Dictionary<string, DataObject>
                {
                    { KEY_RELAY_CODE, new DataObject(DataObject.VisibilityOptions.Member, relayCode) }
                }
            };
            await Lobbies.Instance.UpdateLobbyAsync(joinedLobby.Id, updateOptions);

            Debug.Log("[LobbyManager] Lobby updated with relay code. Loading game scene...");

            // 5. Load scene -- NetworkManager replicates this to all connected clients
            NetworkManager.Singleton.SceneManager.LoadScene(SCENE_NAME_GAME, LoadSceneMode.Single);
        }
        catch (Exception e)
        {
            Debug.LogError("[LobbyManager] StartGame failed: " + e);
            if (LobbyUI.Instance != null)
                LobbyUI.Instance.SetUIInteractable(true);
            LeaveLobby();
        }
    }

    // ----------------------------------------------------------------
    // Join Relay (Client only)
    // ----------------------------------------------------------------

    private async void JoinRelay(string relayCode)
    {
        if (isJoiningRelay) return; // prevent double-joining from rapid polls
        isJoiningRelay = true;

        try
        {
            Debug.Log($"[LobbyManager] Client joining relay: {relayCode}");

            if (LobbyUI.Instance != null)
                LobbyUI.Instance.SetUIInteractable(false);

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);

            var relayServerData = new RelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            bool success = NetworkManager.Singleton.StartClient();

            if (!success)
            {
                Debug.LogError("[LobbyManager] StartClient() failed!");
                isJoiningRelay = false;
                if (LobbyUI.Instance != null)
                    LobbyUI.Instance.SetUIInteractable(true);
                LeaveLobby();
            }
            // Note: do NOT set joinedLobby = null here
            // Let HandleLobbyPolling do it AFTER JoinRelay is called
            // to avoid a second JoinRelay call on the next poll tick
        }
        catch (Exception e)
        {
            Debug.LogError("[LobbyManager] JoinRelay failed: " + e);
            isJoiningRelay = false;
            if (LobbyUI.Instance != null)
                LobbyUI.Instance.SetUIInteractable(true);
            LeaveLobby();
        }
    }

    // ----------------------------------------------------------------
    // Heartbeat
    // ----------------------------------------------------------------

    private async void HandleLobbyHeartbeat()
    {
        if (joinedLobby != null && IsLobbyHost())
        {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer < 0f)
            {
                heartbeatTimer = 15f;
                try
                {
                    await LobbyService.Instance.SendHeartbeatPingAsync(joinedLobby.Id);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[LobbyManager] Heartbeat failed: " + e.Message);
                }
            }
        }
    }

    // ----------------------------------------------------------------
    // Polling
    // ----------------------------------------------------------------

    private async void HandleLobbyPolling()
    {
        if (joinedLobby == null) return;
        if (isJoiningRelay) return; // don't poll while already connecting

        lobbyPollTimer -= Time.deltaTime;
        if (lobbyPollTimer > 0f) return;

        lobbyPollTimer = 2.5f;

        try
        {
            Lobby lobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
            joinedLobby = lobby;

            try
            {
                OnLobbyUpdate?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
            }
            catch (Exception uiEx)
            {
                Debug.LogWarning("[LobbyManager] UI update failed, keeping network alive: " + uiEx);
            }

            // Check if host has pushed relay code
            if (joinedLobby.Data != null &&
                joinedLobby.Data.ContainsKey(KEY_RELAY_CODE) &&
                !string.IsNullOrEmpty(joinedLobby.Data[KEY_RELAY_CODE].Value))
            {
                if (!IsLobbyHost())
                {
                    string relayCode = joinedLobby.Data[KEY_RELAY_CODE].Value;
                    joinedLobby = null; // clear BEFORE calling JoinRelay to stop further polls
                    JoinRelay(relayCode);
                }
            }
        }
        catch (LobbyServiceException e)
        {
            if (e.Reason == LobbyExceptionReason.RateLimited)
            {
                Debug.LogWarning("[LobbyManager] Rate limited -- slowing poll.");
                lobbyPollTimer = 5f; // back off instead of crashing
                return;
            }

            Debug.LogError("[LobbyManager] Lobby lost: " + e.Message);
            joinedLobby = null;
            SceneManager.LoadScene(SCENE_NAME_MENU);
        }
    }

    // ----------------------------------------------------------------
    // Lobby Timer
    // ----------------------------------------------------------------

    private void HandleLobbyTimer()
    {
        if (joinedLobby != null &&
            joinedLobby.Data != null &&
            joinedLobby.Data.ContainsKey(KEY_START_TIME))
        {
            long startTimeTicks = long.Parse(joinedLobby.Data[KEY_START_TIME].Value);
            DateTime startTime = new DateTime(startTimeTicks, DateTimeKind.Utc);
            float secondsPassed = (float)(DateTime.UtcNow - startTime).TotalSeconds;

            currentLobbyTimer = lobbyTimerMax - secondsPassed;

            if (currentLobbyTimer <= 0f)
            {
                Debug.Log("[LobbyManager] Lobby timed out.");
                LeaveLobby();
            }
        }
    }

    // ----------------------------------------------------------------
    // Leave Lobby
    // ----------------------------------------------------------------

    public async void LeaveLobby()
    {
        if (joinedLobby != null)
        {
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(
                    joinedLobby.Id,
                    AuthenticationService.Instance.PlayerId);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LobbyManager] LeaveLobby cleanup failed: " + e.Message);
            }

            joinedLobby = null;
        }

        isJoiningRelay = false;

        // Only go back to menu if we're not already in-game via Netcode
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene(SCENE_NAME_MENU);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    public bool IsLobbyHost() =>
        joinedLobby != null &&
        AuthenticationService.Instance.IsSignedIn &&
        joinedLobby.HostId == AuthenticationService.Instance.PlayerId;

    public float GetLobbyTimer() => currentLobbyTimer;

    public Lobby GetJoinedLobby() => joinedLobby;

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
