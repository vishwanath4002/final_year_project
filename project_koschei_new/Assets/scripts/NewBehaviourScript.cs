using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Collections;

[System.Serializable]
public class NPCRequest
{
    public string player_text;
    public string round_id = "r1";
    public string imitate_player_id = "p1";
}

[System.Serializable]
public class NPCResponse
{
    public string text;
}

public class NPCChat : MonoBehaviour
{
    public InputField inputBox;   // Drag your Unity UI InputField here
    public Text outputBox;        // Drag your Unity UI Text here

    private string apiUrl = "http://127.0.0.1:8000/npc/reply";

    public void OnSendMessage()
    {
        string playerMessage = inputBox.text;
        StartCoroutine(SendToNPC(playerMessage));
    }

    IEnumerator SendToNPC(string message)
    {
        NPCRequest req = new NPCRequest { player_text = message };
        string jsonData = JsonUtility.ToJson(req);

        using (UnityWebRequest www = UnityWebRequest.Post(apiUrl, ""))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                NPCResponse resp = JsonUtility.FromJson<NPCResponse>(www.downloadHandler.text);
                outputBox.text = "NPC: " + resp.text;
            }
            else
            {
                outputBox.text = "Error: " + www.error;
            }
        }
    }
}
