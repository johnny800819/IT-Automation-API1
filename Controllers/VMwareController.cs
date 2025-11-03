using API.DataModels;
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
    }
}
