using System;
using Assets.Scripts.Protocol;
using UnityEngine;

namespace ServerSide
{
    [Serializable]
    public struct PingReceive : iSendMessage
    {
        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }

    [Serializable]
    public struct PingSend : iReceiveMessage
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