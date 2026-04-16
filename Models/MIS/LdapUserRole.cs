using System;
using System.Collections.Generic;

namespace API.Models.MIS
{
    public partial class LdapUserRole
    {
        public string role { get; set; }
        public string basic_dn { get; set; }
        public string memberof { get; set; }
    }
}
