namespace API.Models
{
    public partial class LdapUserHistory
    {
        public int sn { get; set; }
        public string IsActive { get; set; }
        public string CommonName { get; set; }
        public string Email { get; set; }
        public string StreetAddress { get; set; }
        public string Department { get; set; }
        public string MemberOf { get; set; }
        public string UpdateTime { get; set; }
    }
}
