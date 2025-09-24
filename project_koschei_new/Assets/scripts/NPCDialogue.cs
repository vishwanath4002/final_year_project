using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using TMPro;

public class NPCDialogue : MonoBehaviour
{
    public TMP_Text npcTextUI; // drag your TextMeshPro text here
    private string serverUrl = "http://127.0.0.1:8000/npc/reply";

    public void AskNPC(string playerText)
    {
        StartCoroutine(SendRequest(playerText));
    }

    IEnumerator SendRequest(string playerText)
    {
        // Build JSON
        NPCRequest requestData = new NPCRequest(playerText);
        string json = JsonUtility.ToJson(requestData);

        UnityWebRequest req = new UnityWebRequest(serverUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Error: " + req.error);
        }
        else
        {
            NPCReply reply = JsonUtility.FromJson<NPCReply>(req.downloadHandler.text);
            npcTextUI.text = reply.text;
        }
    }

    [System.Serializable]
    public class NPCRequest { public string player_text; public NPCRequest(string text) { player_text = text; } }

    [System.Serializable]
    public class NPCReply { public string text; }
}
