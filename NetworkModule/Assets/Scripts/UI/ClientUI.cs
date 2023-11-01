using System;
using System.Collections;
using Assets.Scripts.Protocol;
using ClientSide;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// 클라이언트 UI
    /// </summary>
    public class ClientUI : MonoBehaviour
    {
        public Text txtID;
        public Text txtPing;

        public Action OnDisconnect { get; set; }

        public void SetId(string id)
        {
            txtID.text = id;
        }

        public void SetColor(Color color)
        {
            txtID.color = color;
        }

        public void SetPing(string ping)
        {
            txtPing.text = ping;
        }

        public void OnClickDisconnect()
        {
            OnDisconnect?.Invoke();
        }
    }
}
