using Assets.Scripts.Protocol;
using Assets.Scripts.ServerSide;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Unity.Properties;
using UnityEngine;

public class Server : MonoBehaviour
{
    /// <summary>
    /// 받은 패킷
    /// </summary>
    public struct RecvPacket
    {
        public AsyncObject owner;
        public Packet packet;
    }

    private int port = 20000;
    private AddressFamily addressFamily = AddressFamily.InterNetwork;
    private int listenCount = 10;
    private int recvBufferSize = 4096;

    private Socket bindSocket;                   //bind 소켓
    private bool mIsRunning = false;            //서버 구동중
    private object mPacketLock = new object();    //packet lock
    private object mAsyncObjLock = new object();

    private List<AsyncObject> mAsyncObjectList = new List<AsyncObject>();                //비동기 객체 리스트
    private Queue<RecvPacket> mRecvPacketQueue = new Queue<RecvPacket>();           //받은 패킷 큐.
    private int mWaitPacketCount = 0;

    private byte[] mBaseKey;                    //기본 암호화 키
    private byte[] mCryptoKey;                  //변하는 키

    private ulong sessionID = 0;                //발급 세션 id
    private object session_lock = new object();

    #region [ properties ]
    public bool IsRunning { get { return mIsRunning; } }
    #endregion

    #region [ callbacks ]
    public delegate void OnAccept(string address);
    private OnAccept mOnAccept;
    public OnAccept OnAcceptCallback { set { mOnAccept = value; } }
   
    #endregion

    #region [ private ]


    private void Accept()
    {
        bindSocket.BeginAccept(AcceptCallback, null);
        mIsRunning = true;
    }



    /// <summary>
    /// 패킷이 존재하면 처리
    /// </summary>
    public void Dispatch()
    {
        if (mRecvPacketQueue.Count == 0) return;

        RecvPacket p;
        lock (mPacketLock)
        {
            while (mRecvPacketQueue.Count > 0)
            {
                p = mRecvPacketQueue.Dequeue();
                ProcessPacket(p);
            }
        }
    }

    private void ProcessPacket(RecvPacket p)
    {
        if (p.packet.ID == (ushort)PacketID.Login_Send)
        {
            Login_Send login = JsonUtility.FromJson<Login_Send>(p.packet.Str);

            Debug.Log("[Server] Login : " + login.id);

            ulong sessionID = IssueSessionID();

            p.owner.SetSessionID(sessionID);

            //로그인 검증 후 -
            //성공 패킷 다시 보냄.
            Login_Recv recv = new Login_Recv()
            {
                session = sessionID,
                error = 0
            };
            ushort recvID = (ushort)(p.packet.ID + 1);
            p.owner.Send(recvID, recv.ToJson());
        }
        else if(p.packet.ID == (ushort)PacketID.Ping_Send)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 2000; i++)
            {
                sb.Append("1");
            }
            //로그인 검증 후 -
            //성공 패킷 다시 보냄.
            Ping_Recv recv = new Ping_Recv()
            {
                error = 0,
                text = sb.ToString()
            };
            ushort recvID = (ushort)(p.packet.ID + 1);
            p.owner.Send(recvID, recv.ToJson());

            sb.Clear();
            for (int i = 0; i < 200; i++)
            {
                sb.Append("2");
            }
            Ping_Recv send = new Ping_Recv()
            {
                error = 0,
                text = sb.ToString()
            };
            p.owner.Send(recvID, send.ToJson());
        }
    }

    private ulong IssueSessionID()
    {
        ulong id = 0;
        lock(session_lock)
        {
            sessionID++;
            id = sessionID;
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

        mCryptoKey = new byte[fake.Length];

        for (int i = 0; i < fake.Length; i++)
        {
            mCryptoKey[i] = (byte)(fake[i] ^= xorCode[i]);
        }

        mBaseKey = new byte[mCryptoKey.Length];
        Array.Copy(mCryptoKey, mBaseKey, mBaseKey.Length);
    }
    #endregion

    #region [ coroutine ]
    private async void ProcessPacketLoop()
    {
        while (mIsRunning)
        {
            Dispatch();
            await Task.Delay(1);
        }
    }
    #endregion

    #region [ callbacks ]
    private void AcceptCallback(IAsyncResult ar)
    {
        if (bindSocket == null) return;

        Socket clientSocket = bindSocket.EndAccept(ar);

        //다시 대기
        bindSocket.BeginAccept(AcceptCallback, null);
        Debug.Log("Accept : " + clientSocket.RemoteEndPoint.ToString());        

        AsyncObject asyncObj = new AsyncObject(clientSocket, recvBufferSize, mBaseKey);

        asyncObj.OnReceive = OnReceivePacket;
        asyncObj.OnCloseSocket = OnCloseSocket;
        asyncObj.ReceiveStart();
        lock (mAsyncObjLock)
        {
            mAsyncObjectList.Add(asyncObj);
        }

        if (mOnAccept != null)
        {
            mOnAccept(clientSocket.RemoteEndPoint.ToString());
        }
    }

    /// <summary>
    /// 받은 패킷 추가.
    /// 서브 스레드 접근
    /// </summary>
    /// <param name="p"></param>
    private void OnReceivePacket(Packet p, AsyncObject owner)
    {
        lock (mPacketLock)
        {
            RecvPacket recvPacket = new RecvPacket();
            recvPacket.owner = owner;
            recvPacket.packet = p;

            mRecvPacketQueue.Enqueue(recvPacket);
            mWaitPacketCount = mRecvPacketQueue.Count;
        }
    }

    /// <summary>
    /// 소켓 close 콜백
    /// </summary>
    /// <param name="owner"></param>
    private void OnCloseSocket(AsyncObject owner)
    {
        lock(mAsyncObjLock)
        {
            mAsyncObjectList.Remove(owner);
        }
    }
    #endregion

    #region [ public ]


    public void StartServer()
    {
        if (bindSocket != null)
        {
            return;
        }
        Debug.Log("Start Server");

        CreateCryptoKey();

        bindSocket = new Socket(addressFamily, SocketType.Stream, ProtocolType.IP);
        if (bindSocket != null)
        {
            //바인딩
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Any, port);
            bindSocket.Bind(serverEP);
            bindSocket.Listen(listenCount);

            Accept();
            ProcessPacketLoop();
        }
    }


    public void EndServer()
    {
        if (bindSocket == null) return;

        Debug.Log("End Server");
        bindSocket.Close();
        bindSocket = null;

        mIsRunning = false;

        foreach (AsyncObject obj in mAsyncObjectList)
        {
            obj.Close();
        }
    }
    #endregion
}
