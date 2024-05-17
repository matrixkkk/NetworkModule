using System;
using Assets.Scripts.Protocol;
using UnityEngine;

namespace ClientSide
{
    [Serializable]
    public struct LoginSend : iSendMessage
    {
        public string id;

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }

    [Serializable]
    public struct LoginReceive : iReceiveMessage
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