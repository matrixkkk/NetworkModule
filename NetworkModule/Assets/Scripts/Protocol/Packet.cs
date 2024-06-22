using System;
using System.IO;
using System.Security.Cryptography;

namespace Assets.Scripts.Protocol
{
    /// <summary>
    /// 보내는 메시지 인터페이스
    /// </summary>
    public interface iSendMessage
    {
        string ToJson();
    }

    public interface iReceiveMessage
    {
        bool IsError();
        string ToJson();
    }

    /// <summary>
    /// 패킷
    /// </summary>
    public struct Packet
    {
        public const int  HEADER_SIZE = 16;
        public const uint MAGIC = 0xfe1a2b3c;       //고정 값
        public const int MINIMUM_BODY_SIZE = 16;

        //헤더에 들어갈 정보
        private Header _header;
        private string _jsonStr;                 //jsonStr : 서버에서 받음

        public ushort ID => _header.pId;
        public ushort Seq { set => _header.seq = value; }
        public string Str => _jsonStr;
        public static string Empty => "{}";


        public Packet(ushort id, string json)
        {
            _header = new Header()
            {
                pId = id,
                seq = 0,
                magic = 0,
                size = 0,
                crc = 0
            };
            _jsonStr = json;
        }

        public Packet(Header aHeader, string json)
        {
            _header = aHeader;
            _jsonStr = json;
        }

        public int ToByte(byte[] outputBuffer, byte[] key, byte[] iv, bool crcCheck)
        {
            _jsonStr += "\0";

            int bodySize = System.Text.Encoding.UTF8.GetBytes(_jsonStr, 0, _jsonStr.Length, outputBuffer, 0);          


            if (crcCheck)
            {
                uint crc = CRC32.GetCRC(outputBuffer, bodySize);
                var pid = System.BitConverter.GetBytes(_header.pId);
                var seq = System.BitConverter.GetBytes(_header.seq);
                crc = CRC32.GetCRC(pid, pid.Length, crc);
                crc = CRC32.GetCRC(seq, seq.Length, crc);
                _header.crc = crc;
            }

            try
            {
                byte[] bytes = AES128.Encrypt(outputBuffer, bodySize, key, iv);
                Array.Copy(bytes, 0, outputBuffer, HEADER_SIZE, bytes.Length);
                bodySize = bytes.Length;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e.Message);
                return 0;
            }

            _header.size = (uint)(bodySize + HEADER_SIZE);

            int offset = 0;
            uintToByte(MAGIC, outputBuffer, offset);
            offset += sizeof(uint);

            ushortToByte(_header.pId, outputBuffer, offset);
            offset += sizeof(ushort);

            ushortToByte(_header.seq, outputBuffer, offset);
            offset += sizeof(ushort);

            uintToByte(_header.size, outputBuffer, offset);
            offset += sizeof(uint);

            uintToByte(_header.crc, outputBuffer, offset);
            offset += sizeof(uint);
            return (int)_header.size;
        }

        private void uintToByte(uint aValue, byte[] buffer, int offset)
        {
            buffer[offset] = (byte)(aValue);
            buffer[offset + 1] = (byte)(aValue >> 8);
            buffer[offset + 2] = (byte)(aValue >> 16);
            buffer[offset + 3] = (byte)(aValue >> 24);
        }

        private void ushortToByte(ushort aValue, byte[] buffer, int offset)
        {
            buffer[offset] = (byte)(aValue);
            buffer[offset + 1] = (byte)(aValue >> 8);
        }
    }
}
