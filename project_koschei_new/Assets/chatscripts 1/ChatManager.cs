using UnityEngine;

public class ChatManager : MonoBehaviour
{
    public GameObject chatMessagePrefab;
    public Transform chatContainer;
    public int maxMessages = 5; // prevent infinite list

    public void AddMessage(string playerName, string message, Color nameColor, bool whisper = false)
    {
        GameObject obj = Instantiate(chatMessagePrefab, chatContainer);
        ChatMessage msg = obj.GetComponent<ChatMessage>();
        msg.Setup(playerName, message, nameColor, whisper);

        // remove oldest if too many
        if (chatContainer.childCount > maxMessages)
        {
            Destroy(chatContainer.GetChild(0).gameObject);
        }
    }
}
