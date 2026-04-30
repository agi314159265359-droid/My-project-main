using Mikk.Avatar;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class playsong : MonoBehaviour
{

    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button button;
    [SerializeField] private AvatarChatManager chatManager;
    void Start()
    {

        button.onClick.AddListener(() =>
        {

            chatManager.ProcessChatMessage(inputField.text);

        });


    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
