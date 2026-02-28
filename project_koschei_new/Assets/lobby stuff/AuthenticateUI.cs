using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AuthenticateUI : MonoBehaviour
{

    [SerializeField] private TMP_InputField usernameInput; // Reference to your input field
    [SerializeField] private Button playButton;           // Reference to your Play Game button

    private void Awake()
    {
        // We add a listener so that when the button is clicked, this code runs
        playButton.onClick.AddListener(() =>
        {
            string playerName = usernameInput.text;

            if (!string.IsNullOrEmpty(playerName))
            {
                // This triggers the authentication and scene change in LobbyManager
                LobbyManager.Instance.Authenticate(playerName);
            }
            else
            {
                Debug.LogWarning("Username cannot be empty!");
            }
        });
    }
}
