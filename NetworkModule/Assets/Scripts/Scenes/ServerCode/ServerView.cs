using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Scenes
{
    /// <summary>
    /// 서버 뷰
    /// </summary>
    public class ServerView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI runButtonText;
        [SerializeField] private Button runButton;
        [SerializeField] private TextMeshProUGUI consoleText;

        public Button RunButton => runButton;

        public TextMeshProUGUI ConsoleText => consoleText;

        public TextMeshProUGUI RunButtonText => runButtonText;
    }
}
