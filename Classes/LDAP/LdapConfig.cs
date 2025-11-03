namespace API.Classes.LDAP
{
    /// <summary>
    /// 對應 appsettings.json 中的 LdapConfig 區塊。
    /// </summary>
    public class LdapConfig
    {
        public string Host { get; set; }
        public string BaseDC { get; set; }
        public string AdminBaseDC { get; set; }
        public string AdminPassword { get; set; }
    }
}