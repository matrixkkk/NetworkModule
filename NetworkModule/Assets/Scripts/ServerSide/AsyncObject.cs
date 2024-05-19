using Assets.Scripts.Protocol;
using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using UnityEngine;

namespace Assets.Scripts.ServerSide
{
    /// <summary>
    /// 소켓 객체
    /// </summary>
    public class SocketObject
    {
        private const int HEADER_SIZE = 16;

        private Socket _socket;
        private readonly byte[] _buffer;

        private Header _header;                  //받은 헤더 정보
        private bool _receiveHeader = true;         //헤더 받음.

        private int _receiveOffset;
        private int _receiveSize;
        private readonly byte[] _key;
        private readonly byte[] _iv;
        private ushort _seq;
        private ulong _sessionID;

        private bool _isError;
        private string _errorText;
        public bool IsError => _isError;
        public string ErrorText => _errorText;

        public delegate void OnReceivePacketCallback(Packet p, SocketObject target);

        public OnReceivePacketCallback OnReceive { get; set; }
        public delegate void OnSocketCloseCallback(SocketObject owner);
        public OnSocketCloseCallback OnCloseSocket { get; set; }

        public ulong SessionID => _sessionID;


        public SocketObject(int bufferSize, byte[] aKey)
        {
            _buffer = new byte[bufferSize];
            _receiveOffset = 0;
            _key = aKey;
            _sessionID = 0;
            _iv = new byte[16];
        }

        public void Reset()
        {
            _receiveOffset = 0;
            _sessionID = 0;
            _socket = null;
        }

        public void SetSocket(Socket socket)
        {
            _socket = socket;
        }

        public void SetSessionID(ulong id)
        {
            _sessionID = id;
        }

        /// <summary>
        /// 받기 시작.
        /// </summary>
        public void ReceiveStart()
        {
            _socket.BeginReceive(_buffer, 0, HEADER_SIZE, SocketFlags.None, ReceiveCallback, this);
        }

        public void Close()
        {
            _socket.Close();
        }

        private void ReceiveCallback(IAsyncResult asyncResult)
        {
            //주 스레드가 아님
            SocketObject asyncObj = (SocketObject)asyncResult.AsyncState;

            if (asyncObj._socket == null ||
                !asyncObj._socket.Connected) return;

            int size = asyncObj._socket.EndReceive(asyncResult);
            if (size == 0)
            {
                OnCloseSocket?.Invoke(this);
            }
            else
            {
                if (_receiveHeader)
                {
                    ReceiveHeader(size);
                }
                else
                {
                    ReceiveBody(size);
                }
            }
        }

        private void ReceiveHeader(int size)
        {
            _receiveOffset += size;
            if (_receiveOffset < HEADER_SIZE)
            {
                _socket.BeginReceive(_buffer, _receiveOffset, HEADER_SIZE - _receiveOffset, SocketFlags.None,  ReceiveCallback, this);
            }
            else
            {
                _header = new Header(_buffer);
                _receiveHeader = false;
                _receiveSize = (int)(_header.size - HEADER_SIZE);

                Array.Clear(_buffer, 0, _buffer.Length);
                //body 받음
                _socket.BeginReceive(_buffer, 0, _receiveSize, SocketFlags.None, ReceiveCallback, this);
                _receiveOffset = 0;
            }
        }
        private void ReceiveBody(int size)
        {
            _receiveOffset += size;
            if (_receiveOffset < _receiveSize)
            {
                _socket.BeginReceive(_buffer, _receiveOffset, _receiveSize - _receiveOffset, SocketFlags.None, ReceiveCallback, this);
            }
            else
            {
                if (_sessionID > 0)
                {
                    UInt64 high = _sessionID;
                    UInt64 low = _header.seq;
                    Buffer.BlockCopy(BitConverter.GetBytes(high), 0, _iv, 0, 8);
                    Buffer.BlockCopy(BitConverter.GetBytes(low), 0, _iv, 8, 8);
                }
                else
                {
                    Array.Clear(_iv, 0, _iv.Length);
                }
                byte[] bytes = AES128.Decrypt(_buffer, 0, _receiveSize, _key);
               
                uint crc = CRC32.GetCRC(bytes, bytes.Length);
                var pid = BitConverter.GetBytes(_header.pId);
                var seq = BitConverter.GetBytes(_header.seq);
                crc = CRC32.GetCRC(pid, pid.Length, crc);
                crc = CRC32.GetCRC(seq, seq.Length, crc);
                if (crc != _header.crc)
                {
                    return;
                }

                string byteToString = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);

                Packet newPacket = new Packet(_header, byteToString);
                OnReceive?.Invoke(newPacket, this);

                _receiveOffset = 0;
                _receiveHeader = true;
                ReceiveStart();
            }
        }

        public void Send(ushort id, string jsonStr)
        {
            Packet p = new Packet(id, jsonStr)
            {
                Seq = _seq++
            };

            UInt64 high = _sessionID;
            UInt64 low = _seq;
            Buffer.BlockCopy(BitConverter.GetBytes(high), 0, _iv, 0, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(low), 0, _iv, 8, 8);

            //패킷 전송
            int size = p.ToByte(_buffer, _key, _iv, true);

            try
            {
                _socket.BeginSend(_buffer, 0, size, SocketFlags.None, CallbackSendResult, _socket);
            }
            catch (SocketException e)
            {
                _isError = true;
                _errorText = $"socket BeginSend Error : {e.Message}";
            }
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
                _isError = true;
                _errorText = $"send error : {e.Message}";
            }
        }
    }
}
