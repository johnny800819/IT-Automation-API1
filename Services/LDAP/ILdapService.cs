using API.DataModels;
using API.DataModels.LDAP;

namespace API.Services.LDAP
{
    /// <summary>
    /// 定義 LDAP 相關業務邏輯的服務契約
    /// </summary>
    public interface ILdapService
    {
        /// <summary>
        /// 檢查指定帳號清單的密碼狀態，並對即將到期的帳號觸發郵件通知。
        /// </summary>
        /// <remarks>
        /// 此方法會讀取 appsettings.json 中的設定來決定要檢查哪些帳號、密碼有效期、以及郵件對應關係。
        /// 所有的操作日誌將會被記錄下來。
        /// </remarks>
        /// <returns>一個包含每個被檢查使用者密碼狀態的詳細結果列表。</returns>
        Task<List<LdapUserPasswordStatus>> CheckPasswordStatusAndNotifyAsync();

        /// <summary>
        /// 將 Active Directory 中的所有使用者資料同步至 LdapUserHistory 資料庫表中。
        /// </summary>
        /// <remarks>
        /// 此作業包含新增全新使用者、為資料異動的使用者新增歷史紀錄，以及標記已不存在於 AD 中的使用者為離職狀態。
        /// </remarks>
        /// <returns>一個包含同步作業詳細結果的 AdUserSyncToDbResult 物件。</returns>
        Task<AdUserSyncToDbResult> SyncUsersToDatabaseAsync();

        /// <summary>
        /// 驗證使用者的帳號與密碼是否正確。
        /// </summary>
        /// <param name="username">要驗證的使用者帳號 (sAMAccountName)。</param>
        /// <param name="password">使用者提供的密碼。</param>
        /// <returns>如果驗證成功則回傳 true，否則回傳 false。</returns>
        Task<bool> AuthenticateLdapUserAsync(string username, string password);

        /// <summary>
        /// 使用完整的識別名 (DN) 直接進行 LDAP 綁定驗證。
        /// </summary>
        /// <param name="distinguishedName">要驗證的使用者 DN。如果為 null 或空，則使用設定檔中的預設服務帳號(fsceipsys)。</param>
        /// <param name="password">使用者提供的密碼。如果為 null 或空，則使用設定檔中的預設密碼。</param>
        /// <returns>一個包含操作結果的 SimpleApiResponse 物件。</returns>
        Task<SimpleApiResponse> AuthenticateDirectLdapUserAsync(string distinguishedName, string password);

        /// <summary>
        /// 根據使用者帳號 (sAMAccountName) 或通用名稱 (cn)，查詢單一使用者的密碼狀態。
        /// </summary>
        /// <param name="accountOrCommonName">要查詢的使用者帳號或姓名。</param>
        /// <returns>
        /// 如果找到使用者，則回傳包含其密碼狀態的 <see cref="LdapUserPasswordStatus"/> 物件。
        /// 如果找不到使用者，則回傳 null。
        /// </returns>
        Task<LdapUserPasswordStatus> GetUserPasswordStatusAsync(string accountOrCommonName);

        /// <summary>
        /// 根據一組 sAMAccountName，批次獲取多個使用者的詳細資訊。
        /// </summary>
        /// <param name="accountNames">要查詢的使用者帳號 (sAMAccountName) 集合。</param>
        /// <returns>
        /// 一個字典，其中 Key 是 sAMAccountName (不區分大小寫)，Value 是對應的 LdapUser 物件。
        /// 對於在 AD 中找不到的帳號，將不會包含在回傳的字典中。
        /// </returns>
        Task<Dictionary<string, LdapUser>> GetUsersInfoAsync(IEnumerable<string> accountNames);

        /// <summary>
        /// 獲取 Active Directory 中所有使用者的詳細資訊及其密碼狀態。
        /// </summary>
        /// <returns>一個包含所有使用者資訊及密碼狀態的列表 (<see cref="LdapUserPasswordStatus"/>)。</returns>
        Task<List<LdapUserPasswordStatus>> GetAllUsersWithPasswordStatusAsync();

        /// <summary>
        /// 在 Active Directory 中建立一個新的使用者帳號。
        /// </summary>
        /// <param name="newUserModel">包含新使用者所有必要資訊的 LdapUser 物件。</param>
        /// <returns>一個包含建立作業詳細結果的 AdUserCreateResult 物件。</returns>
        Task<AdUserCreateResult> CreateLdapUserAsync(LdapUser newUserModel);

        /// <summary>
        /// 更新 Active Directory 中指定使用者的資訊。
        /// </summary>
        /// <param name="username">要更新的使用者帳號 (sAMAccountName)。</param>
        /// <param name="updateData">包含要更新欄位及其新值的 AdUserUpdateModel 物件。模型中為 null 的屬性將不會被更新。</param>
        /// <returns>一個包含操作結果的 SimpleApiResponse 物件。</returns>
        Task<SimpleApiResponse> UpdateLdapUserAsync(string username, AdUserUpdateModel updateData);

        /// <summary>
        /// 產生 AD 帳號清查報表所需的資料。
        /// 此方法會執行完整的業務邏輯：查詢 AD 使用者、根據設定檔進行篩選、
        /// 判斷特權帳號，並將結果轉換為 AdAuditReportItem 物件列表。
        /// </summary>
        /// <returns>一份包含報表所需核心資料的 AdAuditReportItem 列表。</returns>
        Task<IEnumerable<AdAuditReportItem>> GenerateAuditReportDataAsync();
    }
}