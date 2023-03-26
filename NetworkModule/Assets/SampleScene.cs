using Assets.Scripts.ClientSide;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class SampleScene : MonoBehaviour
{
    public Server server;
    public NetworkHandler networkHandler;
    public ServerSettings settings;     //서버 설정 정보
    public Text serverButtonText;

    public ClientUI clientPrefab;       //클라이언트 ui 프리팹
    public Transform uiRoot;

    private List<NetworkPeer> peerList = new List<NetworkPeer>();
    private ulong instance_id = 1;

    // Start is called before the first frame update
    void Start()
    {
        serverButtonText.text = "Start Server";
    }

    public void OnClickRunServerButton()
    {
        if(!server.IsRunning)
        {
            server.StartServer();
            serverButtonText.text = "Shutdown Server";
        }
        else
        {
            server.EndServer();
            serverButtonText.text = "Start Server";
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
        NetworkPeer newPeer = new NetworkPeer();
        newPeer.EncryptKey = settings.GetKey();
        newPeer.IsInitialConnect = true;
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
        ui.SetID("#" + instance_id++);
        ui.SetPeer(newPeer);
        ui.Login();
    }
}
