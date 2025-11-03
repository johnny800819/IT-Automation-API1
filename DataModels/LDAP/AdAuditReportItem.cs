namespace API.DataModels.LDAP
{
    /// <summary>
    /// 資料傳輸物件 (DTO)，用於代表 AD 帳號清查報表中的單一行資料。
    /// 這個物件由 LdapService 產生，包含了從 AD 查詢並經過業務邏輯判斷後的核心資料，
    /// 最終會被 Controller 用來生成 Excel 報表的內容。
    /// </summary>
    public class AdAuditReportItem
    {
        /// <summary>
        /// 使用者登入帳號 (sAMAccountName)。
        /// </summary>
        public string SamAccountName { get; set; } = string.Empty;

        /// <summary>
        /// 標記此帳號是否為特權帳號。
        /// 這個值是根據使用者是否隸屬於 AdAuditConfig 中定義的 PrivilegedGroups 來判斷的。
        /// </summary>
        public bool IsPrivileged { get; set; }

        /// <summary>
        /// 使用者的顯示名稱 (DisplayName)。
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 帳號的啟用狀態。
        /// true 代表帳號為啟用(Enabled)，false 代表帳號為停用(Disabled)。
        /// </summary>
        public bool IsEnabled { get; set; }
    }
}