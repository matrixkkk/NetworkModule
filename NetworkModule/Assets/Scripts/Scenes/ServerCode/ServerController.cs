using System;
using System.Globalization;
using System.Text;
using Assets.Scripts.ServerSide;
using Scenes.Server;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Pool;

namespace Scenes.ServerCode
{
    public class ServerController
    {
        private const int CONSOLE_MAX_COUNT = 100;
        private const string TEXT_RUN = "RUN";
        private const string TEXT_STOP = "STOP";
        
        private readonly ServerView _serverView;
        private readonly ServerSide.Server _server;
        private readonly MatchMaker _matchMaker;

        private bool _isRunning = false;
        private readonly StringBuilder _consoleText = new StringBuilder(CONSOLE_MAX_COUNT);
        private int _consoleLineCount = 0;
        
        private ObjectPool<UserController> _userPool;
        private long _instanceId = 0;

        public ServerController(ServerView view, ServerSide.Server server)
        {
            _serverView = view;
            _server = server;

            _matchMaker = new MatchMaker();
        }

        public void Initialize()
        {
            _serverView.ConsoleText.text = "";
            _serverView.RunButtonText.text = TEXT_RUN;
            _serverView.RunButton.onClick.AddListener(OnClickRun);

            _server.AcceptCompleteCallback = OnAccept;
            _server.UserLoginCallback = OnLoginUser;
            
            _server.Initialize();
            _userPool = new ObjectPool<UserController>(() => new UserController());

            _matchMaker.OnError = OnError;
            _matchMaker.OnMessage = OnMessage;
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

        private void SetText(string text, bool error = false)
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
            _consoleText.AppendLine(!error
                ? $"[{DateTime.Now.ToString(CultureInfo.CurrentCulture)}] {text}"
                : $"<color=red>[{DateTime.Now.ToString(CultureInfo.CurrentCulture)}] {text}</color>");
            _consoleLineCount++;
            _serverView.ConsoleText.text = _consoleText.ToString();
        }

        #region EventReceiver

        private void OnMessage(string msg)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                SetText(msg);
            });
        }

        private void OnError(string error)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                SetText(error, true);
            });
        }

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

        private void OnAccept(string ip, SocketObject socketObject)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                SetText($"Accept : {ip}");
            });

            var user = _userPool.Get();
            user.SetAsyncObject(socketObject);
            user.Id = ++_instanceId;
            user.ReleaseCallback = (socketObj) =>
            {
                _server.RemoveSocketObject(socketObj);
            };
            _matchMaker.EnterUser(user);
        }
        
        /// <summary>
        /// 쓰레드에서 콜백 호출
        /// </summary>
        /// <param name="id"></param>
        private void OnLoginUser(string id)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                SetText($"Login : {id}");
            });
        }
        #endregion
    }
}