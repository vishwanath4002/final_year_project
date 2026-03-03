using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AuthenticateUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private Button playButton;

    private void Awake()
    {
        playButton.onClick.AddListener(() =>
        {
            string playerName = usernameInput.text.Trim();

            if (string.IsNullOrEmpty(playerName))
            {
                Debug.LogWarning("[AuthenticateUI] Username cannot be empty.");
                return;
            }

            if (LobbyManager.Instance == null)
            {
                Debug.LogError("[AuthenticateUI] LobbyManager.Instance is null! Add LobbyManager to the LoginScene.");
                return;
            }

            playButton.interactable = false; // prevent double click
            LobbyManager.Instance.Authenticate(playerName);
        });
    }
}
