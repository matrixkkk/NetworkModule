using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.ClientSide
{

    /// <summary>
    /// 서버 설정
    /// </summary>
    [CreateAssetMenu(fileName = "ServerSettings", menuName = "ScriptableObjects/ServerSettings")]
    public class ServerSettings : ScriptableObject
    {
        public enum eServerType
        {
            None,
            DEV,
        }
        public eServerType serverType;
        public int port;

        public string devServerDomain;
        public float backgroundTimeout = 60.0f;     //background timeout(60초)


        #region [ 암호화 키 ]
        public short[] fakeKey = {
            0x0b1d, 0x01ad, 0x0b64, 0x0b57,
            0x1afb, 0x0f9d, 0x0ca9, 0x1339,
            0x048a, 0x0111,0x0669, 0x0868,
            0x1062, 0x1cd3, 0x1e54, 0x1043
        };
        public short[] xorKey = {
                0x0b96, 0x0135, 0x0bf0, 0x0b0a,
                0x1ad2, 0x0ff1, 0x0ce8, 0x13b8,
                0x0430, 0x01d5, 0x0631, 0x080b,
                0x109c, 0x1cd8, 0x1e3d, 0x1047
        };

        private byte[] mKey = null;
        #endregion

        /// <summary>
        /// 초기화
        /// </summary>
        public void Initialize()
        {
            List<byte> byteList = new List<byte>();

            for (int i = 0; i < fakeKey.Length; i++)
            {
                short fake = fakeKey[i];
                short xor = xorKey[i];
                byteList.Add((byte)(fake ^= xor));
            }

            mKey = byteList.ToArray();
        }

        public byte[] GetKey()
        {
            if (mKey == null || mKey.Length == 0)
            {
                Initialize();
            }
            return mKey;
        }

        public string GetDomain()
        {
            if (serverType == eServerType.DEV)
            {
                return devServerDomain;
            }
            return "";
        }
    }

}
