using System;
using Assets.Scripts.ClientSide;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClientSide;
using Controls;
using ServerSide;
using UI;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class SampleScene : MonoBehaviour
{
    [FormerlySerializedAs("_server")] [SerializeField]
    private Server server;
    public NetworkHandler networkHandler;
    public ServerSettings settings;     //서버 설정 정보
    public Text txtConnectButton;

    public ClientUI clientPrefab;       //클라이언트 ui 프리팹
    public Transform uiRoot;
    
    private readonly List<ClientControl> _clientControlList = new List<ClientControl>();

    // Start is called before the first frame update
    private void Start()
    {
        txtConnectButton.text = "Start Server";
        Application.targetFrameRate = 60;
    }

    private void Update()
    {
        float delta = Time.deltaTime;
        _clientControlList.ForEach(f=> f.UpdatePing(delta));
    }

    private void OnApplicationQuit()
    {
        foreach (var clientControl in _clientControlList)
        {
            clientControl.Disconnect();
        }
        _clientControlList.Clear();
    }

    public void OnClickRunServerButton()
    {
        if(!server.IsRunning)
        {
            server.StartServer();
            txtConnectButton.text = "Shutdown Server";
        }
        else
        {
            server.EndServer();
            txtConnectButton.text = "Start Server";
        }
    }

    /// <summary>
    /// 클라이언트 추가 버튼
    /// </summary>
    public async void OnClickAddClientButton()
    {
        if(!server.IsRunning)
        {
            UnityEditor.EditorUtility.DisplayDialog("error", "Server is not working", "Ok");
            return;
        }

        bool connectWait = true;
        NetworkPeer newPeer = new NetworkPeer
        {
            EncryptKey = settings.GetKey(),
            IsInitialConnect = true
        };
        newPeer.Connect(settings.GetDomain(), settings.port);
        newPeer.OnConnect = () =>
        {
            Debug.Log("OnConnect");
            connectWait = false;
        };

        await Task.Run(async () =>
        {
            while (connectWait)
                await Task.Delay(10);
        });

        Debug.Log("AddPeer");
        networkHandler.AddPeer(newPeer);

        var ui = GameObject.Instantiate<ClientUI>(clientPrefab, uiRoot);
        var client = new ClientControl(ui, newPeer);
        client.Login();
        _clientControlList.Add(client);
    }
}
