using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using API.Models.AppAudit;
using API.Models.MIS;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace API.Services.AppAudit
{
    /// <summary>
    /// 應用系統帳號清查服務實作。
    /// 透過 appsettings.json 中的 AppAuditSettings 組態，自動識別目標資料庫與 SQL 查詢，
    /// 並將結果以 Upsert 邏輯同步至 AuditAppAccountHistory。
    /// </summary>
    public class AppAuditService : IAppAuditService
    {
        private readonly IConfiguration _config;
        private readonly AppAuditSettings _settings;
        private readonly MISContext _misContext;
        private readonly ILogger<AppAuditService> _logger;

        public AppAuditService(
            IConfiguration config,
            IOptions<AppAuditSettings> settings,
            MISContext misContext,
            ILogger<AppAuditService> logger)
        {
            _config = config;
            _settings = settings.Value;
            _misContext = misContext;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<IDictionary<string, IEnumerable<AuditAppAccountHistory>>> ScanAndSaveAppAccountsAsync(string systemKey)
        {
            // 1. 從設定中找到對應的系統設定
            var systemConfig = _settings.Systems
                .FirstOrDefault(s => string.Equals(s.SystemKey, systemKey, StringComparison.OrdinalIgnoreCase));

            if (systemConfig == null)
            {
                _logger.LogError("找不到 SystemKey = '{SystemKey}' 的設定，請確認 appsettings.json 的 AppAuditSettings 區塊。", systemKey);
                throw new InvalidOperationException($"找不到系統設定: {systemKey}");
            }

            _logger.LogInformation("開始執行應用系統帳號清查 SystemKey='{SystemKey}'，共 {Count} 個目標。",
                systemKey, systemConfig.Targets.Count);

            // 取得欄位對應（設定可覆寫，若未設定則使用預設名稱）
            string colDept    = systemConfig.ColumnMapping.GetValueOrDefault("Department", "Department");
            string colAccount = systemConfig.ColumnMapping.GetValueOrDefault("AccountName", "Account");
            string colName    = systemConfig.ColumnMapping.GetValueOrDefault("RealName", "Name");

            var syncTime = DateTime.Now;
            var resultMap = new Dictionary<string, IEnumerable<AuditAppAccountHistory>>();

            // 2. 逐一掃描每個目標主機
            foreach (var target in systemConfig.Targets)
            {
                _logger.LogInformation("開始清查目標: {TargetName} ({IP}) 環境={Env}",
                    target.Name, target.IP, target.Environment);

                // 2a. 取得連線字串
                string connStr = _config.GetConnectionString(target.ConnectionStringName);
                if (string.IsNullOrEmpty(connStr))
                {
                    _logger.LogError("找不到連線字串 '{ConnName}'，跳過此目標。", target.ConnectionStringName);
                    continue;
                }

                // 2b. 取得此目標在資料庫中的現有紀錄（用於 Upsert 比對）
                var existingRecords = _misContext.AuditAppAccountHistory
                    .Where(h => h.SystemKey == systemConfig.SystemKey
                             && h.Environment == target.Environment
                             && h.TargetIP == target.IP)
                    .ToList();

                _logger.LogInformation("自資料庫取得 {Count} 筆現有紀錄。", existingRecords.Count);

                var scannedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var processedIds    = new HashSet<int>();
                var targetResults   = new List<AuditAppAccountHistory>();

                // 2c. 執行 SQL 查詢
                try
                {
                    using var connection = new SqlConnection(connStr);
                    await connection.OpenAsync();
                    using var command = new SqlCommand(systemConfig.SqlQuery, connection);
                    using var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        // 讀取各欄位（欄位名稱由設定的 ColumnMapping 決定）
                        string department = reader[colDept]?.ToString()?.Trim() ?? "";
                        string account    = reader[colAccount]?.ToString()?.Trim() ?? "";
                        string realName   = reader[colName]?.ToString()?.Trim() ?? "";

                        if (string.IsNullOrEmpty(account)) continue;
                        scannedAccounts.Add(account);

                        // 比對現有紀錄（Upsert 邏輯）
                        var existing = existingRecords
                            .FirstOrDefault(r => string.Equals(r.AccountName, account, StringComparison.OrdinalIgnoreCase)
                                              && !processedIds.Contains(r.Id));

                        if (existing != null)
                        {
                            // [更新] 系統欄位可覆寫，人工維護欄位 (ActionDecision) 保護不覆蓋
                            existing.Department = department;
                            existing.RealName   = realName;
                            existing.AuditDate  = syncTime;
                            existing.IsActive   = true;
                            processedIds.Add(existing.Id);
                            targetResults.Add(existing);
                        }
                        else
                        {
                            // [新增] 全新帳號
                            var newRecord = new AuditAppAccountHistory
                            {
                                SystemKey      = systemConfig.SystemKey,
                                Environment    = target.Environment,
                                TargetIP       = target.IP,
                                Department     = department,
                                AccountName    = account,
                                RealName       = realName,
                                ActionDecision = "",
                                AuditDate      = syncTime,
                                IsActive       = true
                            };
                            _misContext.AuditAppAccountHistory.Add(newRecord);
                            targetResults.Add(newRecord);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "清查 {TargetName} ({IP}) 時發生錯誤。", target.Name, target.IP);
                    throw;
                }

                // 2d. 軟刪除：本次沒掃到的帳號標記為 IsActive = false
                foreach (var rec in existingRecords.Where(r => !processedIds.Contains(r.Id)))
                {
                    rec.IsActive = false;
                }

                _logger.LogInformation("完成 {TargetName} 清查，有效帳號: {Count} 筆。", target.Name, targetResults.Count);
                resultMap.Add(target.Name, targetResults);
            }

            // 3. 統一儲存所有變更至資料庫
            await _misContext.SaveChangesAsync();
            _logger.LogInformation("應用系統 '{SystemKey}' 清查完成，已儲存至資料庫。", systemKey);

            return resultMap;
        }
    }
}
