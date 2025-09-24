using UnityEngine;
using TMPro;

public class ChatInputFix : MonoBehaviour
{
    private TMP_InputField input;

    void Awake()
    {
        input = GetComponent<TMP_InputField>();
    }

    void Update()
    {
        if (input.isFocused && Input.GetKeyDown(KeyCode.Return))
        {
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                // Simulate button click
                FindObjectOfType<ChatManager>().SendMessage();
            }
        }
    }
}
