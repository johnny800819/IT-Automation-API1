using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using API.Services.Database;
using API.Services.VMware;
using API.Services.AppAudit;
using API.Classes.Reporting;
using API.Services.LDAP;
using API.Models.MIS;
using API.Models.AppAudit;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountAuditController : ControllerBase
    {
        private readonly ILogger<AccountAuditController> _logger;
        private readonly IDatabaseAuditService _dbAuditService;
        private readonly IVmAuditService _vmAuditService;
        private readonly ILdapService _ldapService;
        private readonly IExcelService _excelService;
        private readonly IConfiguration _configuration;
        private readonly MISContext _misContext;
        private readonly IAppAuditService _appAuditService;
        private readonly AppAuditSettings _appAuditSettings;

        public AccountAuditController(
            ILogger<AccountAuditController> logger,
            IDatabaseAuditService dbAuditService,
            IVmAuditService vmAuditService,
            ILdapService ldapService,
            IExcelService excelService,
            IConfiguration configuration,
            MISContext misContext,
            IAppAuditService appAuditService,
            IOptions<AppAuditSettings> appAuditSettings)
        {
            _logger = logger;
            _dbAuditService = dbAuditService;
            _vmAuditService = vmAuditService;
            _ldapService = ldapService;
            _excelService = excelService;
            _configuration = configuration;
            _misContext = misContext;
            _appAuditService = appAuditService;
            _appAuditSettings = appAuditSettings.Value;
        }

        /// <summary>
        /// 匯出資料庫帳號清查報表 (Server Level)。
        /// </summary>
        /// <remarks>
        /// 此 API 會自動掃描指定的正式機 (10.13.30.225, 10.13.30.226) 與測試機 (10.13.20.206) 資料庫主機，
        /// 比對歷史紀錄自動帶入承辦人與說明，並產出包含三個獨立頁籤的整合性 Excel 報表。
        /// </remarks>
        /// <returns>回傳包含多頁籤資料庫清查結果的 Excel 檔案 (.xlsx)。</returns>
        [HttpGet("Database/Export")]
        public async Task<IActionResult> ExportDatabaseAuditReport()
        {
            try
            {
                _logger.LogInformation("開始執行整合性資料庫帳號清查任務...");

                // 定義清查目標
                var targets = new[]
                {
                    new { Name = "正式機(225)-SERVER LEVEL", Conn = "AuditDbConnection_Prod_225", IP = "10.13.30.225" },
                    new { Name = "正式機(226)-SERVER LEVEL", Conn = "AuditDbConnection_Prod_226", IP = "10.13.30.226" },
                    new { Name = "測試機-SERVER LEVEL",      Conn = "AuditDbConnection_Test_206", IP = "10.13.20.206" }
                };

                IDictionary<string, IEnumerable<AuditDbAccountHistory>> reportMap = new Dictionary<string, IEnumerable<AuditDbAccountHistory>>();

                // 執行各台伺服器掃描
                foreach (var target in targets)
                {
                    var data = await _dbAuditService.ScanAndSaveDbAccountsAsync(target.Conn, target.IP);
                    
                    // 1. 過濾掉已刪除 (IsActive=false) 的資料
                    // 2. 依照使用者要求排序：
                    //    a. 帳號狀態 (倒序，讓 [啟用] 在上方)
                    //    b. 承辦人 (正序，讓同個人在一起)
                    //    c. 帳號名稱 (正序)
                    var sortedData = data.Where(x => x.IsActive)
                        .OrderByDescending(x => x.Status ?? "")
                        .ThenBy(x => x.Assignee ?? "")
                        .ThenBy(x => x.AccountName ?? "")
                        .ToList();

                    reportMap.Add(target.Name, sortedData);
                }

                // 2. 轉換為單一 Excel 檔案 (多頁籤)
                var excelBytes = _excelService.CreateDbAuditReport(reportMap);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string fileName = $"Database_ServerLevel_Audit_Report_{timestamp}.xlsx";

                return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "匯出資料庫帳號清查報表時發生錯誤");
                return StatusCode(500, "匯出報表時發生內部伺服器錯誤: " + ex.Message);
            }
        }

        /// <summary>
        /// 匯出虛擬機 (VM) 本機帳號清查報表。
        /// </summary>
        /// <remarks>
        /// 透過 PowerShell 呼叫 VMware PowerCLI 獲取所有 VM 的本機帳號狀態，並產出 Excel 報表。
        /// 報表包含：虛擬伺服器 IP、本機使用者帳戶、狀態、設定。
        /// </remarks>
        /// <returns>回傳虛擬機本機帳號清查結果的 Excel 檔案 (.xlsx)。</returns>
        [HttpGet("VM/Export")]
        public async Task<IActionResult> ExportVmAuditReport()
        {
            try
            {
                // 1. 產生清查資料並存入歷史紀錄 (透過 PowerShell 呼叫 VMware PowerCLI)
                // VM 稽核目前預設為單一環境 "Prod"
                var rawData = await _vmAuditService.ScanAndSaveVmAccountsAsync("Prod");

                // 2. 過濾非有效紀錄，並與雲端手動紀錄 (IsManual=true) 整合排序
                //    排序邏輯：手動主機優先 → 狀態(啟用在上) → IP → 帳號名稱
                var reportData = rawData
                    .Where(x => x.IsActive)
                    .OrderByDescending(x => x.Status ?? "")
                    .ThenBy(x => x.VirtualServerIP ?? "")
                    .ThenBy(x => x.LocalAccountName ?? "")
                    .ToList();

                // 3. 轉換為 Excel 檔案
                var excelBytes = _excelService.CreateVmAuditReport(reportData);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string fileName = $"VM_Local_Accounts_Audit_{timestamp}.xlsx";

                return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "匯出虛擬機本機帳號清查報表時發生錯誤");
                return StatusCode(500, "匯出報表時發生內部伺服器錯誤");
            }
        }

        /// <summary>
        /// 匯出 Active Directory (AD) 帳號清查報表。
        /// </summary>
        /// <remarks>
        /// 查詢 Active Directory 中的使用者資料，排除特定 OU 與群組後，識別特權帳號並產出稽核報表。
        /// 報表包含：帳號、特權帳號標記、名稱、目前狀態、建議處置。
        /// </remarks>
        /// <returns>回傳 AD 帳號清查結果的 Excel 檔案 (.xlsx)。</returns>
        [HttpGet("AD/Export")]
        public async Task<IActionResult> ExportAdAuditReport()
        {
            try
            {
                // 1. 產生清查資料 (已有的 LDAP 邏輯)
                var reportData = await _ldapService.GenerateAuditReportDataAsync();

                // 2. 轉換為 Excel 檔案
                var excelBytes = _excelService.CreateAdAuditReport(reportData);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string fileName = $"AD_Accounts_Audit_{timestamp}.xlsx";

                return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "匯出 AD 帳號清查報表時發生錯誤");
                return StatusCode(500, "匯出報表時發生內部伺服器錯誤");
            }
        }

        // ============================================================
        // TODO: 以下端點為「稽核決策管理」功能骨架，目前暫不對外公開。
        //       待後續實作「管理 UI」後，提供介面讓稽核人員操作。
        //       設計規劃詳見 AuditDbAccountHistory.ActionDecision 欄位說明。
        // ============================================================

        /// <summary>
        /// 【待實作 UI 後正式啟用】更新 DB 帳號稽核記錄的決策欄位。
        /// 更新對象：ActionDecision（稽核決策）、Assignee（承辦人）。
        /// 此端點不影響自動掃描邏輯，為純人工維護管道。
        /// </summary>
        /// <param name="id">AuditDbAccountHistory 的主鍵 Id</param>
        /// <param name="request">包含要更新的 ActionDecision 與 Assignee 值</param>
        [HttpPatch("db/{id}/decision")]
        public async Task<IActionResult> UpdateDbAccountDecision(int id, [FromBody] UpdateDecisionRequest request)
        {
            try
            {
                var record = await _misContext.AuditDbAccountHistory.FindAsync(id);
                if (record == null)
                {
                    return NotFound($"找不到 Id = {id} 的稽核記錄。");
                }

                // 只更新人工維護欄位，不動自動掃描的欄位
                if (request.ActionDecision != null)
                    record.ActionDecision = request.ActionDecision.Trim();

                if (request.Assignee != null)
                    record.Assignee = request.Assignee.Trim();

                await _misContext.SaveChangesAsync();

                _logger.LogInformation("已更新 DB 帳號稽核記錄 Id={Id}: ActionDecision={Decision}, Assignee={Assignee}",
                    id, record.ActionDecision, record.Assignee);

                return Ok(new { id, record.ActionDecision, record.Assignee });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新 DB 帳號稽核決策失敗, Id={Id}", id);
                return StatusCode(500, "更新稽核決策時發生內部伺服器錯誤。");
            }
        }

        /// <summary>
        /// 匯出應用系統帳號清查報表。
        /// </summary>
        /// <remarks>
        /// 根據 systemKey 對應 appsettings.json 中的 AppAuditSettings 設定，
        /// 自動掃描指定的應用系統資料庫，撈取有效帳號後產出 Excel 報表。
        /// 目前支援的 systemKey: FEB_CMS
        /// </remarks>
        /// <param name="systemKey">應用系統識別碼，例如 "FEB_CMS"</param>
        /// <returns>回傳包含多頁籤的應用系統帳號清查 Excel 檔案 (.xlsx)。</returns>
        [HttpGet("App/{systemKey}/Export")]
        public async Task<IActionResult> ExportAppAuditReport(string systemKey)
        {
            try
            {
                _logger.LogInformation("開始執行應用系統帳號清查匯出，SystemKey='{SystemKey}'", systemKey);

                // 1. 呼叫 Service 掃描並儲存歷史紀錄
                var rawDataMap = await _appAuditService.ScanAndSaveAppAccountsAsync(systemKey);

                // 2. 篩選 + 排序: 過濾無效資料，依「組室 → 帳號」排序
                var reportMap = new Dictionary<string, IEnumerable<AuditAppAccountHistory>>();
                foreach (var entry in rawDataMap)
                {
                    var sorted = entry.Value
                        .Where(x => x.IsActive)
                        .OrderBy(x => x.Department ?? "")
                        .ThenBy(x => x.AccountName ?? "")
                        .ToList();
                    reportMap.Add(entry.Key, sorted);
                }

                // 3. 從設定取得 SystemTitle 作為報表標題，找不到則 fallback 為 systemKey
                string systemTitle = _appAuditSettings.Systems
                    .FirstOrDefault(s => string.Equals(s.SystemKey, systemKey, StringComparison.OrdinalIgnoreCase))
                    ?.SystemTitle ?? systemKey;

                // 4. 產生 Excel 報表
                var excelBytes = _excelService.CreateAppAuditReport(systemTitle, reportMap);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string fileName = $"App_{systemKey}_Audit_Report_{timestamp}.xlsx";

                return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("找不到系統設定"))
            {
                // systemKey 不存在於設定檔時，回傳 400 並告知可用清單
                _logger.LogWarning("無效的 SystemKey='{SystemKey}'", systemKey);
                return BadRequest($"找不到應用系統 '{systemKey}' 的設定。請確認 systemKey 是否正確，目前支援的系統請參考 appsettings.json 的 AppAuditSettings。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "匯出應用系統帳號清查報表時發生錯誤，SystemKey='{SystemKey}'", systemKey);
                return StatusCode(500, "匯出報表時發生內部伺服器錯誤: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// PATCH /api/AccountAudit/db/{id}/decision 的請求 DTO。
    /// 所有欄位皆為選填，僅更新有提供值的欄位。
    /// </summary>
    public class UpdateDecisionRequest
    {
        /// <summary>
        /// 稽核決策，例如 "保留"、"刪除"、"停用"。傳入 null 表示不更新此欄位。
        /// </summary>
        public string ActionDecision { get; set; }

        /// <summary>
        /// 承辦人姓名，例如 "王小明"。傳入 null 表示不更新此欄位。
        /// </summary>
        public string Assignee { get; set; }
    }
}
