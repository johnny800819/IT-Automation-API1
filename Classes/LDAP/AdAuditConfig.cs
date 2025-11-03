namespace API.Classes.LDAP
{
    /// <summary>
    /// 代表 'appsettings.json' 中 'AdAuditSettings' 區塊的強型別設定模型。
    /// 這個類別用於將 AD 帳號清查功能的相關設定透過依賴注入(DI)提供給服務層，
    /// 使得設定值可以被安全且方便地讀取，並提高程式碼的可維護性。
    /// </summary>
    public class AdAuditConfig
    {
        /// <summary>
        /// AD 搜尋的根路徑 (Search Base)。
        /// 指定 LDAP 查詢應從哪個 OU (Organizational Unit) 或容器開始遞迴搜尋。
        /// 對應 appsettings.json 中的 'AdAuditSettings:SearchBase'。
        /// </summary>
        public string SearchBase { get; set; } = string.Empty;

        /// <summary>
        /// 在產生報表時需要被排除的 OU (Organizational Unit) 清單。
        /// 程式邏輯會檢查使用者的 DistinguishedName 是否包含此清單中的任何一個值。
        /// 對應 appsettings.json 中的 'AdAuditSettings:ExcludedOUs'。
        /// </summary>
        public List<string> ExcludedOUs { get; set; } = new();

        /// <summary>
        /// 在產生報表時需要被排除的 AD 群組清單。
        /// 清單中的字串應為群組的完整識別名稱 (DistinguishedName)。
        /// 程式邏輯會檢查使用者是否為此清單中任何一個群組的成員。
        /// 對應 appsettings.json 中的 'AdAuditSettings:ExcludedGroups'。
        /// </summary>
        public List<string> ExcludedGroups { get; set; } = new();

        /// <summary>
        /// 用於判斷使用者是否為「特權帳號」的 AD 群組清單。
        /// 清單中的字串應為特權群組的完整識別名稱 (DistinguishedName)，例如 Domain Admins。
        /// 程式邏輯會檢查使用者是否為此清單中任何一個群組的成員。
        /// 對應 appsettings.json 中的 'AdAuditSettings:PrivilegedGroups'。
        /// </summary>
        public List<string> PrivilegedGroups { get; set; } = new();

        /// <summary>
        /// 直接按 sAMAccountName 指定的特權帳號列表。
        /// 對應 appsettings.json 中的 'AdAuditSettings:PrivilegedAccounts'。
        /// </summary>
        public List<string> PrivilegedAccounts { get; set; } = new();
    }
}