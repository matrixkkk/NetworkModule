using Assets.Scripts.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Assets.Scripts.ClientSide
{
    public enum eLogLevel
    {
        Errors,                         //에러만 보여줌.
        Informational,                  //정보 포함
    }

    public enum eClientState
    {
        //초기화 안됨
        UnInitialized,
        Disconnecting,      //디스커넥트 중
        Connecting,         //연결 중
        Connected,
        Disconnected          //디스커넥트
    }

    /// <summary>
    /// Network peer 
    /// - 소켓 처리 및 버퍼 처리 하는 단말
    /// </summary>
    public class NetworkPeer
    {
        private int _streamBufferSize = 8192;                 //스트림 버퍼 사이즈 8k
        private int _receiveBufferSize = 4096;                //리시브 버퍼 사이즈 4k
        private int _sendBufferSize = 4096;
        private int _headerSize = 16;                           //헤더사이즈
        private int _packetSizeOffset = 8;                      //패킷 사이즈를 나타내는 바이트 오프셋

        private TcpClient _tcpSocket;                           //네트워크 소켓
        private NetworkStream _stream = null;                   //네트워크 스트림

        private object _lockPacketQueue = new object();         //동기화 객체
        private Header _receiveHeader;                          //받은 헤더 정보
        private byte[] _streamBuffer;                           //스트림 버퍼.

        private byte[] _sendBuffer;                             //send 버퍼
        private byte[] _receiveBuffer;                          //receive buffer
        private uint _totalRecvSize = 0;                        //이번에 받을 패킷의 총 사이즈
        private uint _accumRecvSize = 0;                        //누적 recv size
        private ushort _lastPacketSeq = 0;                      //마지막 패킷 시퀀스

        private Queue<Packet> _receivePacketQueue = new Queue<Packet>();        //받은 패킷 큐
        private Queue<Packet> _sendPacketQueue = new Queue<Packet>();           //send 패킷 큐

        private byte[] _key;                               //암호화 키
        private byte[] _iv = new byte[16];                 //iv
        private ulong _sessionID;                         //세션 ID

        private bool _isInitializeConnect = false;      //초기화 여부
        private bool _isSendAsk = false;
        private eClientState _clientState = eClientState.UnInitialized;                           //클라이언트 상태

        public int HeaderSize { set => _headerSize = value; }
        public int PacketSizeOffset { set => _packetSizeOffset = value; }

        #region callbacks
        public delegate void OnConnectCallback();
        public delegate void OnDisconnnectCallback();
        public delegate void OnReceiveCallback(Packet p);

        public OnConnectCallback OnConnect { get; set; }
        public OnDisconnnectCallback OnDisconnect { get; set; }
        public OnReceiveCallback OnReceive { get; set; }
        #endregion

        public bool IsInitialConnect { get { return _isInitializeConnect; } set { _isInitializeConnect = value; } }
        public ulong SessionID { set { _sessionID = value; } }
        public bool HasSession { get => _sessionID != 0; }
        public bool IsSendAsk { get { return _isSendAsk; } set { _isSendAsk = true; } }

        public eClientState State { get { return _clientState; } set { _clientState = value; } }
        public Socket TcpSocket { get { return _tcpSocket.Client; } }
        public bool Connected { get { return _tcpSocket != null && _tcpSocket.Connected && _clientState == eClientState.Connected; } }
        public float LastSendOutTime { get; private set; }
        public byte[] EncryptKey { set => _key = value; }

        public NetworkPeer()
        {
            _streamBuffer = new byte[_streamBufferSize];
            _sendBuffer = new byte[_sendBufferSize];
            _receiveBuffer = new byte[_receiveBufferSize];
        }

        public bool Connect(string serverAddress, int port)
        {
            if (_clientState == eClientState.Disconnecting)
            {
                Debug.LogError("Connect() failed. Can't connect while disconnecting (still). Current state");
                return false;
            }

            Debug.Log($"Connecting : {serverAddress} : {port}");

            bool isConnecting = Connecting(serverAddress, port);
            if (isConnecting)
            {
                _clientState = eClientState.Connecting;
            }

            return isConnecting;
        }

        /// <summary>
        /// 버퍼 사이즈 설정
        /// </summary>
        /// <param name="receiveBuferSize"></param>
        /// <param name="sendBufferSize"></param>
        public void SetBufferSize(int receiveBuferSize, int sendBufferSize, int streamBufferSize)
        {
            if (_receiveBufferSize != receiveBuferSize)
            {
                _receiveBufferSize = receiveBuferSize;
                _receiveBuffer = new byte[receiveBuferSize];
            }
            if (_sendBufferSize != sendBufferSize)
            {
                _sendBufferSize = sendBufferSize;
                _sendBuffer = new byte[sendBufferSize];
            }
            if (_streamBufferSize != streamBufferSize)
            {
                _streamBufferSize = streamBufferSize;
                _streamBuffer = new byte[_streamBufferSize];
            }
        }

        private bool Connecting(string serverAddress, int port)
        {
            IPAddress ipAddress = null;
            try
            {
                ipAddress = GetIP(serverAddress);
                if (ipAddress == null)
                {
                    Debug.Log("wrong server address : " + serverAddress);
                    return false;
                }
            }
            catch (Exception e)
            {
                _isInitializeConnect = false;
                _clientState = eClientState.Disconnected;
                Debug.LogError(e.Message);
                return false;
            }

            if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                _tcpSocket = new TcpClient(ipAddress.AddressFamily);
            }
            else
            {
                _tcpSocket = new TcpClient();
            }

            _tcpSocket.ReceiveBufferSize = _receiveBufferSize;
            _tcpSocket.SendBufferSize = _sendBufferSize;
            _tcpSocket.NoDelay = true;
            _tcpSocket.BeginConnect(ipAddress, port, new AsyncCallback(CallbackConnectResult), _tcpSocket);

            return true;
        }

        /// <summary>
        /// 패킷이 존재하면 처리
        /// </summary>
        public bool Dispatch()
        {
            if (_receivePacketQueue.Count == 0) return false;

            Packet p;
            lock (_lockPacketQueue)
            {
                p = _receivePacketQueue.Dequeue();
            }
            OnReceive?.Invoke(p);
            return _receivePacketQueue.Count > 0;
        }

        /// <summary>
        /// 전송 큐에 추가
        /// </summary>
        /// <param name="p"></param>
        public void AddQueue(Packet p)
        {
            _sendPacketQueue.Enqueue(p);
        }

        public void AddQueue(PacketID id, iSendMessage msg)
        {
            _sendPacketQueue.Enqueue(new Packet((ushort)id, msg.ToJson()));
        }

        /// <summary>
        /// 패킷 보냄.
        /// </summary>
        public bool SendOut()
        {
            if (!Connected || _sendPacketQueue.Count == 0) return false;

            SendPacket(_sendPacketQueue.Dequeue());
            return _sendPacketQueue.Count > 0;
        }

        /// <summary>
        /// Connection 콜백
        /// </summary>
        /// <param name="result"></param>
        private void CallbackConnectResult(IAsyncResult result)
        {
            TcpClient tcp = (TcpClient)result.AsyncState;

            IsInitialConnect = false;

            try
            {
                tcp.EndConnect(result);
            }
            catch (SocketException e)
            {
                Debug.LogError(e.Message);
                _clientState = eClientState.Disconnected;
                return;
            }

            if (tcp.Connected)
            {
                //이전 보낼 패킷 삭제
                if (_sendPacketQueue.Count > 0)
                {
                    _sendPacketQueue.Clear();
                }

                _clientState = eClientState.Connected;

                //스트림 가져옴.
                _stream = tcp.GetStream();

                Debug.Log("Connect success : " + DateTime.Now.ToString());
                //ReadHeader();
                ReadStream(true);
                OnConnect?.Invoke();
            }
            else
            {
                _clientState = eClientState.Disconnected;
            }
        }

        /// <summary>
        /// stream을 읽어옴
        /// </summary>
        private void ReadStream(bool isReset = false)
        {
            if (isReset)
            {
                _accumRecvSize = 0;
                _totalRecvSize = 0;
            }
            Read((uint)_receiveBufferSize, 0, new AsyncCallback(RecvCallback));
        }

        private void ReadHeader()
        {
            _accumRecvSize = 0;
            _totalRecvSize = Packet.HEADER_SIZE;
            Read(_totalRecvSize, 0, new AsyncCallback(RecvHeaderCallback));
        }

        private void ReadBody()
        {
            _accumRecvSize = 0;
            _totalRecvSize = (_receiveHeader.size - Packet.HEADER_SIZE);
            Read(_totalRecvSize, 0, new AsyncCallback(RecvBodyCallback));
        }


        private bool Read(uint receiveSize, int offset, AsyncCallback callback)
        {
            //패킷 받기 시작.
            try
            {
                if (_stream.CanRead == false)
                {
                    Debug.LogError("stream can not read");
                    return false;
                }
                _stream.BeginRead(_receiveBuffer, offset, (int)receiveSize, callback, this);
            }
            catch (Exception e)
            {
                Debug.LogError("netStream BegineRead error : " + e.Message);
                Disconnect();
                return false;
            }
            return true;
        }

        /// <summary>
        /// iv update
        /// </summary>
        private void GenerateAES_IV(ushort seq)
        {
            if (_sessionID > 0)
            {
                UInt64 high = _sessionID;
                UInt64 low = seq;
                Buffer.BlockCopy(BitConverter.GetBytes(high), 0, _iv, 0, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(low), 0, _iv, 8, 8);
            }
            else
            {
                Array.Clear(_iv, 0, _iv.Length);
            }
        }

        /// <summary>
        /// 패킷 전송
        /// </summary>
        /// <param name="p"></param>
        private void SendPacket(Packet p)
        {
            LastSendOutTime = Time.realtimeSinceStartup;

            p.Seq = _lastPacketSeq;

            //iv 구성
            GenerateAES_IV(_lastPacketSeq);

            //패킷 전송
            byte[] buffer = _sendBuffer;
            int size = p.ToByte(_sendBuffer, _key, _iv, true);
            if (buffer != null)
            {
                NetworkStream netStream = _tcpSocket.GetStream();

                try
                {
                    netStream.BeginWrite(buffer, 0, size, new AsyncCallback(CallbackSendResult), netStream);
                }
                catch (SocketException e)
                {
                    Debug.LogError("NetStream Send Error : " + e.Message);
                }

                netStream.Flush();
            }
        }

        /// <summary>
        /// 디스커넥트
        /// </summary>
        public void Disconnect()
        {
            _sessionID = 0;
            if (_tcpSocket != null && _tcpSocket.Connected)
            {
                OnDisconnect?.Invoke();

                try
                {
                    _tcpSocket.Client.Shutdown(SocketShutdown.Both);
                    _tcpSocket.Client.BeginDisconnect(false, new AsyncCallback(CallbackDisconnect), this);
                    _clientState = eClientState.Disconnecting;
                }
                catch (Exception e)
                {
                    Debug.LogError(e.Message);
                }
            }
        }

        /// <summary>
        /// 소켓 닫기
        /// </summary>
        private void CloseSocket()
        {
            if (_tcpSocket != null)
            {
                _tcpSocket.Close();
                _tcpSocket = null;
            }
        }

        /// <summary>
        /// disconnect 비동기
        /// </summary>
        /// <param name="result"></param>
        private void CallbackDisconnect(IAsyncResult result)
        {
            NetworkPeer peer = (NetworkPeer)result.AsyncState;
            try
            {
                peer.TcpSocket.EndDisconnect(result);
                peer._clientState = eClientState.Disconnected;
                peer._isInitializeConnect = false;

                peer.CloseSocket();
            }
            catch (SocketException e)
            {
                Debug.LogError(e.Message);
            }
        }

        /// <summary>
        /// IPv4 체크
        /// </summary>
        /// <param name="hostName"></param>
        /// <returns></returns>
        private bool IsDottedIPv4(string hostName)
        {
            Regex regex = new Regex(@"^(\d{1,2}|1\d\d|2[0-4]\d|25[0-5])\.(\d{1,2}|1\d\d|2[0-4]\d|25[0-5])\.(\d{1,2}|1\d\d|2[0-4]\d|25[0-5])\.(\d{1,2}|1\d\d|2[0-4]\d|25[0-5])$");
            return regex.IsMatch(hostName);
        }

        private void CallbackSendResult(IAsyncResult result)
        {
            NetworkStream nStream = (NetworkStream)result.AsyncState;

            try
            {
                nStream.EndWrite(result);
            }
            catch (SocketException e)
            {
                Debug.LogError(e.Message);
            }
        }

        /// <summary>
        /// ip 획득
        /// </summary>
        /// <returns></returns>
        private IPAddress GetIP(string hostName)
        {
            if (string.IsNullOrEmpty(hostName))
            {
                return null;
            }
            hostName.Trim();
            if (IsDottedIPv4(hostName))
            {
                return IPAddress.Parse(hostName);
            }

            IPAddress ipAddress = null;
            IPHostEntry hostEntry = Dns.GetHostEntry(hostName);

            for (int i = 0; i < hostEntry.AddressList.Length; i++)
            {
                ipAddress = hostEntry.AddressList[i];

                if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    Debug.Log("IPV4 : " + ipAddress.ToString());
                    return ipAddress;
                }
            }

            //ipv6 처리
            for (int i = 0; i < hostEntry.AddressList.Length; i++)
            {
                ipAddress = hostEntry.AddressList[i];

                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    Debug.Log("IPV6 : " + ipAddress.ToString());
                    return ipAddress;
                }
            }
            return ipAddress;
        }

        protected void RecvCallback(IAsyncResult asyncResult)
        {
            int receiveSize = 0;

            if (_tcpSocket == null || _clientState == eClientState.Disconnecting) return;

            try
            {
                //소켓이 연결 중이 아님.
                if (_tcpSocket.Connected && _stream != null)
                {
                    receiveSize = _stream.EndRead(asyncResult);
                }
                else
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
                if (_tcpSocket != null && _tcpSocket.Connected)
                {
                    Disconnect();
                }
                return;
            }

            if (receiveSize == 0)
            {
                //size가 0이면 소켓이 닫힘.
                Disconnect();
            }
            else
            {
                //받은 그대로 스트림 버퍼로 복사
                Buffer.BlockCopy(_receiveBuffer, 0, _streamBuffer, (int)_accumRecvSize, receiveSize);
                _accumRecvSize += (uint)receiveSize;          //누적 바이트 더함

                //헤더보다 적은경우
                //if (_accumRecvSize < _totalRecvSize)
                //{
                //다시 추가로 받음.
                //    Read(_totalRecvSize - _accumRecvSize, new AsyncCallback(RecvHeaderCallback));
                //    return;
                //}
                //_receiveHeader = new Header(_streamBuffer);
                //if (_totalRecvSize > STREAM_BUFFER_SIZE)
                //{
                //body가 버퍼보다 크다.
                //Debug.LogError("Packet size error : " + _totalRecvSize);
                //Disconnect();
                //return;
                //}

                //body 사이즈로 받음
                //ReadBody();
                Packetization();
                ReadStream();
            }
        }

        /// <summary>
        /// 패킷화 처리
        /// </summary>
        protected void Packetization()
        {
            if (_accumRecvSize < _headerSize)
            {
                //받은 패킷의 사이즈가 헤더사이즈보다 작은 경우 리턴
                return;
            }

            //헤더 이상 받았을 경우 총 받을 사이즈를 계산합니다
            _totalRecvSize = _streamBuffer[_packetSizeOffset];

            if (_accumRecvSize >= _totalRecvSize)
            {
                Header header = new Header(_streamBuffer);

                //누적사이즈가 총 사이즈보다 큰 경우 패킷을 구성함
                //패킷 다 받음 - 패킷 구성
                GenerateAES_IV(header.seq);

                //body에 해당하는 부분 복호화
                byte[] bytes = AES128.Decrypt(_streamBuffer, _headerSize, (int)(_totalRecvSize - _headerSize), _key);
                if (bytes == null)
                {
                    Debug.LogError("Decrypt error : " + header.pId);
                    return;
                }

                uint crc = CRC32.GetCRC(bytes, (int)bytes.Length);
                var pid = System.BitConverter.GetBytes(header.pId);
                var seq = System.BitConverter.GetBytes(header.seq);
                crc = CRC32.GetCRC(pid, pid.Length, crc);
                crc = CRC32.GetCRC(seq, seq.Length, crc);
                if (crc != header.crc)
                {
                    Debug.LogError("CRC error : " + header.pId);
                    return;
                }

                string byteToString = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);

                //받은 리스트에 추가.
                lock (_lockPacketQueue)
                {
                    _lastPacketSeq = header.seq;
                    _receivePacketQueue.Enqueue(new Packet(header, byteToString));
                }

                //사용한 만큼 버퍼 삭제하고 남은 부분 좌측 시프트
                for (int i = 0; i < _accumRecvSize; i++)
                {
                    if (_accumRecvSize < _streamBufferSize)
                    {
                        _streamBuffer[i] = _streamBuffer[_accumRecvSize + i];
                        _streamBuffer[_accumRecvSize + i] = 0;
                    }
                }
                _accumRecvSize -= _totalRecvSize;
                _totalRecvSize = 0;
            }
        }

        /// <summary>
        /// 헤더 사이즈로 받음
        /// </summary>
        /// <param name="aResult"></param>
        protected void RecvHeaderCallback(IAsyncResult asyncResult)
        {
            int receiveSize = 0;

            if (_tcpSocket == null || _clientState == eClientState.Disconnecting) return;

            try
            {
                //소켓이 연결 중이 아님.
                if (_tcpSocket.Connected && _stream != null)
                {
                    receiveSize = _stream.EndRead(asyncResult);
                }
                else
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
                if (_tcpSocket != null && _tcpSocket.Connected)
                {
                    Disconnect();
                }
                return;
            }

            if (receiveSize == 0)
            {
                //size가 0이면 소켓이 닫힘.
                Disconnect();
            }
            else
            {
                _accumRecvSize += (uint)receiveSize;          //누적 바이트 더함.

                //헤더보다 적은경우
                if (_accumRecvSize < _totalRecvSize)
                {
                    //다시 추가로 받음.
                    Read(_totalRecvSize - _accumRecvSize, (int)_accumRecvSize, new AsyncCallback(RecvHeaderCallback));
                    return;
                }
                _receiveHeader = new Header(_streamBuffer);
                if (_totalRecvSize > _streamBufferSize)
                {
                    //body가 버퍼보다 크다.
                    Debug.LogError("Packet size error : " + _totalRecvSize);
                    Disconnect();
                    return;
                }

                //body 사이즈로 받음
                ReadBody();
            }
        }

        /// <summary>
        /// 패킷의 body 부분 받음
        /// </summary>
        /// <param name="aReceiveBytes"></param>
        private void RecvBodyCallback(IAsyncResult asyncResult)
        {
            int receiveSize = 0;
            try
            {
                //소켓이 연결 중이 아님.
                if (_stream != null && _tcpSocket.Connected)
                {
                    receiveSize = _stream.EndRead(asyncResult);
                }
                else
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
                Disconnect();
                return;
            }
            if (receiveSize == 0)
            {
                //서버에 의해 소켓이 닫힘.
                Debug.LogError("Close socket RecvBodyCallback");
                Disconnect();
            }
            else
            {
                _accumRecvSize += (uint)receiveSize;
                //현재 스트림 모두 다 채웟는지
                uint iRemainSize = _totalRecvSize - _accumRecvSize;
                if (iRemainSize > 0)
                {
                    //스트림 읽기 계속               
                    Read(iRemainSize, (int)_accumRecvSize, new AsyncCallback(RecvBodyCallback));
                }
                else
                {
                    //패킷 다 받음 - 패킷 구성
                    GenerateAES_IV(_receiveHeader.seq);

                    //검증 후 string으로 변환
                    byte[] bytes = AES128.Decrypt(_streamBuffer, 0, (int)_totalRecvSize, _key);
                    if (bytes == null)
                    {
                        Debug.LogError("Decrypt error : " + _receiveHeader.pId);
                        return;
                    }

                    uint crc = CRC32.GetCRC(bytes, (int)bytes.Length);
                    var pid = System.BitConverter.GetBytes(_receiveHeader.pId);
                    var seq = System.BitConverter.GetBytes(_receiveHeader.seq);
                    crc = CRC32.GetCRC(pid, pid.Length, crc);
                    crc = CRC32.GetCRC(seq, seq.Length, crc);
                    if (crc != _receiveHeader.crc)
                    {
                        Debug.LogError("CRC error : " + _receiveHeader.pId);
                        return;
                    }

                    string byteToString = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);

                    //받은 리스트에 추가.
                    lock (_lockPacketQueue)
                    {
                        _lastPacketSeq = _receiveHeader.seq;
                        _receivePacketQueue.Enqueue(new Packet(_receiveHeader, byteToString));
                    }
                    ReadHeader();
                }
            }
        }
    }
}
