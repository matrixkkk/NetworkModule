using Assets.Scripts.ServerSide;
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
        private Header header;
        private string jsonStr;                 //jsonStr : 서버에서 받음

        public ushort ID => header.pId;
        public ushort Seq { set => header.seq = value; }
        public string Str => jsonStr;
        public static string Empty => "{}";


        public Packet(ushort id, string json)
        {
            header = new Header()
            {
                pId = id,
                seq = 0,
                magic = 0,
                size = 0,
                crc = 0
            };
            jsonStr = json;
        }

        public Packet(Header aHeader, string json)
        {
            header = aHeader;
            jsonStr = json;
        }

        public int ToByte(byte[] outputBuffer, byte[] key, byte[] iv, bool crcCheck)
        {
            jsonStr += "\0";

            int bodySize = System.Text.Encoding.UTF8.GetBytes(jsonStr, 0, jsonStr.Length, outputBuffer, 0);          


            if (crcCheck)
            {
                uint crc = CRC32.GetCRC(outputBuffer, bodySize);
                var pid = System.BitConverter.GetBytes(header.pId);
                var seq = System.BitConverter.GetBytes(header.seq);
                crc = CRC32.GetCRC(pid, pid.Length, crc);
                crc = CRC32.GetCRC(seq, seq.Length, crc);
                header.crc = crc;
            }

            try
            {
                byte[] bytes = AES128.Encrypt(outputBuffer, bodySize, key);
                Array.Copy(bytes, 0, outputBuffer, HEADER_SIZE, bytes.Length);
                bodySize = bytes.Length;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e.Message);
                return 0;
            }

            header.size = (uint)(bodySize + HEADER_SIZE);

            int offset = 0;
            uintToByte(MAGIC, outputBuffer, offset);
            offset += sizeof(uint);

            ushortToByte(header.pId, outputBuffer, offset);
            offset += sizeof(ushort);

            ushortToByte(header.seq, outputBuffer, offset);
            offset += sizeof(ushort);

            uintToByte(header.size, outputBuffer, offset);
            offset += sizeof(uint);

            uintToByte(header.crc, outputBuffer, offset);
            offset += sizeof(uint);
            return (int)header.size;
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
