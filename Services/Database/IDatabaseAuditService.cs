using System.Collections.Generic;
using System.Threading.Tasks;
using API.Models.MIS;

namespace API.Services.Database
{
    /// <summary>
    /// 定義資料庫帳號清查服務介面
    /// </summary>
    public interface IDatabaseAuditService
    {
        /// <summary>
        /// 執行資料庫帳號清查，並將結果保存至歷史紀錄中。
        /// </summary>
        /// <param name="connectionStringName">要使用的連線字串名稱 (從配置檔案中取得)。</param>
        /// <param name="targetServerName">目標伺服器顯示名稱 (例如: 10.13.30.225)。</param>
        /// <returns>包含稽核結果的列表。</returns>
        Task<IEnumerable<AuditDbAccountHistory>> ScanAndSaveDbAccountsAsync(string connectionStringName, string targetServerName);
    }
}
