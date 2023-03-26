using System;
using UnityEngine;

namespace Assets.Scripts.Protocol
{
    /// <summary>
    /// 패킷 Header
    /// </summary>
    public struct Header
    {
        public uint magic;      //0xffe3e4e5(고정값)
        public ushort pId;              //패킷 id
        public ushort seq;              //시퀀스
        public uint size;              //패킷 전체 사이즈(헤더포함). 최대 4KB = 4096bytes
        public uint crc;              //패킷 CRC값

        public Header(byte[] buffer)
        {
            int iIndex = 0;
            magic = BitConverter.ToUInt32(buffer, iIndex);
            iIndex += sizeof(uint);
            pId = BitConverter.ToUInt16(buffer, iIndex);
            iIndex += sizeof(ushort);
            seq = BitConverter.ToUInt16(buffer, iIndex);
            iIndex += sizeof(ushort);
            size = BitConverter.ToUInt32(buffer, iIndex);
            iIndex += sizeof(uint);
            crc = BitConverter.ToUInt32(buffer, iIndex);
        }
    }   
}
