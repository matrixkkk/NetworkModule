using System;
using Assets.Scripts.Protocol;
using UnityEngine;

namespace ServerSide
{
    [Serializable]
    public struct LoginReceive : iSendMessage
    {
        public string id;

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }

    [Serializable]
    public struct LoginSend : iReceiveMessage
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