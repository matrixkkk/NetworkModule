using System;
using UnityEngine;

namespace Assets.Scripts.Protocol
{
    [Serializable]
    public struct Login_Send : iSendMessage
    {
        public string id;

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }

    [Serializable]
    public struct Login_Recv : iReceiveMessage
    {
        public ulong session;
        public int error;

        public bool IsError()
        {
            return error != 0;
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }
}
