﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Assets.Scripts;
using Assets.Scripts.Protocol;
using Scenes.Server;
using UnityEngine;

namespace ClientSide
{
    public enum LogLevel
    {
        Errors,                         //에러만 보여줌.
        Informational,                  //정보 포함
    }

    public enum ClientState
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
        private const int STREAM_BUFFER_SIZE = 8192;                 //스트림 버퍼 사이즈 8k
        private const int RECEIVE_BUFFER_SIZE = 4096;                //리시브 버퍼 사이즈 4k
        private const int SEND_BUFFER_SIZE = 4096;
        private const int HEADER_SIZE = 16;                           //헤더사이즈
        private const int PACKET_SIZE_OFFSET = 8;                      //패킷 사이즈를 나타내는 바이트 오프셋

        private TcpClient _tcpSocket;                           //네트워크 소켓
        private NetworkStream _stream;                   //네트워크 스트림

        private readonly object _lockPacketQueue = new object();         //동기화 객체
        private Header _receiveHeader;                                   //받은 헤더 정보
        private readonly byte[] _streamBuffer;                           //스트림 버퍼.
        private readonly byte[] _sendBuffer;                             //send 버퍼
        private readonly byte[] _receiveBuffer;                          //receive buffer
        private uint _totalReceiveSize;                                  //이번에 받을 패킷의 총 사이즈
        private uint _accumReceiveSize;                                  //누적 받은 사이즈
        private ushort _lastPacketSeq;                                   //마지막 패킷 시퀀스

        private readonly Queue<Packet> _receivePacketQueue = new Queue<Packet>();        //받은 패킷 큐
        private readonly Queue<Packet> _sendPacketQueue = new Queue<Packet>();           //send 패킷 큐

        private readonly byte[] _key;                               //암호화 키
        private readonly byte[] _iv = new byte[16];                 //iv

        #region callbacks
        public delegate void OnConnectCallback();
        public delegate void OnDisconnectCallback();
        public delegate void OnReceiveCallback(Packet p);

        public delegate void OnConnectFailCallback(string error);

        public delegate void OnExceptionCallback(string message);

        public OnConnectCallback OnConnect { get; set; }
        public OnDisconnectCallback OnDisconnect { get; set; }
        public OnReceiveCallback OnReceive { get; set; }
        public OnConnectFailCallback OnConnectFailed { get; set; }
        public OnExceptionCallback OnException { get; set; }
        #endregion
        
        public ulong SessionID { get; set; }
        
        public bool HasSession => SessionID != 0;

        public ClientState State { get; set; } = ClientState.UnInitialized;

        public Socket TcpSocket => _tcpSocket.Client;
        public bool Connected => _tcpSocket is { Connected: true } && State == ClientState.Connected;
        public float LastSendOutTime { get; private set; }

        public NetworkPeer(byte[] key)
        {
            _key = key;
            _streamBuffer = new byte[STREAM_BUFFER_SIZE];
            _sendBuffer = new byte[SEND_BUFFER_SIZE];
            _receiveBuffer = new byte[RECEIVE_BUFFER_SIZE];
        }

        public bool Connect(string serverAddress, int port)
        {
            if (State == ClientState.Disconnecting)
            {
                Debug.LogError("Connect() failed. Can't connect while disconnecting (still). Current state");
                return false;
            }

            Debug.Log($"Connecting : {serverAddress} : {port}");

            bool isConnecting = Connecting(serverAddress, port);
            if (isConnecting)
            {
                State = ClientState.Connecting;
            }

            return isConnecting;
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
                State = ClientState.Disconnected;
                Debug.LogError(e.Message);
                return false;
            }

            _tcpSocket = ipAddress.AddressFamily == AddressFamily.InterNetworkV6 ? new TcpClient(ipAddress.AddressFamily) : new TcpClient();

            _tcpSocket.ReceiveBufferSize = RECEIVE_BUFFER_SIZE;
            _tcpSocket.SendBufferSize = SEND_BUFFER_SIZE;
            _tcpSocket.NoDelay = true;
            _tcpSocket.BeginConnect(ipAddress, port, CallbackConnectResult, _tcpSocket);

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

        public void AddQueue(PacketId id, string json)
        {
            _sendPacketQueue.Enqueue(new Packet((ushort)id, json));
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

            try
            {
                tcp.EndConnect(result);
            }
            catch (SocketException e)
            {
                State = ClientState.Disconnected;
                OnConnectFailed?.Invoke(e.Message);
                return;
            }

            if (tcp.Connected)
            {
                //이전 보낼 패킷 삭제
                if (_sendPacketQueue.Count > 0)
                {
                    _sendPacketQueue.Clear();
                }

                State = ClientState.Connected;

                //스트림 가져옴.
                _stream = tcp.GetStream();

                ReadStream(true);
                OnConnect?.Invoke();
            }
            else
            {
                State = ClientState.Disconnected;
            }
        }

