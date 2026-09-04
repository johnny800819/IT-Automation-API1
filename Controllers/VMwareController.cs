using API.DataModels;
using API.DataModels.VMware;
using API.Services.VMware;
using Microsoft.AspNetCore.Mvc;
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

        /// <summary>
        /// 初始化 VMwareController 的新執行個體。
        /// </summary>
        public VMwareController(IVMwareService vmwareService, ILogger<VMwareController> logger)
        {
            _vmwareService = vmwareService;
            _logger = logger;
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
        /// <returns>一個包含所有虛擬機硬體與資源配置狀態的 HTML 報告。</returns>
        [HttpGet("GetVMsHardwareReport")]
        [Produces("text/html")]
        public async Task<IActionResult> GetVMsHardwareReport(string val)
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

                // 1. 從 Service 層獲取包含 VM 與 ESXi 實體主機的整合資料
                var reportData = await _vmwareService.GetVmHardwareReportDataAsync(environmentKey);
                var vmList = reportData.VmList;
                var activeHosts = reportData.ActiveEsxiHosts;

                // 2. 資料處理
                var sortedVmList = vmList.OrderBy(vm => vm.Name).ToList();
                var poweredOnVms = sortedVmList.Where(vm => vm.PowerState == "POWERED_ON").ToList();
                var poweredOffVms = sortedVmList.Where(vm => vm.PowerState == "POWERED_OFF").ToList();

                var totalVmMemoryGB = sortedVmList.Sum(vm => vm.MemorySizeGB ?? 0);
                var totalVmCpus = sortedVmList.Sum(vm => vm.CpuCount ?? 0);

                var totalHostRAM = reportData.TotalHostMemoryGB;
                var totalHostCPUs = reportData.TotalHostCpuCores;

                var htmlBuilder = new StringBuilder();
                string envName = environmentKey == "Production" ? "正式機" : "測試機";

                htmlBuilder.Append(@"
                    <head>
                        <meta charset='utf-8'>
                        <title>VMware 虛擬機與 ESXi 實體主機硬體配置報告</title>
                        <style>
                            body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, 'Microsoft JhengHei', sans-serif; margin: 20px; background-color: #f8f9fa; color: #333; }
                            h2, h3 { color: #2E4053; border-bottom: 2px solid #ccc; padding-bottom: 5px; margin-top: 25px; }
                            table { border-collapse: collapse; width: 100%; margin-top: 15px; margin-bottom: 30px; box-shadow: 0 2px 4px rgba(0,0,0,0.08); background-color: white; border-radius: 4px; overflow: hidden; }
                            th, td { border: 1px solid #dee2e6; padding: 10px 12px; text-align: left; }
                            th { background-color: #007bff; color: white; font-weight: 600; }
                            .th-host { background-color: #495057; color: white; }
                            tr:nth-child(even) { background-color: #fcfcfc; }
                            tr:hover { background-color: #f1f3f5; }
                            .summary-grid { display: flex; gap: 20px; margin-bottom: 25px; flex-wrap: wrap; }
                            .summary-card { flex: 1; min-width: 300px; background-color: #ffffff; padding: 18px 20px; border-left: 6px solid #007bff; border-radius: 4px; box-shadow: 0 2px 4px rgba(0,0,0,0.06); }
                            .summary-card.host-card { border-left-color: #28a745; background-color: #f6fff8; }
                            .summary-card h4 { margin: 0 0 10px 0; color: #2E4053; border-bottom: 1px solid #e9ecef; padding-bottom: 6px; }
                            .summary-card p { margin: 6px 0; font-size: 1.02em; }
                            .badge-on { background-color: #28a745; color: white; padding: 3px 8px; border-radius: 12px; font-size: 0.85em; font-weight: bold; }
                            .badge-off { background-color: #dc3545; color: white; padding: 3px 8px; border-radius: 12px; font-size: 0.85em; font-weight: bold; }
                            .num-align { text-align: right; }
                            .ip-badge { display: inline-block; padding: 2px 7px; margin: 1px 0; border-radius: 4px; font-family: Consolas, Monaco, monospace; font-size: 0.88em; }
                            .ip-primary { background-color: #e8f4fd; color: #0d6efd; border: 1px solid #b6d4fe; font-weight: 600; }
                            .ip-secondary { background-color: #f8f9fa; color: #495057; border: 1px solid #dee2e6; }
                        </style>
                    </head>
                ");

                htmlBuilder.Append($"<body><h2>{envName} VMware 資源與硬體配置報告</h2>");

                // Summary 區塊 (雙卡片呈現：ESXi 實體主機資源 vs VM 配置資源)
                htmlBuilder.Append("<div class='summary-grid'>");

                // 卡片 1: ESXi 實體硬體容量 (非維護且搭載 VM)
                htmlBuilder.Append("<div class='summary-card host-card'>");
                htmlBuilder.Append("<h4>🖥️ ESXi 實體主機資源 (運作中且搭載 VM)</h4>");
                htmlBuilder.Append($"<p><b>有效主機數：</b><b>{activeHosts.Count}</b> 台 (排除維護模式與無 VM 主機)</p>");
                htmlBuilder.Append($"<p><b>實體總 RAM：</b><b style='color:#28a745; font-size:1.15em;'>{totalHostRAM:F2} GB</b></p>");
                htmlBuilder.Append($"<p><b>實體總 CPU：</b><b style='color:#28a745; font-size:1.15em;'>{totalHostCPUs} 核心</b></p>");
                htmlBuilder.Append("</div>");

                // 卡片 2: VM 虛擬機分配總量
                htmlBuilder.Append("<div class='summary-card'>");
                htmlBuilder.Append("<h4>📦 虛擬機 (VM) 資源配置總量</h4>");
                htmlBuilder.Append($"<p><b>虛擬機總數：</b>{sortedVmList.Count} 台 （<span class='badge-on'>開機中：{poweredOnVms.Count} 台</span> ｜ <span class='badge-off'>已關機：{poweredOffVms.Count} 台</span>）</p>");
                htmlBuilder.Append($"<p><b>總配置記憶體：</b><b>{totalVmMemoryGB:F2} GB</b></p>");
                htmlBuilder.Append($"<p><b>總配置 vCPU：</b><b>{totalVmCpus} 核心</b></p>");
                htmlBuilder.Append("</div>");

                htmlBuilder.Append("</div>"); // 結束 summary-grid

                // 若有符合條件的 ESXi 主機，顯示主機明細表
                if (activeHosts.Count > 0)
                {
                    htmlBuilder.Append("<h3>ESXi 實體主機清單（非維護模式且搭載 VM）</h3>");
                    htmlBuilder.Append("<table><thead><tr><th class='th-host'>ESXi 主機名稱</th><th class='th-host num-align'>實體 CPU (核心)</th><th class='th-host num-align'>實體 RAM (GB)</th><th class='th-host num-align'>搭載 VM 數</th><th class='th-host'>狀態</th></tr></thead><tbody>");
                    foreach (var h in activeHosts)
                    {
                        htmlBuilder.Append($"<tr>" +
                            $"<td><b>{h.Name}</b></td>" +
                            $"<td class='num-align'>{h.CpuCores?.ToString() ?? "N/A"}</td>" +
                            $"<td class='num-align'><b>{h.MemorySizeGB?.ToString("F2") ?? "N/A"}</b></td>" +
                            $"<td class='num-align'>{h.VmCount}</td>" +
                            $"<td><span class='badge-on'>正常運作</span></td>" +
                            $"</tr>");
                    }
                    htmlBuilder.Append("</tbody></table>");
                }

                // 建立開機中清單
                htmlBuilder.Append($"<h3>開機中 (POWERED_ON) 虛擬機（共 {poweredOnVms.Count} 台）</h3>");
                htmlBuilder.Append("<table><thead><tr><th>VM 名稱</th><th>IP 位址 (主要 / 次要)</th><th class='num-align'>vCPU (核心)</th><th class='num-align'>記憶體 (GB)</th><th class='num-align'>記憶體 (MiB)</th><th>作業系統 (Guest OS)</th><th>狀態</th></tr></thead><tbody>");
                foreach (var vm in poweredOnVms)
                {
                    htmlBuilder.Append($"<tr>" +
                        $"<td><b>{vm.Name}</b></td>" +
                        $"<td>{RenderIpAddressesHtml(vm)}</td>" +
                        $"<td class='num-align'>{vm.CpuCount?.ToString() ?? "N/A"}</td>" +
                        $"<td class='num-align'><b>{vm.MemorySizeGB?.ToString("F2") ?? "N/A"}</b></td>" +
                        $"<td class='num-align'>{vm.MemorySizeMiB?.ToString("N0") ?? "N/A"}</td>" +
                        $"<td>{vm.GuestOS ?? "N/A"}</td>" +
                        $"<td><span class='badge-on'>開機</span></td>" +
                        $"</tr>");
                }
                htmlBuilder.Append("</tbody></table>");

                // 建立關機中清單
                htmlBuilder.Append($"<h3>已關機 (POWERED_OFF) 虛擬機（共 {poweredOffVms.Count} 台）</h3>");
                htmlBuilder.Append("<table><thead><tr><th>VM 名稱</th><th>IP 位址 (主要 / 次要)</th><th class='num-align'>vCPU (核心)</th><th class='num-align'>記憶體 (GB)</th><th class='num-align'>記憶體 (MiB)</th><th>作業系統 (Guest OS)</th><th>狀態</th></tr></thead><tbody>");
                foreach (var vm in poweredOffVms)
                {
                    htmlBuilder.Append($"<tr>" +
                        $"<td><b>{vm.Name}</b></td>" +
                        $"<td>{RenderIpAddressesHtml(vm)}</td>" +
                        $"<td class='num-align'>{vm.CpuCount?.ToString() ?? "N/A"}</td>" +
                        $"<td class='num-align'><b>{vm.MemorySizeGB?.ToString("F2") ?? "N/A"}</b></td>" +
                        $"<td class='num-align'>{vm.MemorySizeMiB?.ToString("N0") ?? "N/A"}</td>" +
                        $"<td>{vm.GuestOS ?? "N/A"}</td>" +
                        $"<td><span class='badge-off'>關機</span></td>" +
                        $"</tr>");
                }
                htmlBuilder.Append("</tbody></table></body>");

                return Content(htmlBuilder.ToString(), "text/html", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 GetVMsHardwareReport 時發生未預期的錯誤。");
                return StatusCode(500, $"生成 VMware 硬體配置報告時發生內部伺服器錯誤: {ex.Message}");
            }
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
    }
}
