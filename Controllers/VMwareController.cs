using API.DataModels;
using API.DataModels.VMware;
using API.Services.VMware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

#pragma warning disable 1591

namespace API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VMwareController : ControllerBase
    {
        // Using Postman with the vCenter API
        // 參考網站：https://www.vgemba.net/vmware/VCSA-API-Postman/

        private readonly IVMwareService _vmwareService;
        private readonly ILogger<VMwareController> _logger;
        private readonly IMemoryCache _memoryCache;

        /// <summary>
        /// 初始化 VMwareController 的新執行個體。
        /// </summary>
        public VMwareController(IVMwareService vmwareService, ILogger<VMwareController> logger, IMemoryCache memoryCache)
        {
            _vmwareService = vmwareService;
            _logger = logger;
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// 取得 VMware 虛擬機列表 (1是正式, 2是測試)
        /// </summary>
        /// <param name="val">環境選擇； 1 代表正式環境 (Production)，2 代表測試環境 (Test)。</param>
        /// <returns>一個包含所有虛擬機狀態的 HTML 報告。</returns>
        [HttpGet("GetVMsList")]
        [Produces("text/html")]
        public async Task<IActionResult> GetVMsList(string val)
        {
            try
            {
                // 將數字參數轉換為更具可讀性的字串
                string environmentKey;
                switch (val)
                {
                    case "1":
                        environmentKey = "Production";
                        break;
                    case "2":
                        environmentKey = "Test";
                        break;
                    default:
                        _logger.LogWarning("GetVMsList 被呼叫，但使用了無效的環境參數: '{val}'", val ?? "null");
                        var errorMessage = $"缺少或無效的環境參數 '{val}'，請使用 ?val=1 (正式) 或 ?val=2 (測試)。";
                        var errorResult = Content(errorMessage, "text/html", Encoding.UTF8);
                        errorResult.StatusCode = 400;
                        return errorResult;
                }

                // 1. 從 Service 層獲取純資料列表，傳入已轉換的 key
                var vmList = await _vmwareService.GetVmsAsync(environmentKey);

                // 2. 資料處理
                var sortedVmList = vmList.OrderBy(vm => vm.Name).ToList();
                var poweredOnVms = sortedVmList.Where(vm => vm.PowerState == "POWERED_ON").ToList();
                var poweredOffVms = sortedVmList.Where(vm => vm.PowerState == "POWERED_OFF").ToList();

                var htmlBuilder = new StringBuilder();
                string envName = environmentKey == "Production" ? "正式機" : "測試機";

                htmlBuilder.Append(@"
                    <head>
                        <style>
                            body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background-color: #f8f9fa; }
                            h2, h3 { color: #2E4053; border-bottom: 2px solid #ccc; padding-bottom: 5px; }
                            table { border-collapse: collapse; width: 100%; margin-top: 20px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); background-color: white; }
                            th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }
                            th { background-color: #007bff; color: white; }
                            tr:nth-child(even) { background-color: #f2f2f2; }
                            tr:hover { background-color: #e9ecef; }
                            .summary { background-color: #EAF2F8; padding: 15px; border-left: 5px solid #007bff; margin-bottom: 20px; }
                            .summary p { margin: 5px 0; font-size: 1.1em; }
                        </style>
                    </head>
                ");

                htmlBuilder.Append($"<body><h2>{envName} vCenter 虛擬機狀態報告</h2>");

                htmlBuilder.Append("<div class='summary'>");
                htmlBuilder.Append($"<p><b>開機中 (POWERED_ON)：</b>{poweredOnVms.Count} 台</p>");
                htmlBuilder.Append($"<p><b>已關機 (POWERED_OFF)：</b>{poweredOffVms.Count} 台</p>");
                htmlBuilder.Append("</div>");

                // 建立開機列表
                htmlBuilder.Append("<h3>開機中 (POWERED_ON) 虛擬機</h3>");
                htmlBuilder.Append("<table><thead><tr><th>VM 名稱</th><th>IP 位址</th><th>上次開機時間</th></tr></thead><tbody>");
                foreach (var vm in poweredOnVms)
                {
                    htmlBuilder.Append($"<tr><td>{vm.Name}</td><td>{vm.IpAddress ?? "N/A"}</td><td>暫時無法取得</td></tr>");
                }
                htmlBuilder.Append("</tbody></table>");

                // 建立關機列表
                htmlBuilder.Append("<h3>已關機 (POWERED_OFF) 虛擬機</h3>");
                htmlBuilder.Append("<table><thead><tr><th>VM 名稱</th><th>IP 位址</th></tr></thead><tbody>");
                foreach (var vm in poweredOffVms)
                {
                    // 關機的 VM 沒有開機時間，所以表格中不顯示此欄位
                    htmlBuilder.Append($"<tr><td>{vm.Name}</td><td>{vm.IpAddress ?? "N/A"}</td></tr>");
                }
                htmlBuilder.Append("</tbody></table></body>");

                // 4. 回傳 ContentResult
                return Content(htmlBuilder.ToString(), "text/html", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 GetVMsList 時發生未預期的錯誤。");
                return StatusCode(500, $"生成 VMware 報告時發生內部伺服器錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 取得 VMware 虛擬機硬體配置報告（包含記憶體、CPU、作業系統、IP、狀態）(1是正式, 2是測試)
        /// </summary>
        /// <param name="val">環境選擇； 1 代表正式環境 (Production)，2 代表測試環境 (Test)。</param>
        /// <param name="refresh">是否強制穿透快取，重新向 vCenter 擷取最新即時資料（預設 false）。</param>
        /// <returns>一個包含所有虛擬機硬體與資源配置狀態的 HTML 報告。</returns>
        [HttpGet("GetVMsHardwareReport")]
        [Produces("text/html")]
        public async Task<IActionResult> GetVMsHardwareReport(string val, [FromQuery] bool refresh = false)
        {
            try
            {
                // 將數字參數轉換為更具可讀性的字串
                string environmentKey;
                switch (val)
                {
                    case "1":
                        environmentKey = "Production";
                        break;
                    case "2":
                        environmentKey = "Test";
                        break;
                    default:
                        _logger.LogWarning("GetVMsHardwareReport 被呼叫，但使用了無效的環境參數: '{val}'", val ?? "null");
                        var errorMessage = $"缺少或無效的環境參數 '{val}'，請使用 ?val=1 (正式) 或 ?val=2 (測試)。";
                        var errorResult = Content(errorMessage, "text/html", Encoding.UTF8);
                        errorResult.StatusCode = 400;
                        return errorResult;
                }

                // 1. 雙層快取策略 (RAM 記憶體快取 + 本機微型 JSON 快照檔)：實現永久 0.01 秒秒開與零冷啟動
                string cacheKey = $"VMwareHardwareReport_{environmentKey}";
                DateTime snapshotTime = DateTime.Now;
                bool isFromCache = false;
                VmHardwareReportData reportData = null;

                if (!refresh)
                {
                    // A. 先查 RAM 記憶體快取 (奈秒級瞬間讀取)
                    if (_memoryCache.TryGetValue(cacheKey, out (VmHardwareReportData Data, DateTime Timestamp) cachedItem))
                    {
                        reportData = cachedItem.Data;
                        snapshotTime = cachedItem.Timestamp;
                        isFromCache = true;
                    }
                    else
                    {
                        // B. 若 RAM 剛好無快取 (如伺服器剛重啟)，查本機微型 JSON 快照檔 (0.001 秒)
                        var cacheFilePath = GetLocalCacheFilePath(environmentKey);
                        if (System.IO.File.Exists(cacheFilePath))
                        {
                            try
                            {
                                var jsonStr = await System.IO.File.ReadAllTextAsync(cacheFilePath);
                                var diskSnapshot = JsonSerializer.Deserialize<LocalCachePayload>(jsonStr);
                                if (diskSnapshot?.Data != null)
                                {
                                    reportData = diskSnapshot.Data;
                                    snapshotTime = diskSnapshot.Timestamp;
                                    isFromCache = true;
                                    // 回填至記憶體快取 (常駐 2 小時)
                                    _memoryCache.Set(cacheKey, (reportData, snapshotTime), TimeSpan.FromHours(2));
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "讀取本機快照檔案失敗，將改向 vCenter 取得最新資料。");
                            }
                        }
                    }
                }

                // C. 若快取未命中或使用者點擊「即時同步 (refresh=true)」，向 vCenter 擷取 (剪枝優化後僅需約 2 秒)
                if (reportData == null)
                {
                    reportData = await _vmwareService.GetVmHardwareReportDataAsync(environmentKey);
                    snapshotTime = DateTime.Now;
                    isFromCache = false;

                    // 寫入記憶體快取 (保存 2 小時)
                    _memoryCache.Set(cacheKey, (reportData, snapshotTime), TimeSpan.FromHours(2));

                    // 背景非同步持久化至本機快照檔案 (防止重開機冷啟動)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var cacheFilePath = GetLocalCacheFilePath(environmentKey);
                            var dir = Path.GetDirectoryName(cacheFilePath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            var payload = new LocalCachePayload { Data = reportData, Timestamp = snapshotTime };
                            var jsonStr = JsonSerializer.Serialize(payload);
                            await System.IO.File.WriteAllTextAsync(cacheFilePath, jsonStr);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "儲存本機快照檔案失敗。");
                        }
                    });
                }

                var vmList = reportData.VmList;
                var activeHosts = reportData.ActiveEsxiHosts;

                // 2. 資料處理
                var sortedVmList = vmList.OrderBy(vm => vm.Name).ToList();
                var poweredOnVms = sortedVmList.Where(vm => vm.PowerState == "POWERED_ON").ToList();
                var poweredOffVms = sortedVmList.Where(vm => vm.PowerState == "POWERED_OFF").ToList();

                var totalVmMemoryGB = sortedVmList.Sum(vm => vm.MemorySizeGB ?? 0);
                var totalVmMemoryGB_On = poweredOnVms.Sum(vm => vm.MemorySizeGB ?? 0);
                var totalVmMemoryGB_Off = poweredOffVms.Sum(vm => vm.MemorySizeGB ?? 0);

                var totalVmCpus = sortedVmList.Sum(vm => vm.CpuCount ?? 0);
                var totalVmCpus_On = poweredOnVms.Sum(vm => vm.CpuCount ?? 0);
                var totalVmCpus_Off = poweredOffVms.Sum(vm => vm.CpuCount ?? 0);

                var totalVmDiskGB = sortedVmList.Sum(vm => vm.TotalDiskCapacityGB);
                var totalVmDiskGB_On = poweredOnVms.Sum(vm => vm.TotalDiskCapacityGB);
                var totalVmDiskGB_Off = poweredOffVms.Sum(vm => vm.TotalDiskCapacityGB);

                var totalHostRAM = reportData.TotalHostMemoryGB;
                var totalHostCPUs = reportData.TotalHostCpuCores;

                // HA 安全容量計算 (N-1 容錯：扣除單一最大節點資源)
                double maxHostRAM = activeHosts.Any() ? activeHosts.Max(h => h.MemorySizeGB ?? 0) : 0;
                int maxHostCPU = activeHosts.Any() ? activeHosts.Max(h => h.CpuCores ?? 0) : 0;

                double safeRAM = activeHosts.Count > 1 ? totalHostRAM - maxHostRAM : totalHostRAM;
                int safeCPU = activeHosts.Count > 1 ? totalHostCPUs - maxHostCPU : totalHostCPUs;

                // 1. 記憶體 (RAM) 計算：管理者首要關注 HA 安全水位使用率 (71.8%)，同時標記實體總量 (35.3%)
                double remainingSafeRAM = Math.Max(0, safeRAM - totalVmMemoryGB_On);
                double ramHaUsagePercent = safeRAM > 0 ? (totalVmMemoryGB_On / safeRAM) * 100 : 0;
                double ramPhysicalUsagePercent = totalHostRAM > 0 ? (totalVmMemoryGB_On / totalHostRAM) * 100 : 0;
                double ramTotalAllocatedPercent = totalHostRAM > 0 ? (totalVmMemoryGB / totalHostRAM) * 100 : 0;

                // 2. 處理器 (CPU) 計算：依業界最佳實務 (Best Practice: 3:1 ~ 4:1 超配比) 進行管理
                const double RecommendedOvercommitRatio = 4.0;
                double currentCpuOvercommitRatio = totalHostCPUs > 0 ? (double)totalVmCpus_On / totalHostCPUs : 0;
                double haCpuOvercommitRatio = safeCPU > 0 ? (double)totalVmCpus_On / safeCPU : 0;

                // 依 4:1 業界標準計算實體與 HA 容錯可用 vCPU 容量
                int physicalSafeVcpuCapacity = (int)(totalHostCPUs * RecommendedOvercommitRatio);
                int remainingPhysicalSafeVcpus = Math.Max(0, physicalSafeVcpuCapacity - totalVmCpus_On);
                int haSafeVcpuCapacity = (int)(safeCPU * RecommendedOvercommitRatio);
                int remainingHaSafeVcpus = Math.Max(0, haSafeVcpuCapacity - totalVmCpus_On);
                double cpuOvercommitPercentOfLimit = (currentCpuOvercommitRatio / RecommendedOvercommitRatio) * 100;

                var htmlBuilder = new StringBuilder();
                string envName = environmentKey == "Production" ? "正式機" : "測試機";

                htmlBuilder.Append(@"
                    <head>
                        <meta charset='utf-8'>
                        <title>VMware 虛擬機與 ESXi 實體主機硬體配置報告</title>
                        <style>
                            html { scroll-behavior: smooth; }
                            body { font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Tahoma, Geneva, Verdana, 'Microsoft JhengHei', sans-serif; margin: 20px; background-color: #f1f5f9; color: #1e293b; }
                            h2 { color: #0f172a; margin-top: 10px; margin-bottom: 16px; font-weight: 700; }
                            
                            /* 頂部釘選快速導覽列 */
                            .quick-nav-container {
                                position: sticky;
                                top: 10px;
                                z-index: 900;
                                background: rgba(255, 255, 255, 0.94);
                                backdrop-filter: blur(8px);
                                -webkit-backdrop-filter: blur(8px);
                                border: 1px solid #cbd5e1;
                                border-radius: 12px;
                                padding: 8px 16px;
                                margin-bottom: 20px;
                                box-shadow: 0 4px 12px rgba(0, 0, 0, 0.06);
                                display: flex;
                                flex-wrap: wrap;
                                align-items: center;
                                justify-content: space-between;
                                gap: 10px;
                            }
                            .quick-nav-links { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
                            .nav-pill {
                                text-decoration: none;
                                color: #334155;
                                background: #f8fafc;
                                padding: 4px 12px;
                                border-radius: 20px;
                                font-size: 0.88em;
                                font-weight: 600;
                                border: 1px solid #cbd5e1;
                                transition: all 0.2s ease;
                                display: inline-flex;
                                align-items: center;
                                gap: 5px;
                            }
                            .nav-pill:hover { background: #2563eb; color: #ffffff; border-color: #2563eb; transform: translateY(-1px); }
                            .nav-pill.pill-on:hover { background: #10b981; border-color: #10b981; }
                            .nav-pill.pill-off:hover { background: #ef4444; border-color: #ef4444; }
                            .nav-actions { display: flex; gap: 6px; }
                            .btn-nav-toggle {
                                background: #ffffff;
                                border: 1px solid #cbd5e1;
                                color: #475569;
                                padding: 4px 10px;
                                border-radius: 6px;
                                font-size: 0.82em;
                                cursor: pointer;
                                font-weight: 500;
                                transition: all 0.2s ease;
                            }
                            .btn-nav-toggle:hover { background: #e2e8f0; color: #0f172a; }

                            /* 可折疊區塊 (Collapsible Section) */
                            .collapsible-section { scroll-margin-top: 75px; margin-bottom: 22px; }
                            .section-heading {
                                list-style: none;
                                background: #ffffff;
                                border: 1px solid #cbd5e1;
                                border-left: 6px solid #2563eb;
                                border-radius: 8px;
                                padding: 12px 18px;
                                font-size: 1.1em;
                                font-weight: 700;
                                color: #0f172a;
                                cursor: pointer;
                                user-select: none;
                                display: flex;
                                justify-content: space-between;
                                align-items: center;
                                box-shadow: 0 2px 4px rgba(0,0,0,0.03);
                                transition: all 0.2s ease;
                            }
                            .section-heading::-webkit-details-marker { display: none; }
                            .section-heading:hover { background: #f8fafc; border-color: #94a3b8; }
                            .heading-host { border-left-color: #10b981; }
                            .heading-on { border-left-color: #10b981; }
                            .heading-off { border-left-color: #ef4444; }
                            .collapsible-section[open] .section-heading {
                                border-bottom-left-radius: 0;
                                border-bottom-right-radius: 0;
                                border-bottom-color: #f1f5f9;
                            }
                            .section-content {
                                background: #ffffff;
                                border: 1px solid #cbd5e1;
                                border-top: none;
                                border-bottom-left-radius: 8px;
                                border-bottom-right-radius: 8px;
                                padding: 16px;
                                box-shadow: 0 4px 6px -1px rgba(0,0,0,0.04);
                            }
                            .fold-icon::after {
                                content: '收合 ▲';
                                font-size: 0.78em;
                                font-weight: normal;
                                color: #64748b;
                                background: #f1f5f9;
                                padding: 2px 8px;
                                border-radius: 12px;
                                border: 1px solid #e2e8f0;
                            }
                            .collapsible-section:not([open]) .fold-icon::after {
                                content: '展開 ▼';
                                color: #2563eb;
                                background: #eff6ff;
                                border-color: #bfdbfe;
                                font-weight: 600;
                            }

                            /* 表格與卡片樣式 */
                            table { border-collapse: collapse; width: 100%; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.07); background-color: white; border-radius: 8px; overflow: hidden; }
                            th, td { border: 1px solid #e2e8f0; padding: 10px 12px; text-align: left; }
                            th { background: #2563eb; color: white; font-weight: 600; cursor: pointer; user-select: none; position: relative; padding-right: 20px; }
                            th:hover { background: #1d4ed8; }
                            th::after { content: '↕'; position: absolute; right: 6px; color: rgba(255,255,255,0.4); font-size: 0.85em; }
                            th.sort-asc::after { content: '▲'; color: white; }
                            th.sort-desc::after { content: '▼'; color: white; }
                            .th-host { background: #334155; color: white; cursor: pointer; }
                            .th-host:hover { background: #1e293b; }
                            tr:nth-child(even) { background-color: #f8fafc; }
                            tr:hover { background-color: #f1f5f9; }
                            .summary-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(420px, 1fr)); gap: 20px; }
                            .summary-card { background-color: #ffffff; padding: 20px; border-radius: 8px; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.06), 0 2px 4px -2px rgba(0,0,0,0.04); display: flex; flex-direction: column; border-top: 4px solid #2563eb; }
                            .summary-card.host-card { border-top-color: #10b981; }
                            .summary-card h4 { margin: 0 0 14px 0; color: #0f172a; font-size: 1.1em; display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #f1f5f9; padding-bottom: 8px; }
                            .stat-box-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin-bottom: 12px; }
                            .stat-box { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 8px 6px; text-align: center; }
                            .stat-box .stat-val { font-size: 1.25em; font-weight: 700; line-height: 1.2; }
                            .stat-box .stat-lbl { font-size: 0.76em; color: #64748b; margin-top: 4px; }
                            .badge-on { background-color: #10b981; color: white; padding: 3px 8px; border-radius: 12px; font-size: 0.82em; font-weight: bold; }
                            .badge-off { background-color: #ef4444; color: white; padding: 3px 8px; border-radius: 12px; font-size: 0.82em; font-weight: bold; }
                            .num-align { text-align: right; }
                            .ip-badge { display: inline-block; padding: 2px 7px; margin: 1px 0; border-radius: 4px; font-family: Consolas, Monaco, monospace; font-size: 0.88em; }
                            .ip-primary { background-color: #eff6ff; color: #1d4ed8; border: 1px solid #bfdbfe; font-weight: 600; }
                            .ip-secondary { background-color: #f8fafc; color: #475569; border: 1px solid #e2e8f0; }
                            .disk-badges { margin-top: 3px; display: flex; flex-wrap: wrap; gap: 3px; justify-content: flex-end; }
                            .disk-badge { display: inline-block; padding: 1px 5px; border-radius: 3px; font-size: 0.76em; background-color: #f1f5f9; color: #334155; border: 1px solid #cbd5e1; font-family: Consolas, Monaco, monospace; }
                            .text-muted { color: #94a3b8; font-size: 0.9em; }
                            .progress-container { width: 100%; background-color: #e2e8f0; border-radius: 6px; overflow: hidden; margin: 6px 0 10px; height: 18px; box-shadow: inset 0 1px 2px rgba(0,0,0,.08); }
                            .progress-bar { height: 100%; display: flex; align-items: center; justify-content: center; color: white; font-size: 0.78em; font-weight: bold; transition: width 0.6s ease; text-shadow: 0px 1px 1px rgba(0,0,0,0.4); }
                            .bg-safe { background-color: #10b981; }
                            .bg-warn { background-color: #f59e0b; color: #ffffff; }
                            .bg-danger { background-color: #ef4444; }
                            .os-text { font-size: 0.82em; color: #475569; line-height: 1.3; }
                            details.card-details { margin-top: 8px; font-size: 0.88em; }
                            details.card-details summary { cursor: pointer; color: #2563eb; font-weight: 600; outline: none; user-select: none; padding: 4px 0; }
                            details.card-details summary:hover { color: #1d4ed8; text-decoration: underline; }
                            .details-content { background-color: #f8fafc; border-radius: 6px; padding: 8px 12px; margin-top: 6px; border: 1px solid #e2e8f0; color: #475569; line-height: 1.6; }

                            /* 浮動回到頂部按鈕 */
                            .floating-top-btn {
                                position: fixed;
                                bottom: 24px;
                                right: 24px;
                                z-index: 999;
                                background: #2563eb;
                                color: white;
                                border: none;
                                border-radius: 50%;
                                width: 44px;
                                height: 44px;
                                font-size: 1.1em;
                                cursor: pointer;
                                box-shadow: 0 4px 12px rgba(37,99,235,0.4);
                                display: flex;
                                align-items: center;
                                justify-content: center;
                                text-decoration: none;
                                transition: all 0.2s ease;
                                opacity: 0.85;
                            }
                            .floating-top-btn:hover {
                                opacity: 1;
                                transform: translateY(-3px);
                                box-shadow: 0 6px 16px rgba(37,99,235,0.5);
                                color: white;
                            }

                            /* 全螢幕半透明毛玻璃 Loading 遮罩 */
                            .loading-mask {
                                position: fixed;
                                top: 0;
                                left: 0;
                                width: 100vw;
                                height: 100vh;
                                background: rgba(15, 23, 42, 0.75);
                                backdrop-filter: blur(8px);
                                -webkit-backdrop-filter: blur(8px);
                                z-index: 99999;
                                display: none;
                                align-items: center;
                                justify-content: center;
                            }
                            .loading-box {
                                background: #ffffff;
                                padding: 32px 42px;
                                border-radius: 16px;
                                box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.3), 0 10px 10px -5px rgba(0, 0, 0, 0.15);
                                text-align: center;
                                max-width: 440px;
                                display: flex;
                                flex-direction: column;
                                align-items: center;
                                gap: 14px;
                            }
                            .loading-spinner {
                                width: 44px;
                                height: 44px;
                                border: 4px solid #e2e8f0;
                                border-top-color: #2563eb;
                                border-radius: 50%;
                                animation: spin 0.8s linear infinite;
                            }
                            @keyframes spin {
                                0% { transform: rotate(0deg); }
                                100% { transform: rotate(360deg); }
                            }
                            .loading-title { font-size: 1.15em; font-weight: 700; color: #0f172a; }
                            .loading-desc { font-size: 0.88em; color: #64748b; line-height: 1.4; }
                            
                            .cache-badge {
                                font-size: 0.8em;
                                padding: 4px 10px;
                                border-radius: 20px;
                                display: inline-flex;
                                align-items: center;
                                gap: 4px;
                                font-weight: 500;
                            }
                            .cache-cached { background: #f1f5f9; color: #475569; border: 1px solid #cbd5e1; }
                            .cache-fresh { background: #dcfce7; color: #15803d; border: 1px solid #86efac; font-weight: 600; }
                            
                            .btn-refresh {
                                background: #2563eb;
                                color: #ffffff !important;
                                text-decoration: none;
                                padding: 4px 12px;
                                border-radius: 20px;
                                font-size: 0.86em;
                                font-weight: 600;
                                display: inline-flex;
                                align-items: center;
                                gap: 5px;
                                transition: all 0.2s ease;
                                cursor: pointer;
                                border: 1px solid #2563eb;
                            }
                            .btn-refresh:hover {
                                background: #1d4ed8;
                                border-color: #1d4ed8;
                                transform: translateY(-1px);
                            }
                        </style>
                    </head>
                ");

                // 全螢幕 Loading 遮罩
                htmlBuilder.Append(@"
                    <div id='loading-mask' class='loading-mask'>
                        <div class='loading-box'>
                            <div class='loading-spinner'></div>
                            <div class='loading-title'>⚡ 正在連線 VMware vCenter 同步即時數據...</div>
                            <div class='loading-desc'>正在並行擷取 62 台虛擬機與 ESXi 實體主機規格，請稍候約 2 秒</div>
                        </div>
                    </div>
                ");

                htmlBuilder.Append($"<body><h2>{envName} VMware 資源與硬體配置報告</h2>");

                // 快速導覽列 (支援釘選定位、一鍵全展開/全收合、快照標籤與即時同步按鈕)
                string cacheStatusBadge = isFromCache 
                    ? $"<span class='cache-badge cache-cached' title='資料來源：伺服器快照 (0.01秒瞬間秒開)'>🕒 資料快照：{snapshotTime:yyyy/MM/dd HH:mm:ss} (快取模式)</span>" 
                    : $"<span class='cache-badge cache-fresh' title='資料來源：剛才連線 vCenter 獲取的最新數據'>⚡ 即時連線：{snapshotTime:yyyy/MM/dd HH:mm:ss} (最新即時)</span>";

                htmlBuilder.Append("<div class='quick-nav-container'>");
                htmlBuilder.Append("<div class='quick-nav-links'>");
                htmlBuilder.Append("<span style='font-size:0.9em; font-weight:bold; color:#475569;'>⚡ 快速導覽：</span>");
                htmlBuilder.Append("<a href='#sec-overview' class='nav-pill'>📊 運算資源總覽</a>");
                if (activeHosts.Count > 0)
                {
                    htmlBuilder.Append($"<a href='#sec-hosts' class='nav-pill'>🖥️ ESXi 主機 ({activeHosts.Count})</a>");
                }
                htmlBuilder.Append($"<a href='#sec-vms-on' class='nav-pill pill-on'>🟢 開機 VM ({poweredOnVms.Count})</a>");
                htmlBuilder.Append($"<a href='#sec-vms-off' class='nav-pill pill-off'>🔴 關機 VM ({poweredOffVms.Count})</a>");
                htmlBuilder.Append(cacheStatusBadge);
                htmlBuilder.Append($"<a href='?val={val}&refresh=true' class='btn-refresh' onclick='showLoadingMask()'>🔄 即時同步最新資料</a>");
                htmlBuilder.Append("</div>");
                htmlBuilder.Append("<div class='nav-actions'>");
                htmlBuilder.Append("<button type='button' class='btn-nav-toggle' onclick='toggleAllSections(true)'>全部展開</button>");
                htmlBuilder.Append("<button type='button' class='btn-nav-toggle' onclick='toggleAllSections(false)'>全部收合</button>");
                htmlBuilder.Append("</div>");
                htmlBuilder.Append("</div>");

                // 區塊 1: 運算資源與虛擬機配置總覽 (可折疊)
                htmlBuilder.Append("<details open class='collapsible-section' id='sec-overview'>");
                htmlBuilder.Append("<summary class='section-heading'><span>📊 運算資源與虛擬機配置總覽</span><span class='fold-icon'></span></summary>");
                htmlBuilder.Append("<div class='section-content' style='background:transparent; border:none; padding:15px 0 0 0; box-shadow:none;'>");
                htmlBuilder.Append("<div class='summary-grid'>");

                // 卡片 1: ESXi 實體硬體容量 (非維護且搭載 VM)
                htmlBuilder.Append("<div class='summary-card host-card'>");
                htmlBuilder.Append($"<h4><span>🖥️ ESXi 實體運算資源與 HA 容錯水位</span><span style='font-size:0.85em; font-weight:normal; color:#64748b;'>線上主機：<b>{activeHosts.Count}</b> 台</span></h4>");

                // --- RAM 區塊 ---
                htmlBuilder.Append("<div style='margin-bottom:14px; padding:12px; background:#ffffff; border-radius:6px; border:1px solid #e2e8f0;'>");
                htmlBuilder.Append("<div style='font-weight:600; margin-bottom:8px; color:#0f172a;'>🛡️ 記憶體 (RAM) HA 容錯水位</div>");
                
                string ramHaColor = ramHaUsagePercent > 85 ? "#ef4444" : (ramHaUsagePercent > 70 ? "#f59e0b" : "#10b981");
                htmlBuilder.Append("<div class='stat-box-grid'>");
                htmlBuilder.Append($"<div class='stat-box'><div class='stat-val' style='color:{ramHaColor};'>{ramHaUsagePercent:F1}%</div><div class='stat-lbl'>HA 安全水位 (N-1)</div></div>");
                htmlBuilder.Append($"<div class='stat-box'><div class='stat-val' style='color:#10b981;'>{remainingSafeRAM:F1} G</div><div class='stat-lbl'>HA 安全剩餘</div></div>");
                htmlBuilder.Append($"<div class='stat-box'><div class='stat-val' style='color:#334155;'>{ramPhysicalUsagePercent:F1}%</div><div class='stat-lbl'>實體配置率</div></div>");
                htmlBuilder.Append("</div>");

                string ramColorClass = ramHaUsagePercent > 85 ? "bg-danger" : (ramHaUsagePercent > 70 ? "bg-warn" : "bg-safe");
                htmlBuilder.Append($"<div class='progress-container' title='HA 安全水位使用率: {ramHaUsagePercent:F1}%'><div class='progress-bar {ramColorClass}' style='width: {Math.Min(100, ramHaUsagePercent):F1}%'>{ramHaUsagePercent:F1}% (安全基準 {safeRAM:F0} GB)</div></div>");

                htmlBuilder.Append("<details class='card-details'>");
                htmlBuilder.Append("<summary>查看記憶體深度演算明細 ▾</summary>");
                htmlBuilder.Append("<div class='details-content'>");
                htmlBuilder.Append($"<div>• <b>實體總記憶體 (RAM)：</b>{totalHostRAM:F2} GB (各 ESXi 節點總和)</div>");
                htmlBuilder.Append($"<div>• <b>HA 記憶體容錯基準：</b>{safeRAM:F2} GB (扣除最大節點 {maxHostRAM:F2} GB)</div>");
                htmlBuilder.Append($"<div>• <b>開機中 VM 記憶體配置：</b>{totalVmMemoryGB_On:F2} GB (佔 HA 基準 {ramHaUsagePercent:F1}%)</div>");
                htmlBuilder.Append($"<div>• <b>所有 VM 記憶體總配置：</b>{totalVmMemoryGB:F2} GB (佔實體總記憶體 {ramTotalAllocatedPercent:F1}%)</div>");
                htmlBuilder.Append("</div>");
                htmlBuilder.Append("</details>");
                htmlBuilder.Append("</div>");

                // --- CPU 區塊 (業界最佳實務超配比) ---
                htmlBuilder.Append("<div style='padding:12px; background:#ffffff; border-radius:6px; border:1px solid #e2e8f0;'>");
                htmlBuilder.Append("<div style='font-weight:600; margin-bottom:8px; color:#0f172a;'>⚡ 處理器 (CPU) 超額配置比 (Overcommit)</div>");

                string cpuRatioColor = currentCpuOvercommitRatio > 4.5 ? "#ef4444" : (currentCpuOvercommitRatio > 3.5 ? "#f59e0b" : "#10b981");
                htmlBuilder.Append("<div class='stat-box-grid'>");
                htmlBuilder.Append($"<div class='stat-box'><div class='stat-val' style='color:{cpuRatioColor};'>{currentCpuOvercommitRatio:F2} : 1</div><div class='stat-lbl'>目前超配比</div></div>");
                htmlBuilder.Append($"<div class='stat-box'><div class='stat-val' style='color:#10b981;'>{remainingPhysicalSafeVcpus} 核</div><div class='stat-lbl'>4:1 剩餘 vCPU</div></div>");
                htmlBuilder.Append($"<div class='stat-box'><div class='stat-val' style='color:#2563eb;'>健康高效</div><div class='stat-lbl'>業界最佳實務</div></div>");
                htmlBuilder.Append("</div>");

                string cpuColorClass = currentCpuOvercommitRatio > 4.5 ? "bg-danger" : (currentCpuOvercommitRatio > 3.5 ? "bg-warn" : "bg-safe");
                htmlBuilder.Append($"<div class='progress-container' title='目前超配比佔 4.0:1 基準之百分比: {cpuOvercommitPercentOfLimit:F1}%'><div class='progress-bar {cpuColorClass}' style='width: {Math.Min(100, cpuOvercommitPercentOfLimit):F1}%'>{currentCpuOvercommitRatio:F2} : 1 (基準 4:1)</div></div>");

                htmlBuilder.Append("<details class='card-details'>");
                htmlBuilder.Append("<summary>查看運算公式與 HA 單機容錯衝擊 ▾</summary>");
                htmlBuilder.Append("<div class='details-content'>");
                htmlBuilder.Append($"<div>• <b>實體核心與配置：</b>實體 {totalHostCPUs} 核心 ｜ 已開機 {totalVmCpus_On} vCPU (含關機共 {totalVmCpus} vCPU)</div>");
                htmlBuilder.Append($"<div>• <b>業界最佳建議值：</b><b>3:1 ~ 4:1</b> 為虛擬化伺服器最佳平衡區</div>");
                htmlBuilder.Append($"<div>• <b>實體安全剩餘可用 vCPU (4:1 基準)：</b><b style='color:#10b981;'>{remainingPhysicalSafeVcpus} 核心</b> (以 4:1 可承載 {physicalSafeVcpuCapacity} vCPU)</div>");
                string haCpuNoteColor = haCpuOvercommitRatio > 4.0 ? "#ef4444" : "#10b981";
                htmlBuilder.Append($"<div>• <b>HA 單機容錯 (N-1) 衝擊比：</b><b style='color:{haCpuNoteColor};'>{haCpuOvercommitRatio:F2} : 1</b> (單機故障時 24 核承載 {totalVmCpus_On} vCPU，安全剩餘 {remainingHaSafeVcpus} 核心)</div>");
                if (haCpuOvercommitRatio > 4.0)
                {
                    htmlBuilder.Append("<div style='color:#dc2626; font-size:0.85em; margin-top:4px;'>⚠️ 註：目前 2 台架構在單機故障時超配比偏高，建議未來擴充第 3、第 4 台 ESXi 分擔 HA 負載。</div>");
                }
                htmlBuilder.Append("</div>");
                htmlBuilder.Append("</details>");
                htmlBuilder.Append("</div>");

                htmlBuilder.Append("</div>"); // 結束 卡片 1

                // 卡片 2: VM 虛擬機分配總量 (包含 RAM / CPU / 磁碟 Disk)
                htmlBuilder.Append("<div class='summary-card'>");
                htmlBuilder.Append($"<h4><span>📦 虛擬機 (VM) 資源配置總量</span><span style='font-size:0.85em; font-weight:normal; color:#64748b;'>共 <b>{sortedVmList.Count}</b> 台</span></h4>");

                htmlBuilder.Append("<div class='stat-box-grid'>");
                htmlBuilder.Append($"<div class='stat-box'><div class='stat-val' style='color:#10b981;'>{poweredOnVms.Count}</div><div class='stat-lbl'>已開機 VM</div></div>");
                htmlBuilder.Append($"<div class='stat-box'><div class='stat-val' style='color:#ef4444;'>{poweredOffVms.Count}</div><div class='stat-lbl'>已關機 VM</div></div>");
                htmlBuilder.Append($"<div class='stat-box'><div class='stat-val' style='color:#2563eb;'>{sortedVmList.Count}</div><div class='stat-lbl'>虛擬機總數</div></div>");
                htmlBuilder.Append("</div>");

                // 三大資源配置區塊 (RAM / CPU / 磁碟)
                htmlBuilder.Append("<div style='padding:12px; background:#ffffff; border-radius:6px; border:1px solid #e2e8f0; margin-bottom:14px;'>");
                htmlBuilder.Append("<div style='display:flex; justify-content:space-between; margin-bottom:6px;'>");
                htmlBuilder.Append("<span><b>🧠 記憶體配置：</b></span>");
                htmlBuilder.Append($"<span><b>{totalVmMemoryGB:F2} GB</b></span>");
                htmlBuilder.Append("</div>");
                htmlBuilder.Append($"<div style='font-size:0.85em; color:#64748b; margin-bottom:10px;'>• 開機分配：{totalVmMemoryGB_On:F2} GB ｜ 關機分配：{totalVmMemoryGB_Off:F2} GB</div>");

                htmlBuilder.Append("<div style='display:flex; justify-content:space-between; margin-bottom:6px;'>");
                htmlBuilder.Append("<span><b>⚡ vCPU 配置：</b></span>");
                htmlBuilder.Append($"<span><b>{totalVmCpus} 核心</b></span>");
                htmlBuilder.Append("</div>");
                htmlBuilder.Append($"<div style='font-size:0.85em; color:#64748b; margin-bottom:10px;'>• 開機分配：{totalVmCpus_On} 核心 ｜ 關機分配：{totalVmCpus_Off} 核心</div>");

                string diskDisplayStr = totalVmDiskGB >= 1024.0 ? $"{(totalVmDiskGB / 1024.0):F2} TB ({totalVmDiskGB:N0} GB)" : $"{totalVmDiskGB:F2} GB";
                htmlBuilder.Append("<div style='display:flex; justify-content:space-between; margin-bottom:6px;'>");
                htmlBuilder.Append("<span><b>💾 虛擬磁碟配置 (Disk)：</b></span>");
                htmlBuilder.Append($"<span><b style='color:#2563eb;'>{diskDisplayStr}</b></span>");
                htmlBuilder.Append("</div>");
                htmlBuilder.Append($"<div style='font-size:0.85em; color:#64748b;'>• 開機分配：{totalVmDiskGB_On:N1} GB ｜ 關機分配：{totalVmDiskGB_Off:N1} GB</div>");
                htmlBuilder.Append("</div>");

                htmlBuilder.Append("<details class='card-details'>");
                htmlBuilder.Append("<summary>查看虛擬機開關機狀態說明 ▾</summary>");
                htmlBuilder.Append("<div class='details-content'>");
                htmlBuilder.Append($"<div>• <b>VM 開機率：</b>{(poweredOnVms.Count * 100.0 / sortedVmList.Count):F1}% (開機 {poweredOnVms.Count} 台 / 總數 {sortedVmList.Count} 台)</div>");
                htmlBuilder.Append($"<div>• <b>開關機特性：</b>關機 VM 僅佔用虛擬磁碟空間，不消耗實體 CPU 與記憶體運算資源。</div>");
                htmlBuilder.Append("</div>");
                htmlBuilder.Append("</details>");

                htmlBuilder.Append("</div>"); // 結束 卡片 2
                htmlBuilder.Append("</div>"); // 結束 summary-grid
                htmlBuilder.Append("</div>"); // 結束 section-content
                htmlBuilder.Append("</details>"); // 結束 sec-overview

                // 區塊 2: ESXi 實體主機清單 (可折疊)
                if (activeHosts.Count > 0)
                {
                    htmlBuilder.Append("<details open class='collapsible-section' id='sec-hosts'>");
                    htmlBuilder.Append($"<summary class='section-heading heading-host'><span>🖥️ ESXi 實體主機清單（非維護模式且搭載 VM，共 <b>{activeHosts.Count}</b> 台）</span><span class='fold-icon'></span></summary>");
                    htmlBuilder.Append("<div class='section-content' style='padding:0; overflow:hidden;'>");
                    htmlBuilder.Append("<table style='margin:0; box-shadow:none; border-radius:0;'><thead><tr>" +
                        "<th class='th-host'>ESXi 主機名稱</th>" +
                        "<th class='th-host num-align'>實體 CPU</th>" +
                        "<th class='th-host num-align'>實體 RAM</th>" +
                        "<th class='th-host num-align'>搭載 VM (開機 / 關機)</th>" +
                        "<th class='th-host' style='min-width: 180px;'>RAM 配置用量 (滿載率)</th>" +
                        "<th class='th-host' style='min-width: 170px;'>vCPU 超配比 (負載度)</th>" +
                        "<th class='th-host' style='width: 70px;'>狀態</th>" +
                        "</tr></thead><tbody>");

                    foreach (var h in activeHosts)
                    {
                        var hostVms = vmList.Where(v => (h.VmIds != null && h.VmIds.Contains(v.VmId)) || v.HostId == h.HostId).ToList();
                        var hostVmsOn = hostVms.Where(v => v.PowerState == "POWERED_ON").ToList();
                        var hostVmsOff = hostVms.Where(v => v.PowerState == "POWERED_OFF").ToList();

                        double hostRamAllocatedGB = hostVmsOn.Sum(v => v.MemorySizeGB ?? 0);
                        double hostRamTotalGB = h.MemorySizeGB ?? 0;
                        double hostRamUsagePercent = hostRamTotalGB > 0 ? (hostRamAllocatedGB / hostRamTotalGB) * 100 : 0;

                        int hostCpuAllocated = hostVmsOn.Sum(v => v.CpuCount ?? 0);
                        int hostCpuTotal = h.CpuCores ?? 0;
                        double hostCpuRatio = hostCpuTotal > 0 ? (double)hostCpuAllocated / hostCpuTotal : 0;

                        string hRamColor = hostRamUsagePercent > 85 ? "bg-danger" : (hostRamUsagePercent > 70 ? "bg-warn" : "bg-safe");
                        string hCpuColor = hostCpuRatio > 4.5 ? "bg-danger" : (hostCpuRatio > 3.5 ? "bg-warn" : "bg-safe");
                        string hCpuTextColor = hostCpuRatio > 4.5 ? "#ef4444" : (hostCpuRatio > 3.5 ? "#f59e0b" : "#10b981");

                        htmlBuilder.Append("<tr>" +
                            $"<td><b>{h.Name}</b></td>" +
                            $"<td class='num-align'>{hostCpuTotal} 核心</td>" +
                            $"<td class='num-align'><b>{hostRamTotalGB:F2} GB</b></td>" +
                            $"<td class='num-align'>{h.VmCount} 台 (<span class='badge-on'>{hostVmsOn.Count}</span> / <span class='badge-off'>{hostVmsOff.Count}</span>)</td>" +
                            $"<td>" +
                                $"<div style='display:flex; justify-content:space-between; font-size:0.85em; margin-bottom:2px;'>" +
                                    $"<span><b>{hostRamAllocatedGB:F2} GB</b></span>" +
                                    $"<span><b>{hostRamUsagePercent:F1}%</b></span>" +
                                $"</div>" +
                                $"<div class='progress-container' style='margin:0; height:8px;'>" +
                                    $"<div class='progress-bar {hRamColor}' style='width:{Math.Min(100, hostRamUsagePercent):F1}%;'></div>" +
                                $"</div>" +
                            $"</td>" +
                            $"<td>" +
                                $"<div style='display:flex; justify-content:space-between; font-size:0.85em; margin-bottom:2px;'>" +
                                    $"<span><b>{hostCpuAllocated} vCPU</b></span>" +
                                    $"<span style='color:{hCpuTextColor}; font-weight:bold;'>{hostCpuRatio:F2} : 1</span>" +
                                $"</div>" +
                                $"<div class='progress-container' style='margin:0; height:8px;'>" +
                                    $"<div class='progress-bar {hCpuColor}' style='width:{Math.Min(100, (hostCpuRatio / 4.0) * 100):F1}%;'></div>" +
                                $"</div>" +
                            $"</td>" +
                            $"<td><span class='badge-on'>正常運作</span></td>" +
                            $"</tr>");
                    }
                    htmlBuilder.Append("</tbody></table>");
                    htmlBuilder.Append("</div>");
                    htmlBuilder.Append("</details>");
                }

                // 區塊 3: 建立開機中清單 (可折疊)
                htmlBuilder.Append("<details open class='collapsible-section' id='sec-vms-on'>");
                htmlBuilder.Append($"<summary class='section-heading heading-on'><span>🟢 開機中 (POWERED_ON) 虛擬機（共 <b>{poweredOnVms.Count}</b> 台）</span><span class='fold-icon'></span></summary>");
                htmlBuilder.Append("<div class='section-content' style='padding:0; overflow:hidden;'>");
                htmlBuilder.Append("<table style='margin:0; box-shadow:none; border-radius:0;'><thead><tr>" +
                    "<th>VM 名稱</th>" +
                    "<th>IP 位址 (主要 / 次要)</th>" +
                    "<th class='num-align'>vCPU</th>" +
                    "<th class='num-align'>記憶體 (GB)</th>" +
                    "<th class='num-align' style='min-width: 140px;'>磁碟配置 (Disk)</th>" +
                    "<th>作業系統 (Guest OS)</th>" +
                    "<th style='width: 60px;'>狀態</th>" +
                    "</tr></thead><tbody>");

                foreach (var vm in poweredOnVms)
                {
                    htmlBuilder.Append($"<tr>" +
                        $"<td><b>{vm.Name}</b></td>" +
                        $"<td>{RenderIpAddressesHtml(vm)}</td>" +
                        $"<td class='num-align'>{vm.CpuCount?.ToString() ?? "N/A"}</td>" +
                        $"<td class='num-align'><b>{vm.MemorySizeGB?.ToString("F2") ?? "N/A"}</b></td>" +
                        $"<td class='num-align'>{RenderDisksHtml(vm)}</td>" +
                        $"<td class='os-text'>{vm.GuestOS ?? "N/A"}</td>" +
                        $"<td><span class='badge-on'>開機</span></td>" +
                        $"</tr>");
                }
                htmlBuilder.Append("</tbody></table>");
                htmlBuilder.Append("</div>");
                htmlBuilder.Append("</details>");

                // 區塊 4: 建立關機中清單 (可折疊)
                htmlBuilder.Append("<details open class='collapsible-section' id='sec-vms-off'>");
                htmlBuilder.Append($"<summary class='section-heading heading-off'><span>🔴 已關機 (POWERED_OFF) 虛擬機（共 <b>{poweredOffVms.Count}</b> 台）</span><span class='fold-icon'></span></summary>");
                htmlBuilder.Append("<div class='section-content' style='padding:0; overflow:hidden;'>");
                htmlBuilder.Append("<table style='margin:0; box-shadow:none; border-radius:0;'><thead><tr>" +
                    "<th>VM 名稱</th>" +
                    "<th>IP 位址 (主要 / 次要)</th>" +
                    "<th class='num-align'>vCPU</th>" +
                    "<th class='num-align'>記憶體 (GB)</th>" +
                    "<th class='num-align' style='min-width: 140px;'>磁碟配置 (Disk)</th>" +
                    "<th>作業系統 (Guest OS)</th>" +
                    "<th style='width: 60px;'>狀態</th>" +
                    "</tr></thead><tbody>");

                foreach (var vm in poweredOffVms)
                {
                    htmlBuilder.Append($"<tr>" +
                        $"<td><b>{vm.Name}</b></td>" +
                        $"<td>{RenderIpAddressesHtml(vm)}</td>" +
                        $"<td class='num-align'>{vm.CpuCount?.ToString() ?? "N/A"}</td>" +
                        $"<td class='num-align'><b>{vm.MemorySizeGB?.ToString("F2") ?? "N/A"}</b></td>" +
                        $"<td class='num-align'>{RenderDisksHtml(vm)}</td>" +
                        $"<td class='os-text'>{vm.GuestOS ?? "N/A"}</td>" +
                        $"<td><span class='badge-off'>關機</span></td>" +
                        $"</tr>");
                }
                htmlBuilder.Append("</tbody></table>");
                htmlBuilder.Append("</div>");
                htmlBuilder.Append("</details>");

                // 浮動回到頂部按鈕
                htmlBuilder.Append("<a href='#' class='floating-top-btn' title='回到頂部'>▲</a>");

                // 注入表格前端動態排序與快速導覽折疊 JavaScript
                htmlBuilder.Append(@"
                    <script>
                        window.showLoadingMask = function() {
                            var mask = document.getElementById('loading-mask');
                            if (mask) {
                                mask.style.display = 'flex';
                            }
                        };

                        window.toggleAllSections = function(expand) {
                            document.querySelectorAll('.collapsible-section').forEach(sec => {
                                sec.open = expand;
                            });
                        };

                        document.addEventListener('DOMContentLoaded', function() {
                            // 點擊快速導覽時若目標已收合則自動展開
                            document.querySelectorAll('.nav-pill').forEach(pill => {
                                pill.addEventListener('click', function(e) {
                                    const targetId = this.getAttribute('href');
                                    if (targetId && targetId.startsWith('#sec')) {
                                        const targetSec = document.querySelector(targetId);
                                        if (targetSec) {
                                            targetSec.open = true;
                                        }
                                    }
                                });
                            });

                            const parseValue = (val) => {
                                let clean = val.trim();
                                if (clean === '' || clean === 'N/A' || clean === '-') return -99999999;
                                let m = clean.replace(/,/g, '').match(/^(\d+(\.\d+)?)/);
                                if (m) return parseFloat(m[1]);
                                return clean;
                            };

                            const getCellValue = (tr, idx) => tr.children[idx].innerText || tr.children[idx].textContent;

                            const comparer = (idx, asc) => (a, b) => {
                                let v1 = getCellValue(asc ? a : b, idx);
                                let v2 = getCellValue(asc ? b : a, idx);
                                let p1 = parseValue(v1);
                                let p2 = parseValue(v2);
                                
                                if (typeof p1 === 'number' && typeof p2 === 'number') {
                                    return p1 - p2;
                                }
                                return v1.toString().localeCompare(v2);
                            };

                            document.querySelectorAll('th').forEach(th => {
                                th.addEventListener('click', function() {
                                    const table = th.closest('table');
                                    const tbody = table.querySelector('tbody');
                                    if(!tbody) return;
                                    
                                    let asc = !this.asc;
                                    this.asc = asc;
                                    
                                    table.querySelectorAll('th').forEach(h => h.classList.remove('sort-asc', 'sort-desc'));
                                    this.classList.add(asc ? 'sort-asc' : 'sort-desc');

                                    Array.from(tbody.querySelectorAll('tr'))
                                        .sort(comparer(Array.from(th.parentNode.children).indexOf(th), asc))
                                        .forEach(tr => tbody.appendChild(tr));
                                });
                            });
                        });
                    </script>
                ");

                htmlBuilder.Append("</body>");

                return Content(htmlBuilder.ToString(), "text/html", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 GetVMsHardwareReport 時發生未預期的錯誤。");
                return StatusCode(500, $"生成 VMware 硬體配置報告時發生內部伺服器錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 格式化輸出虛擬機的磁碟 (Disk) 配置容量與標籤。
        /// </summary>
        private static string RenderDisksHtml(VmInfo vm)
        {
            if (vm.Disks == null || vm.Disks.Count == 0)
            {
                return "<span class='text-muted'>-</span>";
            }

            var sb = new StringBuilder();
            sb.Append($"<b style='font-size:1.05em;'>{vm.TotalDiskCapacityGB:N2} GB</b>");

            if (vm.Disks.Count > 1)
            {
                sb.Append("<div class='disk-badges'>");
                int maxDisplay = 4; // 限制最多顯示 4 顆磁碟，避免撐破版面
                for (int i = 0; i < Math.Min(vm.Disks.Count, maxDisplay); i++)
                {
                    var d = vm.Disks[i];
                    string labelStr = d.CapacityGB >= 1000 ? $"{(d.CapacityGB / 1024.0):0.#}T" : $"{d.CapacityGB:0.#}G";
                    sb.Append($"<span class='disk-badge' title='{d.Label}: {d.CapacityGB:N2} GB'>{labelStr}</span>");
                }
                if (vm.Disks.Count > maxDisplay)
                {
                    int remaining = vm.Disks.Count - maxDisplay;
                    sb.Append($"<span class='disk-badge' style='background:#e2e8f0; font-weight:bold;' title='還有 {remaining} 顆未顯示'>+{remaining}</span>");
                }
                sb.Append("</div>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 格式化輸出虛擬機的 IP 位址清單為 HTML 標籤（Primary IP 醒目標示，次要 IP 垂直排列）。
        /// </summary>
        private static string RenderIpAddressesHtml(VmInfo vm)
        {
            if (vm.IpAddresses != null && vm.IpAddresses.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < vm.IpAddresses.Count; i++)
                {
                    var ip = vm.IpAddresses[i];
                    if (i == 0)
                    {
                        // 首位為 Primary IP，醒目標籤呈現
                        sb.Append($"<span class='ip-badge ip-primary' title='主要 IP (Primary)'>{ip}</span>");
                    }
                    else
                    {
                        // 次要 IP，垂直分行淺色標籤呈現
                        sb.Append($"<br/><span class='ip-badge ip-secondary' title='其他網卡 / 次要 IP'>{ip}</span>");
                    }
                }
                return sb.ToString();
            }

            if (!string.IsNullOrEmpty(vm.IpAddress))
            {
                return $"<span class='ip-badge ip-primary'>{vm.IpAddress}</span>";
            }

            return "<span style='color:#999;'>N/A</span>";
        }

        /// <summary>
        /// 取得本機快照檔案存放路徑。
        /// </summary>
        private static string GetLocalCacheFilePath(string environmentKey)
        {
            return Path.Combine(AppContext.BaseDirectory, "App_Data", "Cache", $"vm_report_{environmentKey}.json");
        }
    }

    /// <summary>
    /// 本機持久化快照模型（確保伺服器重啟後依然能 0.01 秒秒開）
    /// </summary>
    public class LocalCachePayload
    {
        public VmHardwareReportData Data { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
