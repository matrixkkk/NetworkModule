using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using ClientSide;
using UnityEngine;

namespace Assets.Scripts.ClientSide
{
    /// <summary>
    /// 토르 네트워크 처리 핸들러
    /// </summary>
    public class NetworkHandler : MonoBehaviour
    {
        private static string serverSettingAssetFile = "ServerSettings";         //서버 설정파일 이름


        private static NetworkHandler instance;

        protected internal static bool AppQuits;        //앱 종료 여부

        private static bool isMessageQueueRunning = true;               //messageQueueRunning 처리 여부
        private static Stopwatch timerToStopConnectionInBackground;     //백그라운드 연결 중지 시간 체크
        private int nextSendTickCount = 0;
        private int updateInterval = 50;                                  
        private float backgroundTimeout = 60f;                           //백그라운드 타임 아웃 시간(60초)

        private ServerSettings settings;
        private List<NetworkPeer> peerList = new List<NetworkPeer>();

        protected void Awake()
        {
            if (instance != null && instance != this && instance.gameObject != null)
            {
                GameObject.DestroyImmediate(instance.gameObject);
            }

            instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        private void Start()
        {
            if (settings == null)
            {
                settings = (ServerSettings)Resources.Load(serverSettingAssetFile, typeof(ServerSettings));
                if (settings == null)
                {
                    UnityEngine.Debug.LogError("Can't connect: Loading settings failed. ServerSettings asset must be in any 'Resources' folder as: " + serverSettingAssetFile);
                    return;
                }
            }

            backgroundTimeout = settings.backgroundTimeout;
        }

        protected void OnApplicationQuit()
        {
            NetworkHandler.AppQuits = true;
        }

        protected void OnApplicationPause(bool pause)
        {
            if (backgroundTimeout > 0.1f)
            {
                if (timerToStopConnectionInBackground == null)
                {
                    timerToStopConnectionInBackground = new Stopwatch();
                }

                if (pause)
                {
                    timerToStopConnectionInBackground.Reset();
                    timerToStopConnectionInBackground.Start();
                }
                else
                {
                    timerToStopConnectionInBackground.Stop();
                    UnityEngine.Debug.Log("OnApplicationPause : " + pause + " Pause time : " + timerToStopConnectionInBackground.Elapsed.TotalSeconds);
                }
            }
        }

        protected void OnDestroy()
        {
            foreach(var peer in peerList)
            {
                peer.Disconnect();
            }
        }

        protected void Update()
        {
#if !UNITY_EDITOR
        if(Application.internetReachability == NetworkReachability.NotReachable)
        {
            return;
        }
#endif

            if (peerList.Count == 0)
            {
                return;
            }

            if(isMessageQueueRunning)
            {
                foreach(var peer in peerList)
                {
                    if (peer.State == ClientState.Disconnected || (peer.State == ClientState.Connected && !peer.TcpSocket.Connected))
                    {
                        peer.State = ClientState.Disconnected;
                        continue;
                    }
                    bool doRecv = true;
                    while(isMessageQueueRunning && doRecv)
                    {
                        doRecv = peer.Dispatch();
                    }
                }
            }
            

            SendAck();

            if (updateInterval > 0)
            {
                int currentMsSinceStart = (int)(Time.realtimeSinceStartup * 1000);
                if (currentMsSinceStart > this.nextSendTickCount)
                {
                    foreach(var peer in peerList)
                    {
                        bool doSend = true;
                        while (isMessageQueueRunning && doSend)
                        {
                            doSend = peer.SendOut();
                        }
                    }
                    this.nextSendTickCount = currentMsSinceStart + this.updateInterval;
                }
            }
            else
            {
                foreach (var peer in peerList)
                {
                    bool doSend = true;
                    while (isMessageQueueRunning && doSend)
                    {
                        doSend = peer.SendOut();
                    }
                }
            }
        }

        void SendAck()
        {
            float currentTime = Time.realtimeSinceStartup;

            if (timerToStopConnectionInBackground != null && backgroundTimeout > 0.1f)
            {
                if (timerToStopConnectionInBackground.ElapsedMilliseconds > backgroundTimeout * 1000)
                {
                    //peer가 단일일 경우만 처리
                    foreach (var peer in peerList)
                    {
                        if (peer.Connected)
                        {
                            peer.Disconnect();
                        }
                    }
                    timerToStopConnectionInBackground.Stop();
                    timerToStopConnectionInBackground.Reset();
                    return;
                }
            }

            /*foreach (var peer in peerList)
            {
                if(peer.IsSendAsk)
                {
                    if (currentTime - peer.LastSendOutTime > 60)
                    {
                        peer.SendAck();
                    }
                }
            }*/
        }

        /// <summary>
        /// peer 추가
        /// </summary>
        /// <param name="peer"></param>
        public void AddPeer(NetworkPeer peer)
        {
            peerList.Add(peer);
        }
    }
}