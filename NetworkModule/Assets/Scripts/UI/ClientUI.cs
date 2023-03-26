using Assets.Scripts.ClientSide;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using Assets.Scripts.Protocol;

/// <summary>
/// 클라이언트 UI
/// </summary>
public class ClientUI : MonoBehaviour
{
    public Text tID;
    public Text ping;

    private NetworkPeer peer;

    private float lastPingSendTime = 0f;

    private void Start()
    {
        StartCoroutine(PingLoop());
    }

    private void Update()
    {
        if(peer!= null && !peer.Connected)
            GameObject.Destroy(gameObject);
    }


    public void SetID(string aID)
    {
        tID.text = aID;
        tID.color = Color.red;
    }

    /// <summary>
    /// peer 등록
    /// </summary>
    /// <param name="p"></param>
    public void SetPeer(NetworkPeer p)
    {
        peer = p;
        peer.OnReceive = this.OnReceivePacket;
    }

    public void Login()
    {
        Login_Send send = new Login_Send()
        {
            id = tID.text
        };
        peer.AddQueue(new Packet((ushort)PacketID.Login_Send, send.ToJson()));
    }

    public void OnClickDisconnect()
    {
        peer?.Disconnect();
        GameObject.Destroy(gameObject);
    }

    public void OnReceivePacket(Packet p)
    {
        PacketID id = (PacketID)p.ID;
        switch(id)
        {
            case PacketID.Login_Recv:
                {
                    Login_Recv recv = JsonUtility.FromJson<Login_Recv>(p.Str);
                    if(!recv.IsError())
                    {
                        peer.SessionID = recv.session;
                        tID.color = Color.green;
                    }
                }
                break;
            case PacketID.Ping_Recv:
                {
                    Ping_Recv recv = JsonUtility.FromJson<Ping_Recv>(p.Str);
                    if(!recv.IsError())
                    {
                        float delta = Time.realtimeSinceStartup - lastPingSendTime;
                        ping.text = (int)(delta * 1000) + "ms";
                    }
                }
                break;
        }
    }

    IEnumerator PingLoop()
    {
        ping.text = "";
        while (peer == null)
            yield return null;

        while (peer.Connected)
        {
            yield return new WaitForSeconds(1f);

            if (!peer.HasSession)
                continue;

            lastPingSendTime = Time.realtimeSinceStartup;
            peer.AddQueue(PacketID.Ping_Send, new Login_Send());
            peer.SendOut();
        }
    }
}
