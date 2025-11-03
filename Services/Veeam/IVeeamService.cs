using API.Models;

namespace API.Services.Veeam
{
    /// <summary>
    /// 定義與 Veeam 備份資料相關的業務邏輯服務契約。
    /// </summary>
    public interface IVeeamService
    {
        /// <summary>
        /// 非同步地獲取所有 Veeam 備份工作階段的資料，並按預設順序排序。
        /// </summary>
        /// <remarks>
        /// 預設排序為：先按更新時間 (`UpdateTime`) 降序，再按工作階段名稱 (`VmsSessionName`) 升序。
        /// </remarks>
        /// <returns>一個包含所有 VeeamBackupSessions 資料的列表。</returns>
        Task<List<VeeamBackupSessions>> GetBackupSessionsAsync();
    }
}