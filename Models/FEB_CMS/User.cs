using System;
using System.Collections.Generic;

namespace API.Models.FEB_CMS
{
    public partial class User
    {
        public long Id { get; set; }
        public string Account { get; set; }
        public string Name { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public long UpdateUserId { get; set; }
        public byte Status { get; set; }
        public long CreateUserId { get; set; }
        public DateTime? LoginTime { get; set; }
        public string EmployeeId { get; set; }
        public string Department { get; set; }
        public string JobTitle { get; set; }
        public string Email { get; set; }
        public string JurisdictionJson { get; set; }
        public DateTime? CheckReportSyncDate { get; set; }
        public byte Role { get; set; }
        public long? Group { get; set; }
        public string UserPassword { get; set; }
        public DateTime? ValidateDate { get; set; }
    }
}
