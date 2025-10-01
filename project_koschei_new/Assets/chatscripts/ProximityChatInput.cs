using UnityEngine;
using TMPro;

public class ProximityChatInput : MonoBehaviour
{
    public TMP_InputField inputField;   //  changed to TMP_InputField
    public ProximityChatManager chatManager;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (!string.IsNullOrWhiteSpace(inputField.text))
            {
                // For now "You" (later replace with network player name)
                chatManager.AddMessage("You", inputField.text, Color.green);

                inputField.text = ""; // clear field
                inputField.DeactivateInputField(); // lose focus so player can move again
            }
            else
            {
                // If empty, just focus the field so player can type
                inputField.ActivateInputField();
            }
        }
    }
}
