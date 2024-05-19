using ServerSide;
using UnityEngine;

namespace Scenes
{
    public class ServerScene : MonoBehaviour
    {
        [SerializeField] private ServerView view;
        [SerializeField] private Server server;

        private ServerController _controller;
        
        private void Start()
        {
            _controller = new ServerController(view, server);
            _controller.Initialize();
        }
    }
}