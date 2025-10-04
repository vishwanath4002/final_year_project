using UnityEngine;

public class ProximityChatManager : MonoBehaviour
{
    public GameObject chatMessagePrefab;
    public Transform chatContainer;
    public int maxMessages = 5;

    void Awake()
    {
        // Make sure container stays bottom-left
        RectTransform rt = chatContainer.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);   // bottom-left
        rt.anchorMax = new Vector2(0, 0);   // bottom-left
        rt.pivot = new Vector2(0, 0);       // pivot at bottom-left
        rt.anchoredPosition = new Vector2(20, 40); // offset from corner
    }

    public void AddMessage(string playerName, string message, Color nameColor)
    {
        GameObject obj = Instantiate(chatMessagePrefab, chatContainer);
        obj.SetActive(true); //  activate the clone, not the prefab

        ProximityChatMessage msg = obj.GetComponent<ProximityChatMessage>();
        msg.Setup(playerName, message, nameColor);

        // If too many messages, remove oldest
        if (chatContainer.childCount > maxMessages)
        {
            Destroy(chatContainer.GetChild(0).gameObject);
        }
    }

}
