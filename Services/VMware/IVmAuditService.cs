using System.Collections.Generic;
using System.Threading.Tasks;
using API.Models.MIS;

namespace API.Services.VMware
{
    /// <summary>
    /// 定義虛擬機本機帳號清查服務介面
    /// </summary>
    public interface IVmAuditService
    {
        /// <summary>
        /// 執行虛擬機本機帳號清查，解析 JSON 結果，並將歷史紀錄保存至資料庫
        /// </summary>
        /// <param name="environment">指定的環境名稱 (例如 Prod, Test)</param>
        /// <returns>清查結果的清單</returns>
        Task<IEnumerable<AuditVmAccountHistory>> ScanAndSaveVmAccountsAsync(string environment = "Prod");
    }
}
