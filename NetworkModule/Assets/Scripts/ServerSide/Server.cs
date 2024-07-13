using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;

namespace ServerSide
{
    public class Server : MonoBehaviour
    {
        /// <summary>
        /// 받은 패킷
        /// </summary>
        private struct ReceivePacket
        {
            public SocketObject Owner;
            public Assets.Scripts.Protocol.Packet Packet;
        }

        private const int PORT = 20000;
        private readonly AddressFamily _addressFamily = AddressFamily.InterNetwork;
        private const int LISTEN_COUNT = 10;
        private const int RECEIVE_BUFFER_SIZE = 4096;

        private Socket _bindSocket;                   //bind 소켓
        private bool _isRunning = false;            //서버 구동중
        private readonly object _packetLock = new object();    //packet lock
        private readonly object _socketObjLock = new object();

        private ObjectPool<SocketObject> _objectPool;

        private readonly Queue<ReceivePacket> _receivePacketQueue = new ();           //받은 패킷 큐.

        private byte[] _baseKey;                    //기본 암호화 키
        private byte[] _cryptoKey;                  //변하는 키

        private ulong _sessionID = 0;                //발급 세션 id
        private readonly object _sessionLock = new object();
        
        #region [ properties ]
        public bool IsRunning => _isRunning;
        #endregion

        #region [ callbacks ]
        public Action<string, SocketObject> AcceptCompleteCallback { get; set; }

        //message 콜백
        public Action<SocketObject, string> UserLoginCallback { get; set; } 
        #endregion

        #region [ private ]

        private void Accept()
        {
            _bindSocket.BeginAccept(OnAccept, null);
            _isRunning = true;
        }

        /// <summary>
        /// 패킷이 존재하면 처리
        /// </summary>
        private void Dispatch()
        {
            lock (_packetLock)
            {
                if (_receivePacketQueue == null || _receivePacketQueue.Count == 0) return;
            }

            lock (_packetLock)
            {
                while (_receivePacketQueue.Count > 0)
                {
                    if(_receivePacketQueue.TryDequeue(out var p))
                    {
                        Task.Run(() =>
                        {
                            ProcessPacket(p);
                        });
                    }
                }
            }
        }
        
        /// <summary>
        /// 비동기 패킷 처리
        /// </summary>
        /// <param name="p"></param>
        private void ProcessPacket(ReceivePacket p)
        {
            ReceiveId id = (ReceiveId)p.Packet.ID;
            switch (id)
            {
                case ReceiveId.Login:
                {
                    LoginReceive login = JsonUtility.FromJson<LoginReceive>(p.Packet.Str);
                    
                    ulong sessionID = IssueSessionID();
                    p.Owner.SetSessionID(sessionID);

                    //로그인 검증 후 -
                    //성공 패킷 다시 보냄.
                    LoginSend send = new LoginSend()
                    {
                        session = sessionID,
                        error = 0
                    };
                    ushort receiveId = (ushort)(p.Packet.ID + 1);
                    p.Owner.Send(receiveId, send.ToJson());
                    UserLoginCallback?.Invoke(p.Owner, login.id);
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
                    ushort receiveId = (ushort)(p.Packet.ID + 1);
                    p.Owner.Send(receiveId, receive.ToJson());
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
            ThreadPool.SetMaxThreads(5, 5);
            while (_isRunning)
            {
                Dispatch();
                await Task.Delay(1);
            }
        }
        #endregion

        #region [ callbacks ]
        private void OnAccept(IAsyncResult ar)
        {
            if (_bindSocket == null) return;

            Socket clientSocket = _bindSocket.EndAccept(ar);
            
            lock (_socketObjLock)
            {
                var socketObject = _objectPool.Get();
                socketObject.SetSocket(clientSocket);
                socketObject.OnReceive = OnReceivePacket;
                socketObject.ReceiveStart();
                AcceptCompleteCallback?.Invoke(clientSocket.RemoteEndPoint.ToString(), socketObject);
            }

            //다시 대기
            _bindSocket.BeginAccept(OnAccept, null);
        }

        /// <summary>
        /// 받은 패킷 추가.
        /// 서브 스레드 접근
        /// </summary>
        /// <param name="p">패킷</param>
        /// <param name="owner">패킷 받은 주체</param>
        private void OnReceivePacket(Assets.Scripts.Protocol.Packet p, SocketObject owner)
        {
            var receivePacket = new ReceivePacket
            {
                Owner = owner,
                Packet = p
            };
            lock (_packetLock)
            {
                _receivePacketQueue.Enqueue(receivePacket);
            }
        }
        #endregion

        #region [ public ]

        public void Initialize()
        {
            _objectPool = new ObjectPool<SocketObject>(() => new SocketObject(RECEIVE_BUFFER_SIZE, _baseKey),
                actionOnRelease: o => o.Reset());
        }

        public void ReleaseSocketObject(SocketObject socketObj)
        {
            socketObj.Close();
            _objectPool?.Release(socketObj);
        }

        private void Clear()
        {
            _objectPool?.Dispose();
        }

        public void StartServer()
        {
            if (_bindSocket != null)
            {
                return;
            }
            CreateCryptoKey();

            _bindSocket = new Socket(_addressFamily, SocketType.Stream, ProtocolType.IP);
            //바인딩
            var ipEndPoint = new IPEndPoint(IPAddress.Any, PORT);
            _bindSocket.Bind(ipEndPoint);
            _bindSocket.Listen(LISTEN_COUNT);

            Accept();
            ProcessPacketLoop();
        }


        public void EndServer()
        {
            if (_bindSocket == null) return;

            _bindSocket.Close();
            _bindSocket = null;
            _isRunning = false;
            
            Clear();
        }
        
        #endregion
    }
}
