using Assets.Scripts.Protocol;
using System;
using System.Collections.Generic;
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
        readonly uint STREAM_BUFFER_SIZE = 8192;     //스트림 버퍼 사이즈
        public readonly uint RECEIVE_SIZE = 8192;
        public readonly uint SEND_SIZE = 4096;

        private TcpClient mTcpSocket;                //네트워크 소켓
        private NetworkStream mStream = null;

        private object mLockPacketQueue = new object();     //동기화 객체
        private Header mReceiveHeader;                   //받은 헤더 정보
        private byte[] mStreamBuffer;                  //스트림 버퍼.

        private byte[] mSendBuffer;                    //send 버퍼
        private uint mTotalRecvSize = 0;               //이번에 받을 패킷의 총 사이즈
        private uint mAccumRecvSize = 0;               //누적 recv size
        private ushort mLastPacketSeq = 0;             //마지막 패킷 시퀀스

        private Queue<Packet> mReceivePacketQueue = new Queue<Packet>();        //받은 패킷 큐
        private Queue<Packet> mSendPacketQueue = new Queue<Packet>();     //send 패킷 큐

        private byte[] mKey;                               //암호화 키
        private byte[] mIv = new byte[16];                 //iv
        private ulong mSessionID;                         //세션 ID

        private bool mIsInitializeConnect = false;      //초기화 여부
        private bool mIsSendAsk = false;
        private eClientState mClientState = eClientState.UnInitialized;                           //클라이언트 상태

        #region callbacks
        public delegate void OnConnectCallback();
        public delegate void OnDisconnnectCallback();
        public delegate void OnReceiveCallback(Packet p);

        public OnConnectCallback OnConnect { get; set; }
        public OnDisconnnectCallback OnDisconnect { get; set; }
        public OnReceiveCallback OnReceive { get; set; }
        #endregion

        public bool IsInitialConnect { get { return mIsInitializeConnect; } set { mIsInitializeConnect = value; } }
        public ulong SessionID { set { mSessionID = value; } }
        public bool HasSession { get => mSessionID != 0; }
        public bool IsSendAsk { get { return mIsSendAsk; } set { mIsSendAsk = true; } }

        public eClientState State { get { return mClientState; } set { mClientState = value; } }
        public Socket TcpSocket { get { return mTcpSocket.Client; } }
        public bool Connected { get { return mTcpSocket != null && mTcpSocket.Connected && mClientState == eClientState.Connected; } }
        public float LastSendOutTime { get; private set; }
        public byte[] EncryptKey { set => mKey = value; }

        public NetworkPeer()
        {
            mStreamBuffer = new byte[STREAM_BUFFER_SIZE];
            mSendBuffer = new byte[SEND_SIZE];
        }

        public bool Connect(string serverAddress, int port)
        {
            if (mClientState == eClientState.Disconnecting)
            {
                Debug.LogError("Connect() failed. Can't connect while disconnecting (still). Current state");
                return false;
            }

            Debug.Log("Connecting : " + serverAddress + " : " + port);

            bool isConnecting = Connecting(serverAddress, port);
            if (isConnecting)
            {
                mClientState = eClientState.Connecting;
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
                mIsInitializeConnect = false;
                mClientState = eClientState.Disconnected;
                Debug.LogError(e.Message);
                return false;
            }

            if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                mTcpSocket = new TcpClient(ipAddress.AddressFamily);
            }
            else
            {
                mTcpSocket = new TcpClient();
            }

            mTcpSocket.ReceiveBufferSize = (int)RECEIVE_SIZE;
            mTcpSocket.SendBufferSize = (int)SEND_SIZE;
            mTcpSocket.NoDelay = true;
            mTcpSocket.BeginConnect(ipAddress, port, new AsyncCallback(CallbackConnectResult), mTcpSocket);

            return true;
        }

        /// <summary>
        /// 패킷이 존재하면 처리
        /// </summary>
        public bool Dispatch()
        {
            if (mReceivePacketQueue.Count == 0) return false;

            Packet p;
            lock (mLockPacketQueue)
            {
                p = mReceivePacketQueue.Dequeue();
            }
            OnReceive?.Invoke(p);
            return mReceivePacketQueue.Count > 0;
        }

        /// <summary>
        /// 전송 큐에 추가
        /// </summary>
        /// <param name="p"></param>
        public void AddQueue(Packet p)
        {
            mSendPacketQueue.Enqueue(p);
        }

        public void AddQueue(PacketID id, iSendMessage msg)
        {
            mSendPacketQueue.Enqueue(new Packet((ushort)id, msg.ToJson()));
        }

        /// <summary>
        /// 패킷 보냄.
        /// </summary>
        public bool SendOut()
        {
            if (!Connected || mSendPacketQueue.Count == 0) return false;

            SendPacket(mSendPacketQueue.Dequeue());
            return mSendPacketQueue.Count > 0;
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
                mClientState = eClientState.Disconnected;
                return;
            }

            if (tcp.Connected)
            {
                //이전 보낼 패킷 삭제
                if (mSendPacketQueue.Count > 0)
                {
                    mSendPacketQueue.Clear();
                }

                mClientState = eClientState.Connected;

                //스트림 가져옴.
                mStream = tcp.GetStream();

                Debug.Log("Connect success : " + DateTime.Now.ToString());
                ReadHeader();
                OnConnect?.Invoke();
            }
            else
            {
                mClientState = eClientState.Disconnected;
            }
        }

        private void ReadHeader()
        {
            mAccumRecvSize = 0;
            mTotalRecvSize = Packet.HEADER_SIZE;
            Read(mTotalRecvSize, new AsyncCallback(RecvHeaderCallback));
        }

        private void ReadBody()
        {
            mAccumRecvSize = 0;
            mTotalRecvSize = (mReceiveHeader.size - Packet.HEADER_SIZE);
            Read(mTotalRecvSize, new AsyncCallback(RecvBodyCallback));
        }


        private bool Read(uint receiveSize, AsyncCallback callback)
        {
            //패킷 받기 시작.
            try
            {
                if (mStream.CanRead == false)
                {
                    Debug.LogError("stream can not read");
                    return false;
                }
                mStream.BeginRead(mStreamBuffer, (int)mAccumRecvSize, (int)receiveSize, callback, this);
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
            if (mSessionID > 0)
            {
                UInt64 high = mSessionID;
                UInt64 low = seq;
                Buffer.BlockCopy(BitConverter.GetBytes(high), 0, mIv, 0, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(low), 0, mIv, 8, 8);
            }
            else
            {
                Array.Clear(mIv, 0, mIv.Length);
            }
        }

        public void SendAck()
        {
           //정의 된 ack 패킷을 보냅니다.
        }

        /// <summary>
        /// 패킷 전송
        /// </summary>
        /// <param name="p"></param>
        private void SendPacket(Packet p)
        {
            LastSendOutTime = Time.realtimeSinceStartup;

            p.Seq = mLastPacketSeq;

            //iv 구성
            GenerateAES_IV(mLastPacketSeq);

            //패킷 전송
            byte[] buffer = mSendBuffer;
            int size = p.ToByte(mSendBuffer, mKey, mIv, true);
            if (buffer != null)
            {
                NetworkStream netStream = mTcpSocket.GetStream();

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
            mSessionID = 0;
            if (mTcpSocket != null && mTcpSocket.Connected)
            {
                OnDisconnect?.Invoke();

                try
                {
                    mTcpSocket.Client.Shutdown(SocketShutdown.Both);
                    mTcpSocket.Client.BeginDisconnect(false, new AsyncCallback(CallbackDisconnect), this);
                    mClientState = eClientState.Disconnecting;
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
            if (mTcpSocket != null)
            {
                mTcpSocket.Close();
                mTcpSocket = null;
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
                peer.mClientState = eClientState.Disconnected;
                peer.mIsInitializeConnect = false;

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

        /// <summary>
        /// 헤더 사이즈로 받음
        /// </summary>
        /// <param name="aResult"></param>
        protected void RecvHeaderCallback(IAsyncResult asyncResult)
        {
            int receiveSize = 0;

            if (mTcpSocket == null || mClientState == eClientState.Disconnecting) return;

            try
            {
                //소켓이 연결 중이 아님.
                if (mTcpSocket.Connected && mStream != null)
                {
                    receiveSize = mStream.EndRead(asyncResult);
                }
                else
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
                if (mTcpSocket != null && mTcpSocket.Connected)
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
                mAccumRecvSize += (uint)receiveSize;          //누적 바이트 더함.

                //헤더보다 적은경우
                if (mAccumRecvSize < mTotalRecvSize)
                {
                    //다시 추가로 받음.
                    Read(mTotalRecvSize - mAccumRecvSize, new AsyncCallback(RecvHeaderCallback));
                    return;
                }
                mReceiveHeader = new Header(mStreamBuffer);
                if (mTotalRecvSize > STREAM_BUFFER_SIZE)
                {
                    //body가 버퍼보다 크다.
                    Debug.LogError("Packet size error : " + mTotalRecvSize);
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
                if (mStream != null && mTcpSocket.Connected)
                {
                    receiveSize = mStream.EndRead(asyncResult);
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
                mAccumRecvSize += (uint)receiveSize;
                //현재 스트림 모두 다 채웟는지
                uint iRemainSize = mTotalRecvSize - mAccumRecvSize;
                if (iRemainSize > 0)
                {
                    //스트림 읽기 계속               
                    Read(iRemainSize, new AsyncCallback(RecvBodyCallback));
                }
                else
                {
                    //패킷 다 받음 - 패킷 구성
                    GenerateAES_IV(mReceiveHeader.seq);

                    //검증 후 string으로 변환
                    byte[] bytes = AES128.Decrypt(mStreamBuffer, (int)mTotalRecvSize, mKey);
                    if(bytes ==  null)
                    {
                        Debug.LogError("Decrypt error : " + mReceiveHeader.pId);
                        return;
                    }

                    uint crc = CRC32.GetCRC(bytes, (int)bytes.Length);
                    var pid = System.BitConverter.GetBytes(mReceiveHeader.pId);
                    var seq = System.BitConverter.GetBytes(mReceiveHeader.seq);
                    crc = CRC32.GetCRC(pid, pid.Length, crc);
                    crc = CRC32.GetCRC(seq, seq.Length, crc);
                    if (crc != mReceiveHeader.crc)
                    {
                        Debug.LogError("CRC error : " + mReceiveHeader.pId);
                        return;
                    }

                    string byteToString = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);

                    //받은 리스트에 추가.
                    lock (mLockPacketQueue)
                    {
                        mLastPacketSeq = mReceiveHeader.seq;
                        mReceivePacketQueue.Enqueue(new Packet(mReceiveHeader, byteToString));
                    }
                    ReadHeader();
                }
            }
        }
    }
}
