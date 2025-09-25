using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System;

[Serializable]
public class ChatMessagePayload
{
    public string game_id = "g1";
    public string round_id = "r1";
    public string player_id = "p1";
    public string player_name = "Alice";
    public string text;
    public string nearby_players;  // comma-separated ("p2,p3")
    public string location = "Reactor";
    public long timestamp;
}

[Serializable]
public class ServerReply
{
    public string text;
    // include other fields from your backend response 'meta' if needed
    // public Meta meta;
}

public class ChatManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField chatInput;
    public TextMeshProUGUI chatLogText;
    public ScrollRect chatScrollRect; // assign the Scroll View's ScrollRect (for autoscroll)

    [Header("Player Info (for testing)")]
    public string playerId = "p1";
    public string playerName = "Alice";
    public string location = "Reactor";
    public string[] nearbyPlayers = new string[] { "p2", "p3" };

    [Header("Backend")]
    public string ingestUrl = "http://127.0.0.1:8000/ingest/chat";
    public string npcReplyUrl = "http://127.0.0.1:8000/npc/reply"; // optional

    void Start()
    {
        if (chatInput != null) chatInput.onSubmit.AddListener(OnInputSubmit);
        // make sure input is focused
        if (chatInput != null) chatInput.ActivateInputField();
    }

    void Update()
    {
        // Press Enter to send
        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (!string.IsNullOrWhiteSpace(chatInput.text))
            {
                string msg = chatInput.text;
                SendChat(msg);
                chatInput.text = "";
                chatInput.ActivateInputField();
            }
        }
    }

    public void OnInputSubmit(string s)
    {
        if (!string.IsNullOrWhiteSpace(s))
        {
            SendChat(s);
            chatInput.text = "";
            chatInput.ActivateInputField();
        }
    }

    public void SendChat(string messageText)
    {
        ChatMessagePayload payload = new ChatMessagePayload();
        payload.text = messageText;
        payload.player_id = playerId;
        payload.player_name = playerName;
        payload.location = location;
        payload.nearby_players = string.Join(",", nearbyPlayers); // flatten list
        payload.timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string json = JsonUtility.ToJson(payload);
        StartCoroutine(PostChatAndHandleResponse(ingestUrl, json, messageText));
    }

    public void OnSendButtonClick()
    {
        if (!string.IsNullOrWhiteSpace(chatInput.text))
        {
            SendChat(chatInput.text);
            chatInput.text = "";
            chatInput.ActivateInputField();
        }
    }


    IEnumerator PostChatAndHandleResponse(string url, string json, string localMsg)
    {
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success;

            if (success)
            {
                // append local message
                AppendToChatLog($"Me: {localMsg}");
                // Optionally parse server reply (if your /ingest/chat returns a JSON reply)
                string responseText = request.downloadHandler.text;
                if (!string.IsNullOrEmpty(responseText) && responseText.Contains("\"text\""))
                {
                    try
                    {
                        ServerReply sr = JsonUtility.FromJson<ServerReply>(responseText);
                        if (sr != null && !string.IsNullOrEmpty(sr.text))
                        {
                            AppendToChatLog($"NPC: {sr.text}");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.Log("Could not parse server JSON reply: " + e.Message);
                    }
                }
            }
            else
            {
                AppendToChatLog($"<color=red>Send failed: {request.error}</color>");
                Debug.LogError($"Chat send error: {request.error} | {request.downloadHandler.text}");
            }
        }
    }

    void AppendToChatLog(string line)
    {
        chatLogText.text += "\n" + line;
        // Auto-scroll to bottom:
        Canvas.ForceUpdateCanvases();
        if (chatScrollRect != null)
        {
            chatScrollRect.verticalNormalizedPosition = 0f; // bottom
            Canvas.ForceUpdateCanvases();
        }
    }
}
