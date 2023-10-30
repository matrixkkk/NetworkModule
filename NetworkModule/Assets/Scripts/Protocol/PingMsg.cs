using System;
using UnityEngine;

namespace Assets.Scripts.Protocol
{
    [Serializable]
    public struct Ping_Send : iSendMessage
    {
        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }

    [Serializable]
    public struct Ping_Recv : iReceiveMessage
    {
        public int error;
        public string text;

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
