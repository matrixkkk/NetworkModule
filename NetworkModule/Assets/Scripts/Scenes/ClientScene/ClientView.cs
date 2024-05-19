using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Scenes.ClientScene
{
    public class ClientView : MonoBehaviour
    {
        [SerializeField] private TMP_InputField idInputField;

        [SerializeField] private TextMeshProUGUI delayText;

        [SerializeField] private TextMeshProUGUI buttonText;

        [SerializeField] private Button connectButton;
        [SerializeField] private CanvasGroup canvasGroup;

        public TMP_InputField IdInputField => idInputField;

        public TextMeshProUGUI DelayText => delayText;

        public TextMeshProUGUI ButtonText => buttonText;

        public Button ConnectButton => connectButton;

        public void SetLockInput(bool isLock)
        {
            canvasGroup.interactable = !isLock;
            canvasGroup.blocksRaycasts = !isLock;
        }
    }
}
