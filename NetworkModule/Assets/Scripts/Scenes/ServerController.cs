using System;
using System.Globalization;
using System.Text;
using ServerSide;

namespace Scenes
{
    public class ServerController
    {
        private const int CONSOLE_MAX_COUNT = 100;
        private const string TEXT_RUN = "RUN";
        private const string TEXT_STOP = "STOP";
        
        private readonly ServerView _serverView;
        private readonly Server _server;

        private bool _isRunning = false;
        private readonly StringBuilder _consoleText = new StringBuilder(CONSOLE_MAX_COUNT);
        private int _consoleLineCount = 0;
        
        public ServerController(ServerView view, Server server)
        {
            _serverView = view;
            _server = server;
        }

        public void Initialize()
        {
            _serverView.ConsoleText.text = "";
            _serverView.RunButtonText.text = TEXT_RUN;
            _serverView.RunButton.onClick.AddListener(OnClickRun);

            _server.AcceptCompleteCallback = OnAccept;
            _server.UserLoginCallback = OnLoginUser;
        }

        private void RunServer()
        {
            SetText("Start Server");
            _server.StartServer();
        }

        private void StopServer()
        {
            SetText("Stop Server");
            _server.EndServer();
        }

        private void SetText(string text)
        {
            if (CONSOLE_MAX_COUNT < _consoleLineCount)
            {
                string[] lines = _consoleText.ToString().Split(Environment.NewLine);
                _consoleText.Clear();
                for (int i = 5; i < lines.Length; i++)
                {
                    _consoleText.AppendLine(lines[i]);
                }
                _consoleLineCount -= 5;
            }
            //시간 추가
            _consoleText.AppendLine($"[{DateTime.Now.ToString(CultureInfo.CurrentCulture)}] {text}");
            _consoleLineCount++;
            _serverView.ConsoleText.text = _consoleText.ToString();
        }

        #region EventReceiver

        private void OnClickRun()
        {
            _isRunning = !_isRunning;
            _serverView.RunButtonText.text = _isRunning ? TEXT_STOP : TEXT_RUN;
            if (_isRunning)
            {
                RunServer();
            }
            else
            {
                StopServer();
            }
        }

        #endregion

        #region Callbacks

        private void OnAccept(string ip)
        {
            SetText($"Accept : {ip}");
        }

        private void OnLoginUser(string id)
        {
            SetText($"Login : {id}");
        }
        #endregion
    }
}