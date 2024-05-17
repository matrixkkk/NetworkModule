using System;
using Assets.Scripts.Protocol;
using UnityEngine;

namespace ClientSide
{
    [Serializable]
    public struct PingSend : iSendMessage
    {
        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }

    [Serializable]
    public struct PingReceive : iReceiveMessage
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