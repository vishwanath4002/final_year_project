using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ChatManager : MonoBehaviour
{
    [Header("UI References")]
    public Transform chatContent;           // Content under ScrollView
    public TMP_InputField chatInputField;   // Input field
    public Button sendButton;               // Send button
    public GameObject messagePrefab;        // Prefab for messages

    void Start()
    {
        // Hook up button and input events
        sendButton.onClick.AddListener(SendMessage);
        chatInputField.onSubmit.AddListener(delegate { SendMessage(); });
    }

    public void SendMessage()
    {
        string message = chatInputField.text.Trim();
        if (string.IsNullOrEmpty(message)) return;

        AddMessage("Player", message);
        chatInputField.text = ""; // Clear input
        chatInputField.ActivateInputField(); // Focus again
    }

    public void AddMessage(string sender, string message)
    {
        GameObject newMessage = Instantiate(messagePrefab, chatContent);
        TMP_Text msgText = newMessage.GetComponent<TMP_Text>();
        msgText.text = $"<b>{sender}:</b> {message}";
    }
}
