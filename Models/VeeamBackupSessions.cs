using System;
using System.Collections.Generic;

namespace API.Models
{
    public partial class VeeamBackupSessions
    {
        public string VmsSessionName { get; set; }
        public string Status { get; set; }
        public string SessionEndTime { get; set; }
        public string VeeamServer { get; set; }
        public string UpdateTime { get; set; }
    }
}
