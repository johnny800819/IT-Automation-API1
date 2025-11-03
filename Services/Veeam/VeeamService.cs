using API.Models;
using Microsoft.EntityFrameworkCore;

namespace API.Services.Veeam
{
    /// <summary>
    /// 實現與 Veeam 備份資料相關的業務邏輯。
    /// </summary>
    public class VeeamService : IVeeamService
    {
        private readonly MISContext _misContext;
        private readonly ILogger<VeeamService> _logger;

        /// <summary>
        /// 初始化 VeeamService 的新執行個體。
        /// </summary>
        public VeeamService(MISContext misContext, ILogger<VeeamService> logger)
        {
            _misContext = misContext;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<List<VeeamBackupSessions>> GetBackupSessionsAsync()
        {
            _logger.LogInformation("開始從資料庫獲取 Veeam 備份工作階段資料...");
            try
            {
                // 從 _context 讀取資料，並進行排序 (改為非同步的 ToListAsync() 以提升效能)
                var sessions = await _misContext.VeeamBackupSessions
                    .OrderByDescending(s => s.UpdateTime)
                    .ThenBy(s => s.VmsSessionName)
                    .ToListAsync();

                _logger.LogInformation("成功獲取了 {Count} 筆 Veeam 備份工作階段資料。", sessions.Count);
                return sessions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "從資料庫獲取 Veeam 備份工作階段資料時發生未預期的錯誤。");
                // 重新拋出例外，讓 Controller 層捕捉並回傳 500 錯誤
                throw;
            }
        }
    }
}