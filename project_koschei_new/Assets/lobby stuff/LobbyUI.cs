using TMPro;
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
        if (LobbyManager.Instance == null)
        {
            Debug.LogError("[LobbyUI] LobbyManager.Instance is null! Make sure LobbyManager exists in LoginScene and is DontDestroyOnLoad.");
            return;
        }

        LobbyManager.Instance.OnJoinedLobby += OnLobbyStateChanged;
        LobbyManager.Instance.OnLobbyUpdate += OnLobbyStateChanged;

        startGameButton.onClick.AddListener(() =>
        {
            if (LobbyManager.Instance.IsLobbyHost())
                LobbyManager.Instance.StartGame();
        });

        if (leaveLobbyButton != null)
        {
            leaveLobbyButton.onClick.AddListener(() =>
            {
                LobbyManager.Instance.LeaveLobby();
            });
        }

        // Hide start button until we're in a lobby
        SetUIInteractable(false);

        LobbyManager.Instance.QuickJoinOrCreate();
    }

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
        if (LobbyManager.Instance == null) return;

        float timeRemaining = LobbyManager.Instance.GetLobbyTimer();
        if (timerText != null)
        {
            if (timeRemaining > 0f)
                timerText.text = "Time Left: " + Mathf.Ceil(timeRemaining).ToString("0") + "s";
            else
                timerText.text = "";
        }
    }

    // ----------------------------------------------------------------

    private void OnLobbyStateChanged(object sender, LobbyManager.LobbyEventArgs e)
    {
        if (e.lobby != null)
            UpdateLobbyUI(e.lobby);
    }

    private void UpdateLobbyUI(Lobby lobby)
    {
        if (lobbyIdText != null)
            lobbyIdText.text = "Lobby: " + lobby.Name;

        // Clear old player rows
        foreach (Transform child in container)
        {
            if (child == playerRowTemplate) continue;
            if (child != null) Destroy(child.gameObject);
        }

        // Rebuild player list
        foreach (Player player in lobby.Players)
        {
            Transform row = Instantiate(playerRowTemplate, container);
            row.gameObject.SetActive(true);

            TextMeshProUGUI nameText = row.GetComponentInChildren<TextMeshProUGUI>();
            if (nameText == null) continue;

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
            if (lobby.Players.Count >= 2) // changed from 4 so you can test with fewer players
            {
                startGameButton.interactable = true;
                if (btnText) btnText.text = "Start Game";
            }
            else
            {
                startGameButton.interactable = false;
                if (btnText) btnText.text = $"Waiting... ({lobby.Players.Count}/4)";
            }
        }
        else
        {
            startGameButton.interactable = false;
            if (btnText) btnText.text = "Waiting for Host...";
        }
    }

    /// <summary>
    /// Disables all buttons during connection/scene transition to prevent double clicks.
    /// </summary>
    public void SetUIInteractable(bool interactable)
    {
        if (startGameButton != null) startGameButton.interactable = interactable;
        if (leaveLobbyButton != null) leaveLobbyButton.interactable = interactable;
    }
}
