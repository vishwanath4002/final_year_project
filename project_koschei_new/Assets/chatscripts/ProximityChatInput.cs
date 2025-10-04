using UnityEngine;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System;

[Serializable]
public class ChatPayload
{
    public string player_id;
    public string message;
}

[Serializable]
public class ChatResponse
{
    public string player_id;
    public string message;
    public string npc_reply;
    public string timestamp;
}

public class ProximityChatInput : MonoBehaviour
{
    public TMP_InputField inputField;   // assign in Inspector
    public ProximityChatManager chatManager; // assign in Inspector

    [Tooltip("FastAPI endpoint (keep localhost in editor)")]
    public string apiUrl = "http://127.0.0.1:8000/chat";

    [Tooltip("Local player id used in payload")]
    public string playerId = "player_1";

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (!string.IsNullOrWhiteSpace(inputField.text))
            {
                string text = inputField.text.Trim();

                // Show locally
                chatManager.AddMessage(playerId, text, Color.green);

                // clear + lose focus
                inputField.text = "";
                inputField.DeactivateInputField();

                // send to server
                StartCoroutine(SendMessageToServer(text));
            }
            else
            {
                // if empty, focus input so player can type
                inputField.ActivateInputField();
            }
        }
    }

    IEnumerator SendMessageToServer(string message)
    {
        ChatPayload payload = new ChatPayload { player_id = playerId, message = message };
        string json = JsonUtility.ToJson(payload);

        using (UnityWebRequest req = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                // parse response
                try
                {
                    ChatResponse resp = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
                    if (!string.IsNullOrEmpty(resp.npc_reply))
                    {
                        // Add NPC reply to UI (use any name you prefer)
                        chatManager.AddMessage("Alien-01", resp.npc_reply, Color.red);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("Failed to parse chat response: " + ex + " raw: " + req.downloadHandler.text);
                }
            }
            else
            {
                Debug.LogError("Chat POST failed: " + req.error);
                chatManager.AddMessage("System", "Connection error", Color.yellow);
            }
        }
    }
}
