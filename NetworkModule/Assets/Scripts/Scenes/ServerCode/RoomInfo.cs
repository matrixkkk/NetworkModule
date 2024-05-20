using System;
using System.Collections.Generic;

namespace Scenes.ServerCode
{
    public class RoomInfo
    {
        private const int MAX_USER_COUNT = 100;
        
        private readonly List<UserController> _userList = new List<UserController>();
        
        public bool IsMaxUser => _userList.Count >= MAX_USER_COUNT;
        
        public void AddUser(UserController user)
        {
            _userList.Add(user);
        }

        public void LeaveUser(long userId)
        {
            _userList.RemoveAll(x => x.Id == userId);
        }
    }
}