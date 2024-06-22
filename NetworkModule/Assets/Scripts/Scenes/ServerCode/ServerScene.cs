using System;
using Scenes.Server;
using UnityEngine;

namespace Scenes.ServerCode
{
    public class ServerScene : MonoBehaviour
    {
        [SerializeField] private ServerView view;
        [SerializeField] private ServerSide.Server server;

        private ServerController _controller;

        private void Start()
        {
            UnityMainThreadDispatcher.Instance.Initialize();
            
            _controller = new ServerController(view, server);
            _controller.Initialize();
        }

        private void Update()
        {
            _controller?.UpdateServer();
        }
    }
}