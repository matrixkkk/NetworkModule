using System;
using Assets.Scripts.ServerSide;

namespace Scenes
{
    /// <summary>
    /// 유저 컨트롤러
    /// 유저를 처리하는 객체
    /// </summary>
    public class UserController
    {
        private long instanceID;
        private SocketObject _socketObject;
        
        public Action<SocketObject> ReleaseCallback { get; set; } 
        public long Id
        {
            get => instanceID;
            set => instanceID = value;
        }
        
        /// <summary>
        /// 소켓 오브젝트 설정
        /// </summary>
        /// <param name="obj"></param>
        public void SetAsyncObject(SocketObject obj)
        {
            _socketObject = obj;
        }

        public void Clear()
        {
            ReleaseCallback?.Invoke(_socketObject);
        }
    }
}