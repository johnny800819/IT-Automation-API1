using System.Collections.Generic;
using System.Threading.Tasks;
using API.Models.MIS;

namespace API.Services.AppAudit
{
    /// <summary>
    /// 定義應用系統帳號清查服務介面。
    /// 採用組態驅動設計，透過 SystemKey 識別要清查的系統，
    /// 支援未來擴充不同的應用系統而不需修改程式碼。
    /// </summary>
    public interface IAppAuditService
    {
        /// <summary>
        /// 根據指定的系統代碼，掃描所有已設定的目標資料庫，
        /// 並將結果以 Upsert 方式同步至 AuditAppAccountHistory 資料表。
        /// </summary>
        /// <param name="systemKey">應用系統識別碼，例如 "FEB_CMS"。需對應 appsettings.json 的設定。</param>
        /// <returns>包含所有目標機器掃描結果的字典，Key 為頁籤名稱，Value 為清查資料。</returns>
        Task<IDictionary<string, IEnumerable<AuditAppAccountHistory>>> ScanAndSaveAppAccountsAsync(string systemKey);
    }
}
