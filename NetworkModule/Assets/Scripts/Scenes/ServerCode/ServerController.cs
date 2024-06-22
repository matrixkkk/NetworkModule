using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Scenes.Server;
using ServerSide;
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

        private readonly object _activeUserLock = new();
        private readonly List<UserController> _activeUserList = new List<UserController>();      //활성 유저 리스트

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
            _userPool = new ObjectPool<UserController>(() => new UserController(), actionOnRelease: controller =>
            {
                controller.Clear();
            });

            _matchMaker.OnError = OnError;
            _matchMaker.OnMessage = OnMessage;
        }

        public void UpdateServer()
        {
            lock (_activeUserLock)
            {
                for (int i = 0; i < _activeUserList.Count;)
                {
                    var userController = _activeUserList[i];
                    if (!userController.IsConnected)
                    {
                        DisconnectUser(userController);
                        _activeUserList.RemoveAt(i);
                    }
                    else i++;
                }
            }
        }

        private void DisconnectUser(UserController userController)
        {
            if (!string.IsNullOrEmpty(userController.UserId))
            {
                Log(userController.UserId);
            }
            
            _server.ReleaseSocketObject(userController.SocketObj);
            if (userController.InEnterRoom)
            {
                _matchMaker.ExitUser(userController);
            }
            _userPool.Release(userController);
        }

        private void ClearAllUser()
        {
            lock (_activeUserLock)
            {
                foreach (var user in _activeUserList)
                {
                    user.SocketObj?.Close();
                }
                _activeUserList.Clear();
                _userPool.Clear();
            }
        }

        private void RunServer()
        {
            AddConsole("Start Server");
            _server.StartServer();
        }

        private void StopServer()
        {
            AddConsole("Stop Server");
            _server.EndServer();

            ClearAllUser();
            _matchMaker.Clear();
        }

        private void AddConsole(string text, bool error = false)
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

        private void Log(string text)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                AddConsole(text);
            });
        }

        #region EventReceiver

        private void OnMessage(string msg)
        {
            Log(msg);
        }

        private void OnError(string error)
        {
            Log(error);
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
                AddConsole($"Accept : {ip}");
            });

            var user = _userPool.Get();
            user.SetAsyncObject(socketObject);
            user.Id = ++_instanceId;
            socketObject.InstanceId = user.Id;
            
            lock (_activeUserLock)
            {
                _activeUserList.Add(user);
            }
        }
        
        /// <summary>
        /// 쓰레드에서 콜백 호출
        /// </summary>
        /// <param name="id"></param>
        private void OnLoginUser(SocketObject obj, string id)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                AddConsole($"Login : {id}");
            });

            var user = GetUser(obj);
            if (user == null)
            {
                OnError($"Not Found User : {id}");
                return;
            }
            _matchMaker.EnterUser(user);
        }

        private UserController GetUser(SocketObject obj)
        {
            lock (_activeUserLock)
            {
                return _activeUserList.Find(f => f.SocketObj== obj);
            }
        }
        #endregion
    }
}