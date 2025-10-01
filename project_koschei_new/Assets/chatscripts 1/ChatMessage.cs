using UnityEngine;
using TMPro;

public class ChatMessage : MonoBehaviour
{
    public TextMeshProUGUI textField;
    public float lifetime = 5f;       // time on screen
    public float fadeDuration = 1f;   // fade out speed

    private CanvasGroup canvasGroup;
    private float timer;

    void Awake()
    {
        canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void Setup(string playerName, string message, Color nameColor, bool whisper = false)
    {
        string style = whisper ? "<i><color=grey>whisper</color></i> " : "";
        textField.text = $"<color=#{ColorUtility.ToHtmlStringRGB(nameColor)}>{playerName}</color> {style}{message}";
        timer = lifetime;
        canvasGroup.alpha = 1f;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0)
        {
            canvasGroup.alpha -= Time.deltaTime / fadeDuration;
            if (canvasGroup.alpha <= 0)
                Destroy(gameObject);
        }
    }
}
