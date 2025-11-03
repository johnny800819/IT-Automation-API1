// 2025/1/7已從DB中刪除此VIEW

namespace API.Models
{
    public partial class LdapUserInfo
    {
        public string CommonName { get; set; }
        public string Email { get; set; }
        public string StreetAddress { get; set; }
        public string Department { get; set; }
        public string MemberOf { get; set; }
        public string UpdateTime { get; set; }
        public string IsActive { get; set; }
    }
}
