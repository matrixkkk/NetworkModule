using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Assets.Scripts.ServerSide;
using UnityEngine;

namespace ServerSide
{
    public class Server : MonoBehaviour
    {
        /// <summary>
        /// 받은 패킷
        /// </summary>
        private struct ReceivePacket
        {
            public AsyncObject owner;
            public Assets.Scripts.Protocol.Packet packet;
        }

        private const int PORT = 20000;
        private readonly AddressFamily _addressFamily = AddressFamily.InterNetwork;
        private readonly int _listenCount = 10;
        private readonly int _receiveBufferSize = 4096;

        private Socket _bindSocket;                   //bind 소켓
        private bool _isRunning = false;            //서버 구동중
        private readonly object _packetLock = new object();    //packet lock
        private readonly object _asyncObjLock = new object();

        private readonly List<AsyncObject> _asyncObjectList = new List<AsyncObject>();                //비동기 객체 리스트
        private readonly Queue<ReceivePacket> _receivePacketQueue = new Queue<ReceivePacket>();           //받은 패킷 큐.

        private byte[] _baseKey;                    //기본 암호화 키
        private byte[] _cryptoKey;                  //변하는 키

        private ulong _sessionID = 0;                //발급 세션 id
        private readonly object _sessionLock = new object();

        #region [ properties ]
        public bool IsRunning => _isRunning;

        #endregion

        #region [ callbacks ]
        public delegate void OnAccept(string address);
        private OnAccept mOnAccept;
        public OnAccept OnAcceptCallback { set => mOnAccept = value; }
   
        #endregion

        #region [ private ]


        private void Accept()
        {
            _bindSocket.BeginAccept(AcceptCallback, null);
            _isRunning = true;
        }

        /// <summary>
        /// 패킷이 존재하면 처리
        /// </summary>
        private void Dispatch()
        {
            lock (_packetLock)
            {
                if (_receivePacketQueue.Count == 0) return;

                while (_receivePacketQueue.Count > 0)
                {
                    var p = _receivePacketQueue.Dequeue();
                    ProcessPacket(p);
                }
            }
        }

        private void ProcessPacket(ReceivePacket p)
        {
            ReceiveId id = (ReceiveId)p.packet.ID;
            switch (id)
            {
                case ReceiveId.Login:
                {
                    LoginReceive login = JsonUtility.FromJson<LoginReceive>(p.packet.Str);

                    Debug.Log("[Server] Login : " + login.id);

                    ulong sessionID = IssueSessionID();
                    p.owner.SetSessionID(sessionID);

                    //로그인 검증 후 -
                    //성공 패킷 다시 보냄.
                    LoginSend send = new LoginSend()
                    {
                        session = sessionID,
                        error = 0
                    };
                    ushort receiveId = (ushort)(p.packet.ID + 1);
                    p.owner.Send(receiveId, send.ToJson());
                    break;
                }
                case ReceiveId.Ping:
                {
                    //로그인 검증 후 -
                    //성공 패킷 다시 보냄.
                    PingSend receive = new PingSend()
                    {
                        error = 0
                    };
                    ushort receiveId = (ushort)(p.packet.ID + 1);
                    p.owner.Send(receiveId, receive.ToJson());
                    break;
                }
            }
        }

        private ulong IssueSessionID()
        {
            ulong id = 0;
            lock(_sessionLock)
            {
                _sessionID++;
                id = _sessionID;
            }
            return id;
        }

        /// <summary>
        /// 암호화 키 생성.
        /// </summary>
        private void CreateCryptoKey()
        {
            //키 계산함.
            int[] xorCode = new int[] {
                0x0b96, 0x0135, 0x0bf0, 0x0b0a,
                0x1ad2, 0x0ff1, 0x0ce8, 0x13b8,
                0x0430, 0x01d5, 0x0631, 0x080b,
                0x109c, 0x1cd8, 0x1e3d, 0x1047};

            int[] fake = new int[] { 0x0b1d, 0x01ad, 0x0b64, 0x0b57,
                0x1afb, 0x0f9d, 0x0ca9, 0x1339,
                0x048a, 0x0111,0x0669, 0x0868,
                0x1062, 0x1cd3, 0x1e54, 0x1043};

            _cryptoKey = new byte[fake.Length];

            for (int i = 0; i < fake.Length; i++)
            {
                _cryptoKey[i] = (byte)(fake[i] ^= xorCode[i]);
            }

            _baseKey = new byte[_cryptoKey.Length];
            Array.Copy(_cryptoKey, _baseKey, _baseKey.Length);
        }
        #endregion

        #region [ coroutine ]
        private async void ProcessPacketLoop()
        {
            while (_isRunning)
            {
                Dispatch();
                await Task.Delay(1);
            }
        }
        #endregion

        #region [ callbacks ]
        private void AcceptCallback(IAsyncResult ar)
        {
            if (_bindSocket == null) return;

            Socket clientSocket = _bindSocket.EndAccept(ar);

            //다시 대기
            _bindSocket.BeginAccept(AcceptCallback, null);
            Debug.Log("Accept : " + clientSocket.RemoteEndPoint.ToString());        

            AsyncObject asyncObj = new AsyncObject(clientSocket, _receiveBufferSize, _baseKey)
            {
                OnReceive = OnReceivePacket,
                OnCloseSocket = OnCloseSocket
            };

            asyncObj.ReceiveStart();
            lock (_asyncObjLock)
            {
                _asyncObjectList.Add(asyncObj);
            }

            mOnAccept?.Invoke(clientSocket.RemoteEndPoint.ToString());
        }

        /// <summary>
        /// 받은 패킷 추가.
        /// 서브 스레드 접근
        /// </summary>
        /// <param name="p">패킷</param>
        /// <param name="owner">패킷 받은 주체</param>
        private void OnReceivePacket(Assets.Scripts.Protocol.Packet p, AsyncObject owner)
        {
            lock (_packetLock)
            {
                var receivePacket = new ReceivePacket
                {
                    owner = owner,
                    packet = p
                };

                _receivePacketQueue.Enqueue(receivePacket);
            }
        }

        /// <summary>
        /// 소켓 close 콜백
        /// </summary>
        /// <param name="owner"></param>
        private void OnCloseSocket(AsyncObject owner)
        {
            lock(_asyncObjLock)
            {
                _asyncObjectList.Remove(owner);
            }
        }
        #endregion

        #region [ public ]


        public void StartServer()
        {
            if (_bindSocket != null)
            {
                return;
            }
            Debug.Log("Start Server");

            CreateCryptoKey();

            _bindSocket = new Socket(_addressFamily, SocketType.Stream, ProtocolType.IP);
            if (_bindSocket != null)
            {
                //바인딩
                var ipEndPoint = new IPEndPoint(IPAddress.Any, PORT);
                _bindSocket.Bind(ipEndPoint);
                _bindSocket.Listen(_listenCount);

                Accept();
                ProcessPacketLoop();
            }
        }


        public void EndServer()
        {
            if (_bindSocket == null) return;

            Debug.Log("End Server");
            _bindSocket.Close();
            _bindSocket = null;

            _isRunning = false;

            foreach (AsyncObject obj in _asyncObjectList)
            {
                obj.Close();
            }
        }
        #endregion
    }
}