        /// <summary>
        /// stream을 읽어옴
        /// </summary>
        private void ReadStream(bool isReset = false)
        {
            if (isReset)
            {
                _accumReceiveSize = 0;
                _totalReceiveSize = 0;
            }
            
            Read(RECEIVE_BUFFER_SIZE, 0, ReceiveCallback);
        }

        private void ReadHeader()
        {
            _accumReceiveSize = 0;
            _totalReceiveSize = Packet.HEADER_SIZE;
            Read(_totalReceiveSize, 0, ReceiveHeaderCallback);
        }

        private void ReadBody()
        {
            _accumReceiveSize = 0;
            _totalReceiveSize = (_receiveHeader.size - Packet.HEADER_SIZE);
            Read(_totalReceiveSize, 0, ReceiveBodyCallback);
        }


        private void Read(uint receiveSize, int offset, AsyncCallback callback)
        {
            //패킷 받기 시작.
            try
            {
                if (!_stream.CanRead)
                {
                    Debug.LogError("stream can not read");
                    return;
                }
                _stream.BeginRead(_receiveBuffer, offset, (int)receiveSize, callback, this);
            }
            catch (Exception e)
            {
                Debug.LogError($"netStream BeginRead error : {e.Message}");
                Disconnect();
            }
        }

        /// <summary>
        /// iv update
        /// </summary>
        private void GenerateAES_IV(ushort seq)
        {
            if (SessionID > 0)
            {
                UInt64 high = SessionID;
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
                    netStream.BeginWrite(buffer, 0, size, CallbackSendResult, netStream);
                }
                catch (SocketException e)
                {
                    Debug.LogError($"NetStream Send Error : {e.Message}");
                }

                netStream.Flush();
            }
        }

        /// <summary>
        /// 디스커넥트
        /// </summary>
        public void Disconnect()
        {
            SessionID = 0;
            if (_tcpSocket is not { Connected: true }) return;
            
            try
            {
                State = ClientState.Disconnecting;
                _tcpSocket.Client.Shutdown(SocketShutdown.Both);
                _tcpSocket.Client.BeginDisconnect(false, CallbackDisconnect, this);
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
            }
        }

        /// <summary>
        /// 소켓 닫기
        /// </summary>
        private void CloseSocket()
        {
            if (_tcpSocket == null) return;
            _tcpSocket.Close();
            _tcpSocket = null;
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
                peer.State = ClientState.Disconnected;
                peer.CloseSocket();
                
                OnDisconnect?.Invoke();
            }
            catch (SocketException e)
            {
                OnException?.Invoke(e.Message);
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

            hostName = hostName.Trim();
            if (IsDottedIPv4(hostName))
            {
                return IPAddress.Parse(hostName);
            }

            IPAddress ipAddress = null;
            IPHostEntry hostEntry = Dns.GetHostEntry(hostName);

            foreach (var t in hostEntry.AddressList)
            {
                ipAddress = t;

                if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    Debug.Log($"IPV4 : {ipAddress}");
                    return ipAddress;
                }
            }

            //ipv6 처리
            foreach (var t in hostEntry.AddressList)
            {
                ipAddress = t;

                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    Debug.Log($"IPV6 : {ipAddress}");
                    return ipAddress;
                }
            }
            return ipAddress;
        }

        private void ReceiveCallback(IAsyncResult asyncResult)
        {
            int receiveSize = 0;

            if (_tcpSocket == null || State == ClientState.Disconnecting) return;

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
                if (_tcpSocket is { Connected: true })
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
                Buffer.BlockCopy(_receiveBuffer, 0, _streamBuffer, (int)_accumReceiveSize, receiveSize);
                _accumReceiveSize += (uint)receiveSize;          //누적 바이트 더함
              
                CreatePacket();
                ReadStream();
            }
        }

        /// <summary>
        /// 패킷화 처리
        /// </summary>
        private void CreatePacket()
        {
            //헤더 사이즈보다 클 때만 처리한다
            while (_accumReceiveSize >= HEADER_SIZE)
            {
                if (_totalReceiveSize == 0)
                {
                    //헤더 이상 받았을 경우 총 받을 사이즈를 계산합니다
                    byte[] sizeBytes = new byte[4];
                    Buffer.BlockCopy(_streamBuffer, PACKET_SIZE_OFFSET, sizeBytes, 0, sizeof(uint));
                    _totalReceiveSize = BitConverter.ToUInt32(sizeBytes);
                }
                if(_accumReceiveSize < _totalReceiveSize)
                {
                    //패킷 구성할 만큼 받지 않았음
                    return;
                }

                Header header = new Header(_streamBuffer);

                //누적사이즈가 총 사이즈보다 큰 경우 패킷을 구성함
                //패킷 다 받음 - 패킷 구성
                GenerateAES_IV(header.seq);

                //body에 해당하는 부분 복호화
                byte[] bytes = AES128.Decrypt(_streamBuffer, HEADER_SIZE, (int)(_totalReceiveSize - HEADER_SIZE), 
                    _key, _iv);
                if (bytes == null)
                {
                    Debug.LogError($"Decrypt error : {header.pId.ToString()}");
                    return;
                }

                uint crc = CRC32.GetCRC(bytes, bytes.Length);
                var pid = BitConverter.GetBytes(header.pId);
                var seq = BitConverter.GetBytes(header.seq);
                crc = CRC32.GetCRC(pid, pid.Length, crc);
                crc = CRC32.GetCRC(seq, seq.Length, crc);
                if (crc != header.crc)
                {
                    Debug.LogError($"CRC error : {header.pId.ToString()}");
                    return;
                }

                string byteToString = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);

                //받은 리스트에 추가.
                lock (_lockPacketQueue)
                {
                    _lastPacketSeq = header.seq;
                    _receivePacketQueue.Enqueue(new Packet(header, byteToString));
                }

                int offset = (int)_totalReceiveSize;
                //사용한 만큼 버퍼 삭제하고 남은 부분 좌측 시프트      
                for (int i = 0; i < _accumReceiveSize; i++)
                {
                    if (_accumReceiveSize > _totalReceiveSize)
                    {
                        _streamBuffer[i] = _streamBuffer[offset + i];
                    }
                    _streamBuffer[offset + i] = 0;
                }
                _accumReceiveSize -= _totalReceiveSize;
                _totalReceiveSize = 0;
            }
        }

        /// <summary>
        /// 헤더 사이즈로 받음
        /// </summary>
        /// <param name="asyncResult"></param>
        private void ReceiveHeaderCallback(IAsyncResult asyncResult)
        {
            int receiveSize = 0;

            if (_tcpSocket == null || State == ClientState.Disconnecting) return;

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
                if (_tcpSocket is { Connected: true })
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
                _accumReceiveSize += (uint)receiveSize;          //누적 바이트 더함.

                //헤더보다 적은경우
                if (_accumReceiveSize < _totalReceiveSize)
                {
                    //다시 추가로 받음.
                    Read(_totalReceiveSize - _accumReceiveSize, (int)_accumReceiveSize, ReceiveHeaderCallback);
                    return;
                }
                _receiveHeader = new Header(_streamBuffer);
                if (_totalReceiveSize > STREAM_BUFFER_SIZE)
                {
                    //body가 버퍼보다 크다.
                    Debug.LogError($"Packet size error : {_totalReceiveSize.ToString()}");
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
        /// <param name="asyncResult"></param>
        private void ReceiveBodyCallback(IAsyncResult asyncResult)
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
                Debug.LogError("Close socket ReceiveBodyCallback");
                Disconnect();
            }
            else
            {
                _accumReceiveSize += (uint)receiveSize;
                //현재 스트림 모두 다 채웟는지
                uint iRemainSize = _totalReceiveSize - _accumReceiveSize;
                if (iRemainSize > 0)
                {
                    //스트림 읽기 계속               
                    Read(iRemainSize, (int)_accumReceiveSize, ReceiveBodyCallback);
                }
                else
                {
                    //패킷 다 받음 - 패킷 구성
                    GenerateAES_IV(_receiveHeader.seq);

                    //검증 후 string으로 변환
                    byte[] bytes = AES128.Decrypt(_streamBuffer, 0, (int)_totalReceiveSize, _key, _iv);
                    if (bytes == null)
                    {
                        Debug.LogError($"Decrypt error : {_receiveHeader.pId.ToString()}");
                        return;
                    }

                    uint crc = CRC32.GetCRC(bytes, bytes.Length);
                    var pid = BitConverter.GetBytes(_receiveHeader.pId);
                    var seq = BitConverter.GetBytes(_receiveHeader.seq);
                    crc = CRC32.GetCRC(pid, pid.Length, crc);
                    crc = CRC32.GetCRC(seq, seq.Length, crc);
                    if (crc != _receiveHeader.crc)
                    {
                        Debug.LogError($"CRC error : {_receiveHeader.pId.ToString()}");
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
