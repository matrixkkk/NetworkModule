using System;
using System.Globalization;
using System.Threading.Tasks;
using Assets.Scripts.Protocol;
using ClientSide;
using Scenes.Server;
using UnityEngine;

namespace Scenes.ClientScene
{
    /// <summary>
    /// 클라이언트 컨트롤러
    /// </summary>
    public class ClientControl
    {
        private const float PING_DELAY = 1f;
        private const string TEXT_LOGIN = "Login";
        private const string TEXT_LOGOUT = "Logout";
        
        private readonly ClientView _view;
        private readonly NetworkPeer _peer;

        private float _lastPingSendTime = 0f;
        private float _nextPingSend = 0;

        private bool _isLogin;
        private string _ip;
        private int _port;
        
        public Action<int> OnEnterRoomPacket { get; set; }
        
        public ClientControl(ClientView view, NetworkPeer peer)
        {
            _view = view;
            _peer = peer;
            _nextPingSend = Time.realtimeSinceStartup + PING_DELAY;

            _view.ConnectButton.onClick.AddListener(OnClickLogin);
            _peer.OnConnect = OnConnect;
            _peer.OnDisconnect = OnDisconnect;
            _peer.OnReceive = OnReceivePacket;

            UnityMainThreadDispatcher.Instance.Initialize();
            RefreshConnectText();
        }

        public void SetDomain(string ip, int port)
        {
            _ip = ip;
            _port = port;
        }

        private void RefreshConnectText()
        {
            _view.ButtonText.text = _isLogin ? TEXT_LOGOUT : TEXT_LOGIN;
            if (!_isLogin)
            {
                _view.DelayText.text = "-";
            }
        }

        public void UpdatePing()
        {
            if(_peer == null) return;
            if (!_peer.Connected || !_peer.HasSession) return;
            if (Time.realtimeSinceStartup < _nextPingSend) return;
            
            _lastPingSendTime = Time.realtimeSinceStartup;
            _nextPingSend = Time.realtimeSinceStartup + PING_DELAY;

            PingSend send;
            
            _peer.AddQueue(PacketId.PingSend, send.ToJson());
            _peer.SendOut();
        }

        /// <summary>
        /// 로그인 버튼 클릭
        /// </summary>
        private async void OnClickLogin()
        {
            //로그인되어 있을경우 로그아웃함
            if (_isLogin)
            {
                Disconnect();
            }
            else
            {
                if (!_peer.Connected)
                {
                    _peer.Connect(_ip, _port);
                    await Task.Run( async () =>
                    {
                        while (!_peer.Connected) await Task.Delay(10);
                    });
                }
                Login();
            }
        }

        private void Login()
        {
            var send = new LoginSend()
            {
                id = _view.IdInputField.text
            };
            _peer.AddQueue(new Packet((ushort)PacketId.LoginSend, send.ToJson()));
            _view.SetLockInput(true);
        }

        private void OnConnect()
        {
            _view.ConnectButton.interactable = true;
        }

        public void Disconnect()
        {
            _peer?.Disconnect();
            _isLogin = false;
            RefreshConnectText();
        }

        private void OnDisconnect()
        {
            _view.ConnectButton.interactable = false;
        }

        private void OnReceivePacket(Packet p)
        {
            PacketId id = (PacketId)p.ID;
            switch(id)
            {
                case PacketId.LoginReceive:
                {
                    var receive = JsonUtility.FromJson<LoginReceive>(p.Str);
                    if(!receive.IsError())
                    {
                        _peer.SessionID = receive.session;
                        _nextPingSend = Time.realtimeSinceStartup + PING_DELAY;
                        _isLogin = true;
                        Debug.Log("Login Success");
                    }
                    else
                    {
                        Debug.Log("Login failed!");
                    }
                    _view.SetLockInput(false);
                    RefreshConnectText();
                    break;
                }
                case PacketId.PingReceive:
                {
                    var receive = JsonUtility.FromJson<PingReceive>(p.Str);
                    if(!receive.IsError())
                    {
                        float delta = Time.realtimeSinceStartup - _lastPingSendTime;
                        _view.DelayText.text = $"{((int)(delta * 1000)).ToString(CultureInfo.CurrentCulture)}ms";
                    }
                    break;
                }
                case PacketId.EnterRoomReceive:
                {
                    var receive = JsonUtility.FromJson<EnterRoom>(p.Str);
                    if (!receive.IsError())
                    {
                        OnEnterRoomPacket?.Invoke(receive.roomNumber);
                    }
                    break;
                }
            }
        }
    }
}