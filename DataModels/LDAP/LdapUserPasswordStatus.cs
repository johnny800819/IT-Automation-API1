#pragma warning disable 1591 // 禁用 CS1591 (缺少 XML 文件註釋)

namespace API.DataModels.LDAP
{
    /// <summary>
    /// 存放一個 LDAP 使用者的密碼狀態相關資訊。
    /// </summary>
    public class LdapUserPasswordStatus
    {
        /// <summary>
        /// 包含該 LDAP 使用者的基本資訊。
        /// </summary>
        public LdapUser User { get; set; }

        /// <summary>
        /// 獲取或設定一個值，該值指示使用者的密碼是否設定為永不過期。
        /// </summary>
        public bool IsPasswordNeverExpires { get; set; }

        /// <summary>
        /// 獲取或設定密碼最後一次設定的本地時間。
        /// </summary>
        public DateTime? PasswordLastSet { get; set; }

        /// <summary>
        /// 獲取或設定根據有效期計算出的密碼預計到期本地時間。
        /// </summary>
        public DateTime? PasswordExpiresOn { get; set; }

        /// <summary>
        /// 獲取或設定距離密碼到期的剩餘天數（負數代表已過期）。
        /// </summary>
        public int? DaysUntilExpiration { get; set; }

        /// <summary>
        /// 獲取或設定計算此狀態時所使用的密碼最大有效期（天）。
        /// </summary>
        public int? PasswordMaxAgeDays { get; set; }
    }
}