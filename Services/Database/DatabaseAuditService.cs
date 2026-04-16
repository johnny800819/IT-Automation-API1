using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using API.Models.MIS;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace API.Services.Database
{
    /// <summary>
    /// 實作資料庫帳號清查服務，透過 ADO.NET 撈取資料並使用 EF Core 將紀錄保存至歷史表。
    /// </summary>
    public class DatabaseAuditService : IDatabaseAuditService
    {
        private readonly IConfiguration _config;
        private readonly MISContext _misContext;
        private readonly ILogger<DatabaseAuditService> _logger;

        public DatabaseAuditService(
            IConfiguration config,
            MISContext misContext,
            ILogger<DatabaseAuditService> logger)
        {
            _config = config;
            _misContext = misContext;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<AuditDbAccountHistory>> ScanAndSaveDbAccountsAsync(string connectionStringName, string targetServerName)
        {
            // 0. 根據目標 IP 自動判定環境標籤
            string environment = "Unknown";
            if (targetServerName.StartsWith("10.13.30")) 
            {
                environment = "Prod";
            }
            else if (targetServerName.Equals("10.13.20.206"))
            {
                environment = "Test";
            }

            _logger.LogInformation("開始執行 {TargetServerName} ({Environment}) 資料庫帳號清查作業...", targetServerName, environment);

            // 1. 取得目標連線字串
            string masterConnectionString = _config.GetConnectionString(connectionStringName);

            if (string.IsNullOrEmpty(masterConnectionString))
            {
                _logger.LogError("找不到指定的連線字串 {ConnectionStringName}，請確認 secrets.dev.json 設定。", connectionStringName);
                throw new InvalidOperationException($"找不到連線字串: {connectionStringName}");
            }

            // 2. 讀取 SQL 腳本
            string batchScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "批次檔", "資料庫帳號(Server Level)清查.sql");
            
            if (!File.Exists(batchScriptPath))
            {
                batchScriptPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "批次檔", "資料庫帳號(Server Level)清查.sql"));
            }

            if (!File.Exists(batchScriptPath))
            {
                _logger.LogError("找不到資料庫清查 SQL 腳本: {Path}", batchScriptPath);
                throw new FileNotFoundException("找不到資料庫清查 SQL 腳本", batchScriptPath);
            }

            string sqlScript = await File.ReadAllTextAsync(batchScriptPath);
            var scanResults = new List<AuditDbAccountHistory>();
            var syncTime = DateTime.Now;

            // 3. 取得目前該主機已經存在於資料庫的所有帳號紀錄
            // 排序策略：優先取出有填寫 Assignee 的，日期則取最新，確保我們比對到的是最有價值的紀錄
            var existingRecords = _misContext.AuditDbAccountHistory
                .Where(h => h.Environment == environment && h.TargetServer == targetServerName)
                .OrderByDescending(h => h.Assignee.Length) 
                .ThenByDescending(h => h.AuditDate)
                .ToList();

            _logger.LogInformation("自資料庫獲取 {Count} 筆現況紀錄。", existingRecords.Count);

            // 用來追蹤本次掃描中，我們已經處理過哪些帳號（防止一台主機有多筆重複 Record 的問題）
            var processedAccountIds = new HashSet<int>();
            var scannedAccountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 4. 執行 SQL 查詢
            try
            {
                using (var connection = new SqlConnection(masterConnectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(sqlScript, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string accountName = reader["帳號名稱"]?.ToString() ?? "";
                                string currentStatus = reader["帳號狀態"]?.ToString() ?? "";
                                string currentRole = reader["伺服器角色"]?.ToString() ?? "";
                                string currentDesc = reader["帳號使用說明"]?.ToString() ?? "";

                                scannedAccountNames.Add(accountName);

                                // 比對是否為已知帳號：找該帳號名稱下「第一筆」（因為我們前面已經按權重排序過了，這會是最優紀錄）
                                var bestExistingRecord = existingRecords
                                    .FirstOrDefault(x => string.Equals(x.AccountName, accountName, StringComparison.OrdinalIgnoreCase) 
                                                         && !processedAccountIds.Contains(x.Id));

                                if (bestExistingRecord != null)
                                {
                                    // [更新邏輯 (現有帳號)]
                                    // 1. 系統自動更新部分
                                    bestExistingRecord.Status = currentStatus;
                                    bestExistingRecord.ServerRole = currentRole;
                                    bestExistingRecord.AuditDate = syncTime;
                                    bestExistingRecord.IsActive = true; 
                                    
                                    // 2. 人工維護欄位保護
                                    // Assignee: 絕對不蓋掉 (除非目前是空的才考慮)
                                    // AccountDescription: 若資料庫已有人工填過主觀說明，不被 SQL 掃到的客觀值蓋掉
                                    if (string.IsNullOrWhiteSpace(bestExistingRecord.AccountDescription) && !string.IsNullOrWhiteSpace(currentDesc))
                                    {
                                        bestExistingRecord.AccountDescription = currentDesc;
                                    }

                                    processedAccountIds.Add(bestExistingRecord.Id);
                                    scanResults.Add(bestExistingRecord);
                                }
                                else
                                {
                                    // [新增邏輯 (全新帳號)]
                                    var newAccount = new AuditDbAccountHistory
                                    {
                                        Environment = environment,
                                        TargetServer = targetServerName,
                                        AccountName = accountName,
                                        Status = currentStatus,
                                        ServerRole = currentRole,
                                        AccountDescription = currentDesc,
                                        Assignee = "",
                                        ActionDecision = "",
                                        AuditDate = syncTime,
                                        IsActive = true
                                    };
                                    _misContext.AuditDbAccountHistory.Add(newAccount);
                                    scanResults.Add(newAccount);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 SQL 清查失敗。伺服器: {TargetServerName}", targetServerName);
                throw;
            }

            // 5. 處理軟刪除與重複清理
            // A. 本次沒掃到的帳號 -> IsActive = false
            // B. 雖然掃到了，但他是該帳號多餘的歷史重複 Row -> 一律 IsActive = false，保持資產庫唯一性
            foreach (var rec in existingRecords)
            {
                if (!processedAccountIds.Contains(rec.Id))
                {
                    rec.IsActive = false;
                }
            }

            // 6. 保存變更
            await _misContext.SaveChangesAsync();
            _logger.LogInformation("成功完成 {TargetServerName} 清查。現存有效帳號: {Count}", targetServerName, scanResults.Count);

            return scanResults;
        }
    }
}
