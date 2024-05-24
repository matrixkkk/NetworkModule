using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Scenes.ServerCode;

namespace Scenes
{
    /// <summary>
    /// 매치 관리자
    /// </summary>
    public class MatchMaker
    {
        private readonly object _lock = new object();
        private readonly List<RoomInfo> _roomList = new List<RoomInfo>();   //룸 목록
        private readonly Dictionary<long, RoomInfo> _userRoomDic = new Dictionary<long, RoomInfo>(); //유저 룸 매핑
        private int _roomNumberId;

        public Action<string> OnError { get; set; }
        public Action<string> OnMessage { get; set; }
        
        /// <summary>
        /// 유저 입장
        /// </summary>
        /// <param name="user"></param>
        public void EnterUser(UserController user)
        {
            Task.Run(() =>
            {
                lock (_lock)
                {
                    foreach (var room in _roomList)
                    {
                        if (room.IsMaxUser) continue;
                    
                        room.AddUser(user);
                        _userRoomDic.TryAdd(user.Id, room);
                        user.EnterRoom(room.RoomNumber);
                        OnMessage?.Invoke($"Enter room : {room.RoomNumber.ToString()}");
                        return;
                    }
                 
                    var newRoom = new RoomInfo
                    {
                        RoomNumber = ++_roomNumberId
                    };
                    newRoom.AddUser(user);
                    _roomList.Add(newRoom);
                    _userRoomDic.TryAdd(user.Id, newRoom);
                    
                    OnMessage?.Invoke($"Create Room : {newRoom.RoomNumber.ToString()}");
                    user.EnterRoom(newRoom.RoomNumber);
                }
            });
        }
        
        public void ExitUser(UserController user)
        {
            Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_userRoomDic.TryGetValue(user.Id, out var room))
                    {
                        room.LeaveUser(user.Id);
                        _userRoomDic.Remove(user.Id);
                    }
                    else
                    {
                        OnError?.Invoke("Not Found Room");
                    }
                }
            });
        }
    }
}