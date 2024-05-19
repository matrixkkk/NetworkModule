using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scenes.Server
{
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static readonly Queue<Action> _executionQueue = new Queue<Action>();

        public void Initialize()
        {
        }

        void Update()
        {
            lock (_executionQueue)
            {
                while (_executionQueue.Count > 0)
                {
                    _executionQueue.Dequeue().Invoke();
                }
            }
        }

        public void Enqueue(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (_executionQueue)
            {
                _executionQueue.Enqueue(action);
            }
        }

        private static UnityMainThreadDispatcher _instance;

        public static UnityMainThreadDispatcher Instance
        {
            get
            {
                if (!_instance)
                {
                    _instance = FindObjectOfType<UnityMainThreadDispatcher>();

                    if (!_instance)
                    {
                        var obj = new GameObject("UnityMainThreadDispatcher");
                        _instance = obj.AddComponent<UnityMainThreadDispatcher>();
                    }
                }

                return _instance;
            }
        }
    }
}