using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using API.Models.MIS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace API.Services.VMware
{
    /// <summary>
    /// 實作虛擬機本機帳號清查服務，透過背景執行 PowerShell 腳本撈取資料並儲存至資料庫。
    /// </summary>
    public class VmAuditService : IVmAuditService
    {
        private readonly IConfiguration _config;
        private readonly MISContext _misContext;
        private readonly ILogger<VmAuditService> _logger;

        public VmAuditService(
            IConfiguration config,
            MISContext misContext,
            ILogger<VmAuditService> logger)
        {
            _config = config;
            _misContext = misContext;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<AuditVmAccountHistory>> ScanAndSaveVmAccountsAsync(string environment = "Prod")
        {
            _logger.LogInformation("開始執行 {Environment} 虛擬機本機帳號清查作業...", environment);

            // 1. 取得 PowerShell 腳本路徑設定（預設使用去互動版 API 腳本）
            string scriptRelativePath = _config.GetValue<string>("VmAuditSettings:PowerShellScriptPath") ?? "批次檔/Audit_VM_LocalAccounts_API.ps1";

            // 2. 解析腳本完整路徑
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, scriptRelativePath);
            if (!File.Exists(scriptPath))
            {
                scriptPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", scriptRelativePath));
            }

            if (!File.Exists(scriptPath))
            {
                _logger.LogError("找不到虛擬機清查 PowerShell 腳本: {Path}", scriptPath);
                throw new FileNotFoundException("找不到 PowerShell 腳本", scriptPath);
            }

            // --- 追加：安全讀取並準備傳遞密碼 ---
            var vCenterUser = _config["VMwareConfig:Environments:Production:Username"] ?? "";
            var vCenterPwd  = _config["VMwareConfig:Environments:Production:Password"] ?? "";
            var guestUser   = _config["VmAuditSettings:GuestUsername"] ?? "";
            var guestPwd    = _config["VmAuditSettings:GuestPassword"] ?? "";

            // 3. 組合 PowerShell 執行參數
            // 使用 -Command 模式並在腳本前置 UTF-8 編碼設定
            // 這樣 Write-Error 輸出的中文訊息在 .NET 端就不會亂碼
            string arguments = $"-ExecutionPolicy Bypass -NoProfile -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; & '{scriptPath}'\"";

            _logger.LogInformation("準備執行 PowerShell: powershell.exe {Arguments}", arguments);

            string jsonOutput = "";
            string errorOutput = "";

            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                // 將帳號密碼注入子程序的記憶體變數，確保安全不留痕
                processStartInfo.Environment["VM_VCENTER_USER"] = vCenterUser;
                processStartInfo.Environment["VM_VCENTER_PWD"]  = vCenterPwd;
                processStartInfo.Environment["VM_GUEST_USER"]   = guestUser;
                processStartInfo.Environment["VM_GUEST_PWD"]    = guestPwd;

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null) throw new InvalidOperationException("無法啟動 powershell.exe");

                    // 非同步讀取輸出，避免死結
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    // 設定超時機制 (10分鐘)
                    if (!process.WaitForExit(600000))
                    {
                        process.Kill();
                        throw new TimeoutException("PowerShell 腳本執行逾時 (超過 10 分鐘)。");
                    }

                    jsonOutput = await outputTask;
                    errorOutput = await errorTask;

                    if (process.ExitCode != 0)
                    {
                        _logger.LogWarning("PowerShell 結束代碼不為 0 ({ExitCode})。錯誤輸出: {ErrorOutput}", process.ExitCode, errorOutput);
                    }
                    else if (!string.IsNullOrWhiteSpace(errorOutput))
                    {
                        // 從 stderr 中提取有意義的訊息行，過濾掉 PowerShell 的格式雜訊
                        // (CategoryInfo, FullyQualifiedErrorId, 位於, + 等行)
                        var cleanedLines = errorOutput
                            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(line =>
                            {
                                var trimmed = line.TrimStart();
                                // 只保留包含我們自訂標記的行
                                return trimmed.Contains("[INFO]") ||
                                       trimmed.Contains("[WARN]") ||
                                       trimmed.Contains("[ERROR]");
                            })
                            .Select(line =>
                            {
                                // 進一步清理：提取 ": [INFO/WARN/ERROR] ..." 之後的部分
                                foreach (var tag in new[] { "[INFO]", "[WARN]", "[ERROR]" })
                                {
                                    int idx = line.IndexOf(tag);
                                    if (idx >= 0) return line.Substring(idx);
                                }
                                return line.Trim();
                            })
                            .ToList();

                        if (cleanedLines.Any())
                        {
                            var summary = string.Join("\n", cleanedLines);

                            // 如果有 WARN 或 ERROR，提升 Log 等級
                            if (cleanedLines.Any(l => l.Contains("[ERROR]")))
                                _logger.LogError("PowerShell 執行過程中有錯誤:\n{Summary}", summary);
                            else if (cleanedLines.Any(l => l.Contains("[WARN]")))
                                _logger.LogWarning("PowerShell 執行過程中有警告:\n{Summary}", summary);
                            else
                                _logger.LogInformation("PowerShell 執行摘要:\n{Summary}", summary);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 PowerShell 腳本失敗。");
                throw;
            }

            // 常常 PowerShell 會夾雜警告或雜訊，我們需要擷取有效的 JSON 陣列部分
            jsonOutput = ExtractJsonArray(jsonOutput);

            if (string.IsNullOrWhiteSpace(jsonOutput))
            {
                _logger.LogWarning("PowerShell 腳本未回傳有效的 JSON 資料。");
                return Enumerable.Empty<AuditVmAccountHistory>();
            }

            var scanResults = new List<AuditVmAccountHistory>();
            var syncTime = DateTime.Now;

            // 4. 解析 JSON 並對應到資料庫模型
            try
            {
                using (var document = JsonDocument.Parse(jsonOutput))
                {
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        var history = new AuditVmAccountHistory
                        {
                            Environment = environment,
                            VirtualServerIP = element.GetProperty("VMIP").GetString() ?? "",
                            LocalAccountName = element.GetProperty("Name").GetString() ?? "",
                            Status = element.GetProperty("Enabled").GetBoolean() ? "[啟用]" : "[停用]",
                            Setting = "■保留帳號 □刪除帳號",
                            AuditDate = syncTime
                        };
                        scanResults.Add(history);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "解析 PowerShell 輸出的 JSON 資料失敗。原始輸出片段: {JsonSnippet}", 
                    jsonOutput.Length > 200 ? jsonOutput.Substring(0, 200) : jsonOutput);
                throw;
            }

            // 5. 狀態化同步 (Upsert) — 更新現況，不再無限 AddRange
            try
            {
                // 5-1. 讀取目前資料庫中，此環境的所有紀錄
                var existingRecords = _misContext.AuditVmAccountHistory
                    .Where(h => h.Environment == environment)
                    .ToList();

                // 5-2. 建立「本次已處理的 ID 集合」與「本次成功掃到的 IP 集合」
                //      [Bug Fix #3] 只對「有成功掃到過的 IP」才執行軟刪除
                //      若整台主機連線失敗 (scanResults 裡沒有它的任何資料)，保持原記錄不動
                var processedIds = new HashSet<int>();
                var scannedIPs = new HashSet<string>(
                    scanResults.Select(s => s.VirtualServerIP),
                    StringComparer.OrdinalIgnoreCase);

                // 5-3. 逐一比對 PowerShell 掃描結果
                foreach (var scanned in scanResults)
                {
                    // 以 IP + 帳號名稱 作為唯一識別鍵
                    var existing = existingRecords.FirstOrDefault(h =>
                        string.Equals(h.VirtualServerIP, scanned.VirtualServerIP, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(h.LocalAccountName, scanned.LocalAccountName, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        // [更新] 只更新可自動同步的欄位，保留其他人工填的欄位
                        existing.Status = scanned.Status;
                        existing.AuditDate = scanned.AuditDate;
                        existing.IsActive = true;
                        processedIds.Add(existing.Id);
                    }
                    else
                    {
                        // [新增] 全新帳號，預設 IsManual = false (由 PowerShell 自動建立)
                        scanned.IsActive = true;
                        scanned.IsManual = false;
                        _misContext.AuditVmAccountHistory.Add(scanned);
                    }
                }

                // 5-4. 精準軟刪除：同時滿足以下三條件才標記為 false
                //       a. 不在本次已處理清單中 (未被 Upsert)
                //       b. 不是手動維護的紀錄 (IsManual = false)
                //       c. [Bug Fix #3] 該 IP 有被本次掃描「成功存取過」，才判定為已刪除帳號
                //          若整台主機失敗，scannedIPs 不含其 IP，其帳號全部保留不動
                foreach (var rec in existingRecords)
                {
                    if (!processedIds.Contains(rec.Id) &&
                        !rec.IsManual &&
                        scannedIPs.Contains(rec.VirtualServerIP))
                    {
                        rec.IsActive = false;
                    }
                }

                await _misContext.SaveChangesAsync();

                // [Bug Fix #2] 從資料庫讀取最終有效筆數，確保 Log 數字準確
                // [Bug Fix #1] 同時回傳手動建立的紀錄 (IsManual=true) 給 Controller 產報表
                var allActiveRecords = _misContext.AuditVmAccountHistory
                    .Where(h => h.Environment == environment && h.IsActive)
                    .ToList();

                _logger.LogInformation("成功完成 {Environment} 虛擬機本機帳號清查，有效帳號: {Count} 筆 (含手動維護: {ManualCount} 筆)。",
                    environment, allActiveRecords.Count, allActiveRecords.Count(x => x.IsManual));

                return allActiveRecords;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存虛擬機清查紀錄至 MISContext 失敗。");
                throw;
            }
        }

        /// <summary>
        /// 從混雜的 PowerShell 輸出中擷取 JSON 陣列片段 `[...]`
        /// </summary>
        private string ExtractJsonArray(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            
            int startIndex = input.IndexOf('[');
            int endIndex = input.LastIndexOf(']');

            if (startIndex >= 0 && endIndex > startIndex)
            {
                return input.Substring(startIndex, endIndex - startIndex + 1);
            }
            return "";
        }
    }
}
