using System;
using Assets.Scripts.Protocol;
using UnityEngine;

namespace ServerSide
{
    [Serializable]
    public struct EnterRoomSend : iSendMessage
    {
        public int roomNumber;

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }
}