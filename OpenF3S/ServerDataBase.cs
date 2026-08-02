using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Fortress3PaewangServerTest
{
    internal class ServerDataBase
    {
        public IPAddress IpAddress { get; set; }
        public ushort Port { get; set; }
        public int Id { get; set; }
        public int Index { get; }
        public int Type { get; set; }
        public int CurrentUserCount { get => currentUserCount; }
        public int MaxUserCount { get; set; }
        public  string LordGuild { get; set; }
        public string LordName { get; set; }
        public bool Enabled { get; set; }

        private int currentUserCount;

        private List<ClientSession> account = new List<ClientSession>();

        public ServerDataBase(int index)
        {
            Index = index;
            IpAddress = IPAddress.Parse("127.0.0.1");
            Port = 0;
            currentUserCount = 0;
            LordGuild = string.Empty;
            LordName = string.Empty;
            Enabled = false;
        }
    }
}
