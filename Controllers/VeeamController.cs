using API.Services.Veeam;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VeeamController : ControllerBase
    {
        private readonly IVeeamService _veeamService;
        private readonly ILogger<VeeamController> _logger;

        /// <summary>
        /// 初始化 VeeamController 的新執行個體。
        /// </summary>
        public VeeamController(IVeeamService veeamService, ILogger<VeeamController> logger)
        {
            _veeamService = veeamService;
            _logger = logger;
        }

        /// <summary>
        /// 取得每日Veeam Backup Jobs Sessions, 資料來源為資料庫。
        /// </summary>
        /// <returns>一個包含所有 Veeam 備份工作階段的 JSON 陣列。</returns>
        [HttpGet("GetVeeamBackupSessions")]
        public async Task<IActionResult> GetVeeamBackupSessions()
        {
            try
            {
                var sessions = await _veeamService.GetBackupSessionsAsync();
                return Ok(sessions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 GetVeeamBackupSessions 時發生未預期的錯誤。");
                return StatusCode(500, "獲取 Veeam 備份工作階段時發生內部伺服器錯誤。");
            }
        }

        /// <summary>
        /// (HTML版)取得每日Veeam Backup Jobs Sessions, 資料來源為資料庫。
        /// 包含現代化視覺儀表板、KPI統計指標、今昨兩天健康檢查判定、護眼淡色例外標示、Excel 友善複製與即時多維度篩選。
        /// </summary>
        /// <returns>一個包含所有 Veeam 備份工作階段的 HTML 表格報告。</returns>
        [HttpGet("GetVeeamBackupSessionsHTML")]
        [Produces("text/html")]
        public async Task<IActionResult> GetVeeamBackupSessionsHTML()
        {
            try
            {
                // 1. 從 Service 層獲取純資料列表
                var sessions = await _veeamService.GetBackupSessionsAsync();

                // 2. Controller 負責 HTML 格式化與視覺報告呈現
                string htmlContent = GenerateVeeamReportHtml(sessions);
                var utf8Encoding = new UTF8Encoding(false);

                // 3. 回傳 ContentResult
                return Content(htmlContent, "text/html", utf8Encoding);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 GetVeeamBackupSessionsHTML 時發生未預期的錯誤。");
                return StatusCode(500, "生成 Veeam 備份報告時發生內部伺服器錯誤。");
            }
        }

        /// <summary>
        /// 產生現代化 Veeam 備份視覺化監控儀表板 HTML。
        /// </summary>
        private static string GenerateVeeamReportHtml(List<API.Models.MIS.VeeamBackupSessions> sessions)
        {
            // 基準日期定義：今天、昨天、7天前
            DateTime today = DateTime.Today;
            DateTime yesterday = today.AddDays(-1);
            DateTime sevenDaysAgo = today.AddDays(-7);

            // 找出每一台 VM 的「最新一筆」紀錄（依 SessionEndTime DESC, UpdateTime DESC）
            var latestSessionSet = new HashSet<string>();
            var groupedByVm = sessions
                .Where(s => !string.IsNullOrEmpty(s.VmsSessionName))
                .GroupBy(s => s.VmsSessionName)
                .Select(g => g.OrderByDescending(s => s.SessionEndTime).ThenByDescending(s => s.UpdateTime).First())
                .ToList();

            foreach (var item in groupedByVm)
            {
                // 以 VM名稱 + 結束時間 + VeeamServer 作為唯一識別
                latestSessionSet.Add($"{item.VmsSessionName}|{item.SessionEndTime}|{item.VeeamServer}");
            }

            // 統計 KPI 指標（以最新一期 VM 為基準）
            int totalUniqueVms = groupedByVm.Count;
            int normalCount = 0;       // 今天或昨天成功
            int delayedCount = 0;      // 2~7天內延遲
            int warningCount = 0;      // 警告
            int failedCount = 0;       // 失敗
            int archivedCount = 0;     // 超過 7 天 (封存/除役)

            foreach (var item in groupedByVm)
            {
                string rawStatus = item.Status?.Trim() ?? "";
                string endTime = item.SessionEndTime?.Trim() ?? "";
                
                DateTime parsedEndTime;
                bool hasValidDate = DateTime.TryParse(endTime.Length >= 10 ? endTime.Substring(0, 10) : endTime, out parsedEndTime);
                
                bool isSuccess = (rawStatus == "0" || rawStatus.Equals("Success", StringComparison.OrdinalIgnoreCase));
                bool isWarning = (rawStatus == "1" || rawStatus.Equals("Warning", StringComparison.OrdinalIgnoreCase));
                bool isFailed = (rawStatus == "2" || rawStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase) || rawStatus.Equals("Error", StringComparison.OrdinalIgnoreCase));

                if (!hasValidDate)
                {
                    archivedCount++;
                    continue;
                }

                if (parsedEndTime < sevenDaysAgo)
                {
                    archivedCount++;
                }
                else if (parsedEndTime >= yesterday && isSuccess)
                {
                    normalCount++;
                }
                else if (isFailed)
                {
                    failedCount++;
                }
                else if (isWarning)
                {
                    warningCount++;
                }
                else
                {
                    // 在 7 天內，但不是今昨兩天，且沒有明顯失敗，視為延遲
                    delayedCount++;
                }
            }

            int exceptionCount = delayedCount + warningCount + failedCount;
            // 活躍主機總數 (扣除封存)
            int activeVms = totalUniqueVms - archivedCount;
            double normalRate = activeVms > 0 ? Math.Round((double)normalCount / activeVms * 100, 1) : 0;

            // 伺服器清單與各伺服器統計
            var serverStats = sessions
                .Where(s => !string.IsNullOrEmpty(s.VeeamServer))
                .GroupBy(s => s.VeeamServer)
                .Select(g => new { Server = g.Key, Count = g.Count(), LatestUpdate = g.Max(s => s.UpdateTime) })
                .OrderBy(g => g.Server)
                .ToList();

            var sb = new StringBuilder();
            sb.Append(@"<!DOCTYPE html>
<html lang='zh-TW'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Veeam 備份狀態儀表板</title>
    <style>
        :root {
            --bg-body: #f1f5f9;
            --bg-card: #ffffff;
            --text-main: #1e293b;
            --text-muted: #64748b;
            --border: #e2e8f0;
            --shadow: 0 4px 6px -1px rgba(0,0,0,0.1), 0 2px 4px -1px rgba(0,0,0,0.06);

            /* 徽章顏色定義 - 參考 Google 高對比度護眼色 */
            --badge-normal-bg: #e6f4ea;
            --badge-normal-text: #137333;
            --badge-normal-border: #ceead6;

            --badge-outdated-bg: #fef7e0;
            --badge-outdated-text: #b06000;
            --badge-outdated-border: #fadc96;

            --badge-warning-bg: #feefe3;
            --badge-warning-text: #c04a00;
            --badge-warning-border: #fbd6b1;

            --badge-failed-bg: #fce8e6;
            --badge-failed-text: #c5221f;
            --badge-failed-border: #fad2cf;

            --badge-archived-bg: #f1f3f4;
            --badge-archived-text: #5f6368;
            --badge-archived-border: #e8eaed;
            
            --badge-latest-bg: #e8f0fe;
            --badge-latest-text: #1967d2;
            --badge-latest-border: #d2e3fc;
        }

        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: var(--bg-body);
            color: var(--text-main);
            padding: 20px;
            line-height: 1.5;
        }

        .container { max-width: 1600px; margin: 0 auto; }

        /* KPI 指標卡片區塊 */
        .stats-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
            gap: 24px;
            margin-bottom: 24px;
        }
        .stat-card {
            background: var(--bg-card);
            border-radius: 12px;
            padding: 24px;
            border: 1px solid var(--border);
            box-shadow: 0 4px 6px -1px rgba(0,0,0,0.05), 0 2px 4px -1px rgba(0,0,0,0.03);
            border-left: 6px solid var(--border);
            transition: transform 0.2s, box-shadow 0.2s;
        }
        .stat-card:hover {
            transform: translateY(-3px);
            box-shadow: 0 10px 15px -3px rgba(0,0,0,0.08), 0 4px 6px -2px rgba(0,0,0,0.04);
        }
        .card-blue { border-left-color: #3b82f6; }
        .card-green { border-left-color: #10b981; }
        .card-orange { border-left-color: #f59e0b; }
        .card-gray { border-left-color: #94a3b8; }
        
        .stat-label {
            font-size: 0.95rem;
            font-weight: 600;
            color: #475569;
            margin-bottom: 12px;
        }
        .stat-value {
            font-size: 2.4rem;
            font-weight: 700;
            margin: 4px 0 8px 0;
            line-height: 1;
            display: flex;
            align-items: baseline;
            gap: 8px;
        }
        .text-blue { color: #1d4ed8; }
        .text-green { color: #047857; }
        .text-orange { color: #b45309; }
        .text-gray { color: #475569; }
        .stat-rate { font-size: 1.1rem; color: #64748b; font-weight: 500; }
        
        .stat-sub {
            font-size: 0.85rem;
            color: #64748b;
        }

        /* 伺服器狀態條 */
        .server-grid {
            display: flex;
            flex-wrap: wrap;
            gap: 10px;
            margin-bottom: 20px;
        }
        .server-chip {
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 6px;
            padding: 8px 12px;
            font-size: 0.85rem;
        }

        /* 控制與篩選工具列 */
        .toolbar {
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 20px;
            margin-bottom: 24px;
            box-shadow: var(--shadow);
            display: flex;
            flex-direction: column;
            gap: 16px;
        }
        .toolbar-top {
            display: flex;
            flex-wrap: wrap;
            gap: 12px;
            justify-content: space-between;
        }
        .search-input {
            width: 320px;
            padding: 10px 14px;
            border-radius: 8px;
            border: 1px solid #cbd5e1;
            font-size: 0.95rem;
            transition: border-color 0.2s, box-shadow 0.2s;
        }
        .search-input:focus {
            outline: none;
            border-color: #3b82f6;
            box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
        }
        .btn {
            border: 1px solid #cbd5e1;
            padding: 9px 16px;
            border-radius: 8px;
            font-size: 0.9rem;
            font-weight: 500;
            cursor: pointer;
            background: #ffffff;
            color: var(--text-main);
            transition: all 0.2s;
        }
        .btn:hover { background: #f8fafc; border-color: #94a3b8; }
        
        /* 標籤篩選組 */
        .filter-row {
            display: flex;
            flex-wrap: wrap;
            align-items: center;
            gap: 16px;
            padding-top: 16px;
            border-top: 1px dashed var(--border);
        }
        .filter-group {
            display: flex;
            align-items: center;
            gap: 8px;
            flex-wrap: wrap;
        }
        .filter-group-label {
            font-size: 0.85rem;
            font-weight: 600;
            color: #475569;
        }
        .pill-btn {
            background: #ffffff;
            border: 1px solid #cbd5e1;
            color: #475569;
            padding: 6px 14px;
            border-radius: 20px;
            font-size: 0.85rem;
            font-weight: 500;
            cursor: pointer;
            transition: all 0.2s;
        }
        .pill-btn:hover { background: #f8fafc; border-color: #94a3b8; }
        .pill-btn.active {
            background: #eff6ff;
            color: #1d4ed8;
            border-color: #bfdbfe;
            font-weight: 600;
        }

        /* 資料表格 */
        .table-card {
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 8px;
            box-shadow: var(--shadow);
            overflow: hidden;
        }
        .table-header-info {
            padding: 16px;
            background: #ffffff;
            border-bottom: 1px solid var(--border);
            font-size: 0.95rem;
            font-weight: 600;
            color: var(--text-main);
        }
        .table-container {
            width: 100%;
            overflow-x: auto;
        }
        table {
            width: 100%;
            border-collapse: collapse;
            text-align: left;
            font-size: 0.9rem;
        }
        thead th {
            background: #1e293b;
            color: #f8fafc;
            padding: 14px 16px;
            font-weight: 500;
            cursor: pointer;
            border-bottom: 2px solid #0f172a;
        }
        tbody tr {
            border-bottom: 1px solid var(--border);
            background-color: #ffffff;
            transition: background-color 0.2s;
        }
        tbody tr:nth-child(even) {
            background-color: #f8fafc;
        }
        tbody tr:hover {
            background-color: #f1f5f9;
        }
        tbody td {
            padding: 14px 16px;
            vertical-align: middle;
            color: var(--text-main);
            font-weight: 500;
        }

        /* 徽章 Badge 樣式 (圓角、清晰文字) */
        .badge {
            display: inline-block;
            padding: 4px 12px;
            border-radius: 12px;
            font-size: 0.85rem;
            font-weight: 600;
            border: 1px solid transparent;
        }
        .badge-normal   { background: var(--badge-normal-bg); color: var(--badge-normal-text); border-color: var(--badge-normal-border); }
        .badge-outdated { background: var(--badge-outdated-bg); color: var(--badge-outdated-text); border-color: var(--badge-outdated-border); }
        .badge-warning  { background: var(--badge-warning-bg); color: var(--badge-warning-text); border-color: var(--badge-warning-border); }
        .badge-failed   { background: var(--badge-failed-bg); color: var(--badge-failed-text); border-color: var(--badge-failed-border); }
        .badge-archived { background: var(--badge-archived-bg); color: var(--badge-archived-text); border-color: var(--badge-archived-border); }
        .badge-history  { background: #ffffff; color: var(--text-muted); border-color: var(--border); }
        .badge-latest   { background: var(--badge-latest-bg); color: var(--badge-latest-text); border-color: var(--badge-latest-border); }

        .time-text { color: #475569; }
        .empty-state { padding: 30px; text-align: center; color: var(--text-muted); }
    </style>
</head>
<body>
    <div class='container'>
        <!-- KPI 統計指標卡片 -->
        <section class='stats-grid'>
            <div class='stat-card card-blue'>
                <div class='stat-label'>監控虛擬機數 (VMs)</div>
                <div class='stat-value text-blue'>").Append(totalUniqueVms).Append(@"</div>
                <div class='stat-sub'>總數 (活躍 ").Append(activeVms).Append(@" + 封存 ").Append(archivedCount).Append(@")</div>
            </div>
            <div class='stat-card card-green'>
                <div class='stat-label'>活躍正常 (今/昨成功)</div>
                <div class='stat-value text-green'>").Append(normalCount).Append(@" <span class='stat-rate'>(").Append(normalRate).Append(@"%)</span></div>
                <div class='stat-sub'>於今日或昨日備份成功</div>
            </div>
            <div class='stat-card card-orange'>
                <div class='stat-label'>活躍例外 (近7天)</div>
                <div class='stat-value text-orange'>").Append(exceptionCount).Append(@"</div>
                <div class='stat-sub'>延遲 ").Append(delayedCount).Append(@" / 警告 ").Append(warningCount).Append(@" / 失敗 ").Append(failedCount).Append(@"</div>
            </div>
            <div class='stat-card card-gray'>
                <div class='stat-label'>歷史封存 (>7天)</div>
                <div class='stat-value text-gray'>").Append(archivedCount).Append(@"</div>
                <div class='stat-sub'>超過一週未備份 (可能已刪除/除役)</div>
            </div>
        </section>

        <!-- 控制與篩選工具列 -->
        <section class='toolbar'>
            <div class='toolbar-top'>
                <input type='text' id='searchInput' class='search-input' placeholder='搜尋虛擬機或 IP...'>
                <div style='display:flex; gap: 8px;'>
                    <button type='button' id='btnCopyExcel' class='btn' title='複製表格純文字，貼至 Excel'>複製至 Excel</button>
                    <button type='button' id='btnExportCsv' class='btn'>匯出 CSV</button>
                </div>
            </div>

            <div class='filter-row'>
                <div class='filter-group' id='periodFilters'>
                    <span class='filter-group-label'>顯示範圍：</span>
                    <button class='pill-btn' data-period='ALL'>全部歷史</button>
                    <button class='pill-btn' data-period='LATEST_ALL'>最新一期(全部VM)</button>
                    <button class='pill-btn active' data-period='LATEST_7DAYS'>最新一期(近7天活躍)</button>
                    <button class='pill-btn' data-period='LATEST_ARCHIVED'>最新一期(>7天封存)</button>
                </div>

                <div class='filter-group' id='serverFilters'>
                    <span class='filter-group-label'>伺服器：</span>
                    <button class='pill-btn active' data-server='ALL'>全部</button>");
            
            foreach (var s in serverStats)
            {
                sb.Append("<button class='pill-btn' data-server='").Append(s.Server).Append("'>").Append(s.Server).Append("</button>");
            }

            sb.Append(@"
                </div>
            </div>
        </section>

        <!-- 表格卡片 -->
        <section class='table-card'>
            <div class='table-header-info'>
                目前顯示：<strong id='visibleCount'>0</strong> 筆
            </div>
            <div class='table-container'>
                <table id='sessionsTable'>
                    <thead>
                        <tr>
                            <th onclick='sortTable(0)'>虛擬機名稱 (VM Name)</th>
                            <th onclick='sortTable(1)'>健診判定 (Condition)</th>
                            <th onclick='sortTable(2)'>備份狀態 (Status)</th>
                            <th onclick='sortTable(3)'>備份完成日 (End Time)</th>
                            <th onclick='sortTable(4)'>期別 (Run Type)</th>
                            <th onclick='sortTable(5)'>Veeam 伺服器</th>
                            <th onclick='sortTable(6)'>更新時間 (Update Time)</th>
                        </tr>
                    </thead>
                    <tbody id='tableBody'>");

            foreach (var session in sessions)
            {
                string key = $"{session.VmsSessionName}|{session.SessionEndTime}|{session.VeeamServer}";
                bool isLatest = latestSessionSet.Contains(key);

                string rawStatus = session.Status?.Trim() ?? "";
                string endTime = session.SessionEndTime?.Trim() ?? "";
                
                DateTime parsedEndTime;
                bool hasValidDate = DateTime.TryParse(endTime.Length >= 10 ? endTime.Substring(0, 10) : endTime, out parsedEndTime);

                bool isSuccess = (rawStatus == "0" || rawStatus.Equals("Success", StringComparison.OrdinalIgnoreCase));
                bool isWarning = (rawStatus == "1" || rawStatus.Equals("Warning", StringComparison.OrdinalIgnoreCase));
                bool isFailed = (rawStatus == "2" || rawStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase) || rawStatus.Equals("Error", StringComparison.OrdinalIgnoreCase));

                string statusText = isSuccess ? "成功" : (isWarning ? "警告" : (isFailed ? "失敗" : (string.IsNullOrEmpty(rawStatus) ? "未知" : rawStatus)));

                // 健診判定
                string condText = "歷史紀錄";
                string condBadgeClass = "badge-history";

                // 分類：7天內為活躍(active)，大於7天為封存(archived)
                string timeGroup = "history";

                if (isLatest)
                {
                    if (!hasValidDate || parsedEndTime < sevenDaysAgo)
                    {
                        timeGroup = "archived";
                        condText = "封存/除役 (>7天)";
                        condBadgeClass = "badge-archived";
                    }
                    else
                    {
                        timeGroup = "active_7days";
                        
                        if (isSuccess && parsedEndTime >= yesterday)
                        {
                            condText = "正常 (今昨)";
                            condBadgeClass = "badge-normal";
                        }
                        else if (isFailed)
                        {
                            condText = "例外 (失敗)";
                            condBadgeClass = "badge-failed";
                        }
                        else if (isWarning)
                        {
                            condText = "例外 (警告)";
                            condBadgeClass = "badge-warning";
                        }
                        else
                        {
                            condText = "延遲 (2-7天)";
                            condBadgeClass = "badge-outdated";
                        }
                    }
                }

                sb.Append("<tr data-is-latest='").Append(isLatest ? "true" : "false")
                  .Append("' data-time-group='").Append(timeGroup)
                  .Append("' data-server='").Append(session.VeeamServer ?? "")
                  .Append("'>");

                sb.Append("<td>").Append(session.VmsSessionName).Append("</td>");
                sb.Append("<td><span class='badge ").Append(condBadgeClass).Append("'>").Append(condText).Append("</span></td>");
                
                string statusBadgeClass = isSuccess ? "badge-normal" : (isWarning ? "badge-warning" : (isFailed ? "badge-failed" : "badge-history"));
                sb.Append("<td><span class='badge ").Append(statusBadgeClass).Append("'>").Append(statusText).Append("</span></td>");
                
                sb.Append("<td><span class='time-text'>").Append(session.SessionEndTime).Append("</span></td>");
                
                sb.Append("<td><span class='badge ").Append(isLatest ? "badge-latest" : "badge-history").Append("'>")
                  .Append(isLatest ? "最新一期" : "歷史").Append("</span></td>");
                
                sb.Append("<td>").Append(session.VeeamServer).Append("</td>");
                sb.Append("<td><span class='time-text'>").Append(session.UpdateTime).Append("</span></td>");

                sb.Append("</tr>");
            }

            sb.Append(@"
                    </tbody>
                </table>
                <div id='emptyMessage' class='empty-state' style='display: none;'>查無資料。</div>
            </div>
        </section>
    </div>

    <script>
        (function() {
            let selectedPeriod = 'LATEST_7DAYS'; // 預設：最新一期(近7天)
            let selectedServer = 'ALL';
            let searchQuery = '';

            const searchInput = document.getElementById('searchInput');
            const periodPills = document.querySelectorAll('#periodFilters .pill-btn');
            const serverPills = document.querySelectorAll('#serverFilters .pill-btn');
            const tableRows = document.querySelectorAll('#tableBody tr');
            const visibleCountEl = document.getElementById('visibleCount');
            const emptyMessage = document.getElementById('emptyMessage');
            const sessionsTable = document.getElementById('sessionsTable');

            function applyFilters() {
                let visibleCount = 0;
                const query = searchQuery.trim().toLowerCase();

                tableRows.forEach(row => {
                    const isLatest = row.getAttribute('data-is-latest') === 'true';
                    const timeGroup = row.getAttribute('data-time-group');
                    const server = row.getAttribute('data-server');
                    const textContent = row.textContent.toLowerCase();

                    // 期間/期別過濾
                    let matchPeriod = false;
                    if (selectedPeriod === 'ALL') {
                        matchPeriod = true;
                    } else if (selectedPeriod === 'LATEST_ALL') {
                        matchPeriod = isLatest;
                    } else if (selectedPeriod === 'LATEST_7DAYS') {
                        matchPeriod = isLatest && timeGroup === 'active_7days';
                    } else if (selectedPeriod === 'LATEST_ARCHIVED') {
                        matchPeriod = isLatest && timeGroup === 'archived';
                    }

                    // 伺服器過濾
                    let matchServer = (selectedServer === 'ALL') || (server === selectedServer);

                    // 搜尋
                    let matchSearch = (query === '') || textContent.includes(query);

                    if (matchPeriod && matchServer && matchSearch) {
                        row.style.display = '';
                        visibleCount++;
                    } else {
                        row.style.display = 'none';
                    }
                });

                visibleCountEl.textContent = visibleCount;
                if (visibleCount === 0) {
                    emptyMessage.style.display = 'block';
                    sessionsTable.style.display = 'none';
                } else {
                    emptyMessage.style.display = 'none';
                    sessionsTable.style.display = '';
                }
            }

            searchInput.addEventListener('input', e => { searchQuery = e.target.value; applyFilters(); });

            periodPills.forEach(pill => {
                pill.addEventListener('click', function() {
                    periodPills.forEach(p => p.classList.remove('active'));
                    this.classList.add('active');
                    selectedPeriod = this.getAttribute('data-period');
                    applyFilters();
                });
            });

            serverPills.forEach(pill => {
                pill.addEventListener('click', function() {
                    serverPills.forEach(p => p.classList.remove('active'));
                    this.classList.add('active');
                    selectedServer = this.getAttribute('data-server');
                    applyFilters();
                });
            });

            // 複製到剪貼簿 (Excel TSV)
            document.getElementById('btnCopyExcel').addEventListener('click', () => {
                const visibleRows = Array.from(tableRows).filter(r => r.style.display !== 'none');
                if (visibleRows.length === 0) return alert('無資料可複製');
                
                let tsv = '虛擬機名稱\t健診判定\t備份狀態\t備份完成日\t期別\tVeeam伺服器\t資料更新時間\r\n';
                visibleRows.forEach(row => {
                    tsv += Array.from(row.children).map(td => td.innerText.trim()).join('\t') + '\r\n';
                });
                
                navigator.clipboard.writeText(tsv).then(() => alert('已複製，可貼至 Excel')).catch(() => alert('複製失敗'));
            });

            // 匯出 CSV
            document.getElementById('btnExportCsv').addEventListener('click', () => {
                const visibleRows = Array.from(tableRows).filter(r => r.style.display !== 'none');
                if (visibleRows.length === 0) return alert('無資料可匯出');
                
                let csv = '\uFEFF' + '""虛擬機名稱"",""健診判定"",""備份狀態"",""備份完成日"",""期別"",""Veeam伺服器"",""資料更新時間""\r\n';
                visibleRows.forEach(row => {
                    csv += Array.from(row.children).map(td => '""' + td.innerText.trim().replace(/""/g, '""""') + '""').join(',') + '\r\n';
                });
                
                const a = document.createElement('a');
                a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8;' }));
                a.download = 'VeeamReport.csv';
                a.click();
            });

            applyFilters();
        })();

        let sortDirections = [true, true, true, true, true, true, true];
        function sortTable(colIndex) {
            const tbody = document.getElementById('tableBody');
            const rows = Array.from(tbody.querySelectorAll('tr'));
            const isAsc = !sortDirections[colIndex];
            sortDirections[colIndex] = isAsc;

            rows.sort((a, b) => {
                const valA = a.children[colIndex].innerText.trim();
                const valB = b.children[colIndex].innerText.trim();
                return isAsc ? valA.localeCompare(valB, 'zh-Hant', { numeric: true }) : valB.localeCompare(valA, 'zh-Hant', { numeric: true });
            });
            rows.forEach(r => tbody.appendChild(r));
        }
    </script>
</body>
</html>");

            return sb.ToString();
        }
    }
}
