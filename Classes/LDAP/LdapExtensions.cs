using Novell.Directory.Ldap;
using System;

namespace API.Classes.LDAP
{
    /// <summary>
    /// 提供 LdapEntry 擴充方法的靜態類別。
    /// </summary>
    public static class LdapEntryExtensions
    {
        /// <summary>
        /// 安全地從 LdapEntry 取得屬性值。
        /// </summary>
        /// <remarks>
        /// 如果屬性不存在或發生錯誤，則回傳空字串 ""。
        /// </remarks>
        /// <param name="entry">LdapEntry 物件本身</param>
        /// <param name="attributeName">要取得的屬性名稱</param>
        /// <returns>屬性的字串值或空字串</returns>
        public static string GetSafeAttribute(this LdapEntry entry, string attributeName)
        {
            // this 關鍵字告訴編譯器：「這不是一個普通的方法，這是一個擴充方法。」
            // LdapEntry 指明了我們要「擴充」的對象是 LdapEntry 這個類別。
            // entry 則是這個物件在方法內部的名稱。
            if (entry == null || string.IsNullOrEmpty(attributeName))
            {
                return "";
            }

            try
            {
                return entry.GetAttribute(attributeName)?.StringValue ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 檢查密碼是否設定為永不過期。
        /// </summary>
        /// <param name="entry">LdapEntry 物件</param>
        /// <returns>如果密碼永不過期則回傳 true，否則回傳 false。</returns>
        public static bool IsPasswordNeverExpires(this LdapEntry entry)
        {
            string uacStr = entry.GetSafeAttribute("userAccountControl");
            if (int.TryParse(uacStr, out int uac))
            {
                // 0x10000 (65536) 是 DONT_EXPIRE_PASSWORD 的旗標
                const int DONT_EXPIRE_FLAG = 0x10000;
                return (uac & DONT_EXPIRE_FLAG) == DONT_EXPIRE_FLAG;
            }
            return false;
        }

        /// <summary>
        /// 檢查使用者是否必須在下次登入時變更密碼。
        /// </summary>
        /// <remarks>
        /// 這是基於 Active Directory 的一個特殊規則：當 pwdLastSet 屬性值為 0 時，代表強制使用者在下次登入時變更密碼。
        /// </remarks>
        /// <param name="entry">LdapEntry 物件</param>
        /// <returns>如果需要強制變更密碼則回傳 true，否則回傳 false。</returns>
        public static bool MustChangePassword(this LdapEntry entry)
        {
            string pwdLastSetStr = entry.GetSafeAttribute("pwdLastSet");
            if (long.TryParse(pwdLastSetStr, out long pwdLastSet))
            {
                return pwdLastSet == 0;
            }
            return false;
        }
    }
}