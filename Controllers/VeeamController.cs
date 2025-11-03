using API.Services.Veeam;
using Microsoft.AspNetCore.Mvc;
using System.Text;

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
        /// </summary>
        /// <returns>一個包含所有 Veeam 備份工作階段的 HTML 表格。</returns>
        [HttpGet("GetVeeamBackupSessionsHTML")]
        [Produces("text/html")]
        public async Task<IActionResult> GetVeeamBackupSessionsHTML()
        {
            try
            {
                // 1. 從 Service 層獲取純資料列表
                var sessions = await _veeamService.GetBackupSessionsAsync();

                // 2. Controller 負責 HTML 格式化
                var htmlTable = new StringBuilder();
                htmlTable.Append("<table style='border-collapse: collapse; width: 100%; font-family: sans-serif;'>");
                htmlTable.Append("<thead style='background-color: #f2f2f2;'><tr><th style='border: 1px solid #ddd; padding: 8px;'>Session Name</th><th style='border: 1px solid #ddd; padding: 8px;'>Status</th><th style='border: 1px solid #ddd; padding: 8px;'>End Time</th><th style='border: 1px solid #ddd; padding: 8px;'>Veeam Server</th><th style='border: 1px solid #ddd; padding: 8px;'>Update Time</th></tr></thead>");
                htmlTable.Append("<tbody>");

                foreach (var session in sessions)
                {
                    // 根據狀態設定顏色
                    string statusColor = session.Status?.ToLower() switch
                    {
                        "success" => "green",
                        "failed" => "red",
                        "warning" => "orange",
                        _ => "black"
                    };

                    htmlTable.Append("<tr>");
                    htmlTable.Append($"<td style='border: 1px solid #ddd; padding: 8px;'>{session.VmsSessionName}</td>");
                    htmlTable.Append($"<td style='border: 1px solid #ddd; padding: 8px; color: {statusColor};'>{session.Status}</td>");
                    htmlTable.Append($"<td style='border: 1px solid #ddd; padding: 8px;'>{session.SessionEndTime}</td>");
                    htmlTable.Append($"<td style='border: 1px solid #ddd; padding: 8px;'>{session.VeeamServer}</td>");
                    htmlTable.Append($"<td style='border: 1px solid #ddd; padding: 8px;'>{session.UpdateTime}</td>");
                    htmlTable.Append("</tr>");
                }

                htmlTable.Append("</tbody></table>");
                var utf8Encoding = new UTF8Encoding(false);

                // 3. 回傳 ContentResult
                return Content(htmlTable.ToString(), "text/html", utf8Encoding);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 GetVeeamBackupSessionsHTML 時發生未預期的錯誤。");
                return StatusCode(500, "生成 Veeam 備份報告時發生內部伺服器錯誤。");
            }
        }
    }
}
