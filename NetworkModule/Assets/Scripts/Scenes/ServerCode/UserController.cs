using System;
using ServerSide;

namespace Scenes
{
    /// <summary>
    /// 유저 컨트롤러
    /// 유저를 처리하는 객체
    /// </summary>
    public class UserController
    {
        private long _instanceID;
        private SocketObject _socketObject;
        private string _userId;
        private int _roomId = -1;  //-1인 경우 룸 없는 상태

        public bool IsConnected => _socketObject?.IsConnected ?? false;
        public bool InEnterRoom => _roomId != -1;
        
        public long Id
        {
            get => _instanceID;
            set => _instanceID = value;
        }

        public string UserId
        {
            get => _userId;
            set => _userId = value;
        }

        public SocketObject SocketObj => _socketObject;

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
            _socketObject = null;
            _userId = null;
            _instanceID = 0;
            _roomId = -1;
        }

        /// <summary>
        /// 룸 입장
        /// </summary>
        /// <param name="roomId"></param>
        public void EnterRoom(int roomId)
        {
            _roomId = roomId;
                    
            EnterRoomSend send = new EnterRoomSend()
            {
                roomNumber = roomId,
            };
            _socketObject.Send((ushort)SendId.EnterRoom, send.ToJson());
        }
    }
}