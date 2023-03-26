using Assets.Scripts.Protocol;
using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using UnityEngine;

namespace Assets.Scripts.ServerSide
{
    public class AsyncObject
    {
        private const int HEADER_SIZE = 16;

        public Socket socket;
        public readonly int bufferSize;

        public byte[] buffer;
        public byte[] outBuffer;

        //private GameServer gameServer;

        private Header header;                  //받은 헤더 정보
        private bool recvHeader = true;         //헤더 받음.

        private byte[] headerBytes;
        private int recvOffset;
        private int recvSize;
        private byte[] key;
        private byte[] iv;
        private ushort seq = 0;
        private ulong sessionID;
        private byte[] sendBuffer;

        public delegate void OnReceivePacketCallback(Packet p, AsyncObject target);

        public OnReceivePacketCallback OnReceive { get; set; }
        public delegate void OnSocketCloseCallback(AsyncObject owner);
        public OnSocketCloseCallback OnCloseSocket { get; set; }

        public ulong SessionID => sessionID;


        public AsyncObject(Socket aSocket, int aBufferSize, byte[] aKey)
        {
            socket = aSocket;
            bufferSize = aBufferSize;
            buffer = new byte[aBufferSize];
            outBuffer = new byte[aBufferSize];
            sendBuffer = new byte[aBufferSize];

            headerBytes = new byte[HEADER_SIZE];
            recvOffset = 0;
            key = aKey;
            sessionID = 0;
            iv = new byte[16];
        }

        public void SetSessionID(ulong id)
        {
            sessionID = id;
        }

        /// <summary>
        /// 받기 시작.
        /// </summary>
        public void ReceiveStart()
        {
            socket.BeginReceive(buffer, 0, HEADER_SIZE, SocketFlags.None, new AsyncCallback(ReceiveCallback), this);
        }

        public void Close()
        {
            socket.Close();
        }

        public void ReceiveCallback(IAsyncResult asyncResult)
        {
            //주 스레드가 아님
            AsyncObject asyncObj = (AsyncObject)asyncResult.AsyncState;

            if (asyncObj.socket == null ||
                !asyncObj.socket.Connected) return;

            int size = asyncObj.socket.EndReceive(asyncResult);
            if (size == 0)
            {
                //socket close;
                OnCloseSocket?.Invoke(this);
            }
            else
            {
                if (recvHeader)
                {
                    RecvHeader(size);
                }
                else
                {
                    RecvBody(size);
                }
            }
        }

        private void RecvHeader(int size)
        {
            recvOffset += size;
            if (recvOffset < HEADER_SIZE)
            {
                socket.BeginReceive(buffer, recvOffset, HEADER_SIZE - recvOffset, SocketFlags.None, new AsyncCallback(ReceiveCallback), this);
            }
            else
            {
                header = new Header(buffer);
                recvHeader = false;
                recvSize = (int)(header.size - HEADER_SIZE);

                Array.Clear(buffer, 0, buffer.Length);
                //body 받음
                socket.BeginReceive(buffer, 0, recvSize, SocketFlags.None, new AsyncCallback(ReceiveCallback), this);
                recvOffset = 0;
            }
        }
        private void RecvBody(int size)
        {
            recvOffset += size;
            if (recvOffset < recvSize)
            {
                socket.BeginReceive(buffer, recvOffset, recvSize - recvOffset, SocketFlags.None, new AsyncCallback(ReceiveCallback), this);
            }
            else
            {
                if (sessionID > 0)
                {
                    UInt64 high = sessionID;
                    UInt64 low = header.seq;
                    Buffer.BlockCopy(BitConverter.GetBytes(high), 0, iv, 0, 8);
                    Buffer.BlockCopy(BitConverter.GetBytes(low), 0, iv, 8, 8);
                }
                else
                {
                    Array.Clear(iv, 0, iv.Length);
                }
                byte[] bytes = AES128.Decrypt(buffer, recvSize, key);
               
                uint crc = CRC32.GetCRC(bytes, bytes.Length);
                var pid = System.BitConverter.GetBytes(header.pId);
                var seq = System.BitConverter.GetBytes(header.seq);
                crc = CRC32.GetCRC(pid, pid.Length, crc);
                crc = CRC32.GetCRC(seq, seq.Length, crc);
                if (crc != header.crc)
                {
                    Console.WriteLine("CRC error : " + header.pId);
                    return;
                }

                string byteToString = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);

                Packet newPacket = new Packet(header, byteToString);
                OnReceive?.Invoke(newPacket, this);

                recvOffset = 0;
                recvHeader = true;
                ReceiveStart();
            }
        }

        private void Decrypt(byte[] input, int offset, int inputSize, byte[] output, byte[] key, byte[] iv)
        {
            RijndaelManaged RijndaelCipher = new RijndaelManaged();

            RijndaelCipher.Key = key;
            //RijndaelCipher.IV = iv;
            RijndaelCipher.Mode = CipherMode.ECB;
            RijndaelCipher.Padding = PaddingMode.PKCS7;

            ICryptoTransform dectryption = RijndaelCipher.CreateDecryptor();

            //using (MemoryStream msEncrypt = new MemoryStream(output))
            //{
            //    using (CryptoStream cs = new CryptoStream(msEncrypt, dectryption, CryptoStreamMode.Write))
            //    {
            //        cs.Write(input, offset, inputSize);
            //    }
            //}

            output = dectryption.TransformFinalBlock(input, offset, inputSize);
        }

        public void Send(ushort id, string jsonStr)
        {
            Packet p = new Packet(id, jsonStr);

            p.Seq = seq++;
            UInt64 high = sessionID;
            UInt64 low = seq;
            Buffer.BlockCopy(BitConverter.GetBytes(high), 0, iv, 0, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(low), 0, iv, 8, 8);

            //패킷 전송
            int size = p.ToByte(buffer, key, iv, true);

            try
            {
                socket.BeginSend(buffer, 0, size, SocketFlags.None, new AsyncCallback(CallbackSendResult), socket);
            }
            catch (SocketException e)
            {
                Debug.LogError("socket BeginSend Error : " + e.Message);
            }
            Debug.Log("[Server] Send - " + "[" + p.ID + "] : " + jsonStr);
        }

        private void CallbackSendResult(IAsyncResult result)
        {
            Socket socket = (Socket)result.AsyncState;

            try
            {
                socket.EndSend(result);
            }
            catch (SocketException e)
            {
                Debug.LogError("[Server] : " + e.Message);
            }
        }
    }
}
