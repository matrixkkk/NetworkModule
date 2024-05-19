using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using ClientSide;
using UnityEngine;

namespace Assets.Scripts.ClientSide
{
    /// <summary>
    /// 토르 네트워크 처리 핸들러
    /// </summary>
    public class NetworkHandler : MonoBehaviour
    {
        private const string SERVER_SETTING_ASSET_FILE = "ServerSettings";         //서버 설정파일 이름

        private static NetworkHandler _instance;
        public static NetworkHandler Instance
        {
            get => _instance;
            set => _instance = value;
        }
        
        private const int UPDATE_INTERVAL = 50;

        [SerializeField] private bool showLog = true;
        
        private bool _isAppQuits;                                         //앱 종료 여부
        private bool _isMessageQueueRunning = true;                       //messageQueueRunning 처리 여부
        private Stopwatch _timerToStopConnectionInBackground;             //백그라운드 연결 중지 시간 체크
        private int _nextSendTickCount = 0;
        private long _backgroundTimeout = 60000;                           //백그라운드 타임 아웃 시간 ms

        private ServerSettings _settings;
        private NetworkPeer _networkPeer;

        public bool IsAppQuits => _isAppQuits;
        
        protected void Awake()
        {
            if (_instance != null && _instance != this && _instance.gameObject != null)
            {
                DestroyImmediate(_instance.gameObject);
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (_settings == null)
            {
                _settings = (ServerSettings)Resources.Load(SERVER_SETTING_ASSET_FILE, typeof(ServerSettings));
                if (_settings == null)
                {
                    UnityEngine.Debug.LogError("Can't connect: Loading settings failed. ServerSettings asset must be in any 'Resources' folder as: " + SERVER_SETTING_ASSET_FILE);
                    return;
                }
            }

            _backgroundTimeout = _settings.backgroundTimeout;
        }

        protected void OnApplicationQuit()
        {
            _isAppQuits = true;
        }

        protected void OnApplicationPause(bool pause)
        {
            if (_backgroundTimeout > 0.1f)
            {
                _timerToStopConnectionInBackground ??= new Stopwatch();

                if (pause)
                {
                    _timerToStopConnectionInBackground.Reset();
                    _timerToStopConnectionInBackground.Start();
                }
                else
                {
                    _timerToStopConnectionInBackground.Stop();
                }
                if (showLog)
                {
                    UnityEngine.Debug.Log($"OnApplicationPause : {pause} , Pause time : {_timerToStopConnectionInBackground.Elapsed.TotalSeconds.ToString(CultureInfo.CurrentCulture)} sec");
                }
            }
        }

        private void OnDestroy()
        {
            _networkPeer?.Disconnect();
        }

        protected void Update()
        {
#if !UNITY_EDITOR
            if(Application.internetReachability == NetworkReachability.NotReachable)
            {
                return;
            }
#endif

            if (_networkPeer == null)
            {
                return;
            }

            if (_isMessageQueueRunning)
            {
                if (_networkPeer.State == ClientState.Disconnected ||
                    (_networkPeer.State == ClientState.Connected && !_networkPeer.TcpSocket.Connected))
                {
                    _networkPeer.State = ClientState.Disconnected;
                    return;
                }

                bool isDone = true;
                while (isDone)
                {
                    isDone = _networkPeer.Dispatch();
                }
            }

            SendAck();
            
            int currentMsSinceStart = (int)(Time.realtimeSinceStartup * 1000);
            if (currentMsSinceStart > _nextSendTickCount)
            {
                bool doSend = true;
                while (_isMessageQueueRunning && doSend)
                {
                    doSend = _networkPeer.SendOut();
                }

                _nextSendTickCount = currentMsSinceStart + UPDATE_INTERVAL;
            }
        }

        private void SendAck()
        {
            if(_timerToStopConnectionInBackground == null || _networkPeer == null) return;
            
            if (_backgroundTimeout != 0 && _timerToStopConnectionInBackground.ElapsedMilliseconds > _backgroundTimeout)
            {
                //peer가 단일일 경우만 처리
                if (_networkPeer.Connected)
                {
                    _networkPeer.Disconnect();
                }
                _timerToStopConnectionInBackground.Stop();
                _timerToStopConnectionInBackground.Reset();
            }
        }

        /// <summary>
        /// peer 설정
        /// </summary>
        /// <param name="peer"></param>
        public void SetNetworkPeer(NetworkPeer peer)
        {
            _networkPeer = peer;
        }
        
        /// <summary>
        /// 메시지 큐 처리 여부
        /// </summary>
        /// <param name="set"></param>
        public void SetMessageQueueRunning(bool set)
        {
            _isMessageQueueRunning = set;
        }
    }
}