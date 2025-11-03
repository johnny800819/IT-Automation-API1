namespace API.DataModels
{
    /// <summary>
    /// LdapUser 因為 FebCmsUserService 也會用到它來進行比對，
    /// 所以它是一個被多個服務共用的模型，應該留在 DataModels 根目錄下。
    /// </summary>
    public class LdapUser
    {
        /// <summary>
        /// 用戶的識別名 (Distinguished Name)
        /// </summary>
        public string DistinguishedName { get; set; }

        /// <summary>
        /// 用戶的通用名稱 (Common Name)
        /// </summary>
        public string CommonName { get; set; }

        /// <summary>
        /// 用戶的姓 (Surname)
        /// </summary>
        public string Surname { get; set; }

        /// <summary>
        /// 用戶的名 (Given Name)
        /// </summary>
        public string GivenName { get; set; }

        /// <summary>
        /// 用戶主名稱 (User Principal Name)
        /// </summary>
        public string UserPrincipalName { get; set; }

        /// <summary>
        /// 安全帳戶名稱 (SAM Account Name)
        /// </summary>
        public string SamAccountName { get; set; }

        /// <summary>
        /// 顯示名稱 (Display Name)
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 電子郵件地址 (Email)
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// 電話號碼 (Telephone Number)
        /// </summary>
        public string TelephoneNumber { get; set; }

        /// <summary>
        /// 職稱 (Title)
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 部門 (Department)
        /// </summary>
        public string Department { get; set; }

        /// <summary>
        /// 公司名稱 (Company)
        /// </summary>
        public string Company { get; set; }

        /// <summary>
        /// 描述 (Description)
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 街道地址 (Street Address)
        /// </summary>
        public string StreetAddress { get; set; }

        /// <summary>
        /// 郵政編碼 (Postal Code)
        /// </summary>
        public string PostalCode { get; set; }

        /// <summary>
        /// 城市 (City, Locality Name)
        /// </summary>
        public string City { get; set; }

        /// <summary>
        /// 國家 (Country, Country Name)
        /// </summary>
        public string Country { get; set; }

        /// <summary>
        /// 帳戶過期時間 (Account Expires)
        /// </summary>
        public DateTime? AccountExpires { get; set; }

        /// <summary>
        /// 帳戶是否啟用 (Enabled)
        /// </summary>
        public bool? Enabled { get; set; }

        /// <summary>
        /// 用戶所屬的群組 (Member Of)
        /// </summary>
        public List<string> MemberOf { get; set; }

        /// <summary>
        /// 獲取或設定帳戶的啟用狀態 ("Active" 或 "Inactive")。
        /// </summary>
        /// <remarks>
        /// 此值通常根據 userAccountControl 屬性的 ACCOUNTDISABLE 旗標計算得出。
        /// </remarks>
        public string IsActive { get; set; }
    }
}
