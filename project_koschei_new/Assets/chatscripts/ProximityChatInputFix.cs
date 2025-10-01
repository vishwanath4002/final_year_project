using UnityEngine;
using UnityEngine.UI;

public class ProximityChatInputFix : MonoBehaviour
{
    public InputField inputField;
    public ProximityChatManager chatManager;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (!string.IsNullOrWhiteSpace(inputField.text))
            {
                // Call our AddMessage method, not Unity's SendMessage
                chatManager.AddMessage("You", inputField.text, Color.green);

                inputField.text = "";
                inputField.ActivateInputField(); // refocus input
            }
        }
    }
}
