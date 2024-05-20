using System;
using Assets.Scripts.Protocol;
using UnityEngine;

namespace ClientSide
{
    [Serializable]
    public struct EnterRoom : iReceiveMessage
    {
        public int roomNumber;

        public bool IsError()
        {
            return roomNumber != -1;
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }
}