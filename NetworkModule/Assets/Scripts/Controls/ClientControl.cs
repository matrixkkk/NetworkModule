using Assets.Scripts.Protocol;
using ClientSide;
using UI;
using UnityEngine;

namespace Controls
{
    /// <summary>
    /// 클라이언트 컨트롤러
    /// </summary>
    public class ClientControl
    {
        private const float PING_DELAY = 1f;
        private readonly ClientUI _ui;
        private readonly NetworkPeer _peer;

        private float _lastPingSendTime = 0f;
        private float _nextPingSend = 0;

        public ClientControl(ClientUI ui, NetworkPeer peer)
        {
            _ui = ui;
            _peer = peer;
            _peer.OnReceive = this.OnReceivePacket;
            _nextPingSend = Time.realtimeSinceStartup + PING_DELAY;
            _ui.OnDisconnect = this.OnDisconnect;
        }

        public void UpdatePing(float delta)
        {
            if (!_peer.Connected || !_peer.HasSession) return;
            if (Time.realtimeSinceStartup < _nextPingSend) return;

            _lastPingSendTime = Time.realtimeSinceStartup;
            _nextPingSend = Time.realtimeSinceStartup + PING_DELAY;

            Debug.Log("Send ping");
            _peer.AddQueue(PacketId.PingSend, new PingSend());
            _peer.SendOut();
        }

        public void Login()
        {
            var send = new LoginSend()
            {
                id = ""
            };
            _peer.AddQueue(new Packet((ushort)PacketId.LoginSend, send.ToJson()));
        }

        public void Disconnect()
        {
            _peer?.Disconnect();
        }

        private void OnDisconnect()
        {
            _peer?.Disconnect();
            //GameObject.Destroy(gameObject);
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
                        _ui.SetColor(Color.green);
                        _ui.SetId("#" + receive.session);
                        
                        _nextPingSend = Time.realtimeSinceStartup + PING_DELAY;
                    }
                    break;
                }
                case PacketId.PingReceive:
                {
                    var receive = JsonUtility.FromJson<PingReceive>(p.Str);
                    if(!receive.IsError())
                    {
                        float delta = Time.realtimeSinceStartup - _lastPingSendTime;
                        _ui.SetPing((int)(delta * 1000) + "ms");
                    }
                    break;
                }
            }
        }
    }
}