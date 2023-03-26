using System.Net.Sockets;
using System;

namespace Assets.Scripts.ClientSide
{
    /// <summary>
    /// name : 네트워크 스트림
    /// author : khg
    /// name : 패킷을 받기위한 Stream 처리
    /// </summary>
    public class NetStream
    {
        public delegate void OnResultCallback(int resultCode);                            //결과 콜백

        public const uint RECEIVE_BUFFER_SIZE = 1048576;    //수신 버퍼 사이즈

        private NetworkStream mStream;                   //네트워크 스트림
        private Socket mSocket;

        private byte[] mByteBuffer = new byte[RECEIVE_BUFFER_SIZE];                       //수신 버퍼

        private uint mAccumRecvBytes;          //누적 바이트 수
        private uint mTotalRecvBytes;          //총 바이트 수

        private object read_lock = new object();    //read 스레드 동기
        private OnResultCallback mResultCallback;

        public byte[] ByteBuffer { get { return mByteBuffer; } }                                           //바이트 버퍼
        public uint AccumRecvBytes { get { return mAccumRecvBytes; } }      //받은 누적 바이트 수
        public uint TotalRecvBytes { get { return mTotalRecvBytes; } }      //총 받아야할 바이트 수

        public NetStream(NetworkStream _stream, Socket socket)
        {
            mSocket = socket;
            mStream = _stream;

            mAccumRecvBytes = 0;
        }

        public void CloseStream()
        {
            if (mStream != null)
            {
                try
                {
                    mStream.Dispose();
                    mStream = null;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError(e.Message);
                }
            }
        }

        /// <summary>
        /// 리셋
        /// </summary>
        public void Reset()
        {
            mAccumRecvBytes = 0;
            mTotalRecvBytes = 0;

            lock (read_lock)
            {
                Array.Clear(mByteBuffer, 0, mByteBuffer.Length);
            }
        }

        /// <summary>
        /// 받을 파일 사이즈 수 리턴
        /// </summary>
        /// <returns></returns>
        public uint GetRemainSize()
        {
            return mTotalRecvBytes - mAccumRecvBytes;
        }

        /// <summary>
        /// 스트림 읽기 시작
        /// </summary>
        /// <param name="iSize">현재 받을 사이즈</param>
        /// <param name="iTotalSize">총 받을 사이즈</param>
        /// <param name="callback"></param>
        public void BeginRead(uint iSize, OnResultCallback callback)
        {
            mResultCallback = callback;

            if (mStream.CanRead == false)
            {
                UnityEngine.Debug.LogError("Stream can read false");
                return;
            }

            AsyncCallback asyncCall = new System.AsyncCallback(ReadAsyncCallback);       //콜백 메소드

            mStream.BeginRead(mByteBuffer, (int)mAccumRecvBytes, (int)iSize, asyncCall, this);
        }

        public void SetTotalBytes(uint iSize)
        {
            mTotalRecvBytes = iSize;
        }

        /// <summary>
        /// read 결과 콜백
        /// </summary>
        /// <param name="asyncResult"></param>
        private void ReadAsyncCallback(IAsyncResult asyncResult)
        {
            int receiveSize = 0;
            lock (read_lock)
            {
                //소켓이 연결 중이 아님.
                if (mStream != null && mSocket.Connected)
                {
                    receiveSize = mStream.EndRead(asyncResult);
                }
                else
                {
                    return;
                }
            }

            if (receiveSize == 0)
            {
                //size가 0이면 소켓이 닫힘.
                mResultCallback(0);
            }
            else
            {
                mAccumRecvBytes += (uint)receiveSize;          //누적 바이트 더함.

                //정상적으로 받음
                mResultCallback(receiveSize);
            }
        }
    }
}