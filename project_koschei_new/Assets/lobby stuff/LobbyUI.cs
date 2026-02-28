using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    public static LobbyUI Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI lobbyIdText;
    [SerializeField] private TextMeshProUGUI timerText; 
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveLobbyButton; 
    [SerializeField] private Transform container;
    [SerializeField] private Transform playerRowTemplate;

    private void Awake()
    {
        Instance = this;
        playerRowTemplate.gameObject.SetActive(false);
    }

    private void Start()
    {
        // Subscribe using named methods
        LobbyManager.Instance.OnJoinedLobby += OnLobbyStateChanged;
        LobbyManager.Instance.OnLobbyUpdate += OnLobbyStateChanged;

        startGameButton.onClick.AddListener(() =>
        {
            if (LobbyManager.Instance.IsLobbyHost())
            {
                LobbyManager.Instance.StartGame();
            }
        });

        if (leaveLobbyButton != null)
        {
            leaveLobbyButton.onClick.AddListener(() =>
            {
                LobbyManager.Instance.LeaveLobby();
            });
        }

        LobbyManager.Instance.QuickJoinOrCreate();
    }

    // NEW: Unsubscribe to prevent MissingReferenceExceptions!
    private void OnDestroy()
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnJoinedLobby -= OnLobbyStateChanged;
            LobbyManager.Instance.OnLobbyUpdate -= OnLobbyStateChanged;
        }
    }

    private void Update()
    {
        float timeRemaining = LobbyManager.Instance.GetLobbyTimer();
        if (timerText != null)
        {
            timerText.text = "Time Left: " + Mathf.Ceil(timeRemaining).ToString("0");
        }
    }

    // Named method for the event
    private void OnLobbyStateChanged(object sender, LobbyManager.LobbyEventArgs e)
    {
        if (e.lobby != null)
        {
            UpdateLobbyUI(e.lobby);
        }
    }

    private void UpdateLobbyUI(Lobby lobby)
    {
        lobbyIdText.text = "Lobby: " + lobby.Name;

        // Clean up old elements safely
        foreach (Transform child in container)
        {
            if (child == playerRowTemplate) continue;
            if (child != null) Destroy(child.gameObject);
        }

        // Rebuild list
        foreach (Player player in lobby.Players)
        {
            Transform row = Instantiate(playerRowTemplate, container);
            row.gameObject.SetActive(true);
            TextMeshProUGUI nameText = row.GetComponentInChildren<TextMeshProUGUI>();
            
            if (player.Data != null && player.Data.ContainsKey("PlayerName"))
                nameText.text = player.Data["PlayerName"].Value;
            else
                nameText.text = "Player " + player.Id.Substring(0, 4);
        }

        UpdateStartButtonState(lobby);
    }

    private void UpdateStartButtonState(Lobby lobby)
    {
        if (startGameButton == null) return;

        TextMeshProUGUI btnText = startGameButton.GetComponentInChildren<TextMeshProUGUI>();

        if (LobbyManager.Instance.IsLobbyHost())
        {
            if (lobby.Players.Count == 4)
            {
                startGameButton.interactable = true;
                if(btnText) btnText.text = "Start Game";
            }
            else
            {
                startGameButton.interactable = false;
                if(btnText) btnText.text = $"Waiting... ({lobby.Players.Count}/4)";
            }
        }
        else
        {
            startGameButton.interactable = false;
            if(btnText) btnText.text = "Waiting for Host...";
        }
    }
}