using System;
using Assets.Scripts.ClientSide;
using ClientSide;
using UnityEngine;

namespace Scenes.ClientScene
{
    public class ClientScene : MonoBehaviour
    {
        [SerializeField] private NetworkHandler networkHandler;
        [SerializeField] private ClientView clientView;
        [SerializeField] private ServerSettings settings;
    
        private ClientControl _clientControl;
    
        // Start is called before the first frame update
        private void Start()
        {
            var networkPeer = new NetworkPeer()
            {
                EncryptKey = settings.GetKey(),
                IsInitialConnect = true
            };
            networkHandler.SetNetworkPeer(networkPeer);
            _clientControl = new ClientControl(clientView, networkPeer);
            _clientControl.SetDomain(settings.serverDomain, settings.port);
        }

        private void Update()
        {
            _clientControl?.UpdatePing();
        }

        private void OnApplicationQuit()
        {
            _clientControl.Disconnect();
        }
    }
}
