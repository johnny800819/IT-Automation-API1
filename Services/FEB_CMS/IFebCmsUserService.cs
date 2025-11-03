using API.DataModels.FEB_CMS;

namespace API.Services.FEB_CMS
{
    /// <summary>
    /// 定義與 FebCms 使用者管理相關的業務邏輯服務契約。
    /// </summary>
    public interface IFebCmsUserService
    {
        /// <summary>
        /// 檢查 FebCms 使用者是否存在於 Active Directory 中，並同步其狀態與職稱。
        /// </summary>
        /// <returns>一個包含同步作業詳細結果的 FebCmsUserSyncResult 物件。</returns>
        Task<FebCmsUserSyncResult> SyncUsersFromAdAsync();
    }
}