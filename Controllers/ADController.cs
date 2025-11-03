using API.Classes.Reporting;
using API.DataModels;
using API.DataModels.FEB_CMS;
using API.DataModels.LDAP;
using API.Services.FEB_CMS;
using API.Services.LDAP;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [SuppressMessage("SonarLint", "S6667", Justification = "")]
    [SuppressMessage("SonarLint", "S125", Justification = "")]
    public class ADController : ControllerBase
    {
        private readonly ILogger<ADController> _logger;   // 預設 Logger (會有 ADController 類別名稱)
        private readonly ILdapService _ldapService;
        private readonly IExcelService _excelService;
        private readonly IFebCmsUserService _febCmsUserService;

        /// <summary>
        /// 初始化 ADController 的新執行個體，這是處理 Active Directory 相關 API 請求的進入點。
        /// </summary>
        /// <remarks>
        /// 此 Controller 遵循依賴注入模式，負責接收 HTTP 請求，並將核心業務邏輯委派給注入的服務層 (LdapService, FebCmsUserService) 進行處理。
        /// 它同時使用 ILoggerFactory 來創建一個特定名稱的 Logger 實例，用於記錄特定的歷史或稽核事件。
        /// </remarks>
        /// <param name="logger">用於記錄 ADController 本身操作事件與錯誤的標準記錄器。</param>
        /// <param name="ldapService">負責處理所有 LDAP 相關業務邏輯的服務實例。</param>
        /// <param name="excelService">通用工具、設定模型與基礎設施服務。</param>
        /// <param name="febCmsUserService">負責處理 FebCms 使用者管理相關業務邏輯的服務實例。</param>
        public ADController(
            ILogger<ADController> logger,
            ILdapService ldapService,
            IExcelService excelService,
            IFebCmsUserService febCmsUserService
            )
        {
            _logger = logger;
            _ldapService = ldapService;
            _excelService = excelService;
            _febCmsUserService = febCmsUserService;
        }

        /// <summary>
        /// 使用者密碼驗證。
        /// </summary>
        /// <param name="username">使用者帳號 (sAMAccountName)</param>
        /// <param name="password">使用者密碼</param>
        /// <returns>如果驗證成功則回傳 true，否則回傳 false。</returns>
        [HttpGet("UserLdapAuth")]
        public async Task<IActionResult> UserLdapAuth(string username, string password)
        {
            try
            {
                bool isAuthenticated = await _ldapService.AuthenticateLdapUserAsync(username, password);

                // 這會回傳一個 HTTP 200，內容為 true 或 false 的 JSON
                return Ok(isAuthenticated);
            }
            catch (Exception ex)
            {
                // 如果 Service 層發生任何無法處理的錯誤，統一在此捕捉並回傳 500 錯誤。
                _logger.LogError(ex, "執行 UserLdapAuth 時發生未預期的錯誤。");
                return StatusCode(500, $"驗證作業時發生未預期的錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 使用者LDAP 389驗證 (不輸入帳號密碼時, 預設驗證fsceipsys)。
        /// </summary>
        /// <remarks>
        /// 如果不提供帳號密碼，將使用系統預設的服務帳號(fsceipsys)進行驗證。
        /// 使用者名稱必須是完整的識別名 (Distinguished Name)，當前固定：CN=...,OU=...,DC=...,DC=...,DC=...。
        /// </remarks>
        /// <param name="distinguishedName">要驗證的使用者 DN。</param>
        /// <param name="password">使用者密碼。</param>
        /// <returns>一個包含操作結果的通用 JSON 物件。</returns>
        [HttpGet("LdapAuth")]
        [ProducesResponseType(typeof(SimpleApiResponse), 200)]
        [ProducesResponseType(typeof(SimpleApiResponse), 400)]
        public async Task<IActionResult> LdapAuth(string distinguishedName = "", string password = "")
        {
            try
            {
                var result = await _ldapService.AuthenticateDirectLdapUserAsync(distinguishedName, password);

                if (result.IsSuccess)
                {
                    return Ok(result);
                }
                else
                {
                    // 如果服務層回報了業務邏輯上的錯誤（例如 DN 格式不對、驗證失敗），則回傳 400 Bad Request。
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                // 如果 Service 層發生了無法處理的嚴重錯誤，統一在此捕捉並回傳 500 錯誤。
                _logger.LogError(ex, "執行 LdapAuth 時發生未預期的錯誤。");
                return StatusCode(500, new SimpleApiResponse { IsSuccess = false, Message = $"伺服器內部發生未預期的錯誤: {ex.Message}" });
            }
        }

        /// <summary>
        /// 使用者LDAP SSL 636驗證 (不輸入帳號密碼時, 預設驗證fsceipsys)。
        /// </summary>
        /// <param name="distinguishedName">要驗證的使用者 DN。</param>
        /// <param name="password">使用者密碼。</param>
        [HttpGet("LdapSSLAuth")]
        public IActionResult LdapSSLAuth(string distinguishedName = "", string password = "")
        {
            return Ok($"此功能尚未實作。");
        }

        /// <summary>
        /// 對即將到期的使用者觸發郵件通知(要通知的清單在appsetting中)。
        /// </summary>
        /// <remarks>
        /// 此 API 端點會呼叫後端的 LdapService 來執行核心業務邏輯。
        /// 服務層會從 appsettings.json 讀取要檢查的帳號清單、密碼有效期等設定，
        /// 計算每個帳號的密碼到期日，並對即將到期的使用者觸發郵件通知。
        /// <br/>
        /// <b>偵錯提示：</b> 您仍然可以使用 `cmd` 命令 `net user %USERNAME% /domain` 來手動查詢單一帳號的到期資訊。
        /// </remarks>
        /// <returns>
        /// <b>成功時：</b> 回傳 HTTP 200 OK，內容為一個 JSON 陣列，其中包含每個被檢查使用者密碼狀態的詳細物件 (<see cref="LdapUserPasswordStatus"/>)。
        /// <br/>
        /// <b>失敗時：</b> 回傳 HTTP 500 Internal Server Error，並在回應主體中附帶錯誤訊息。
        /// </returns>
        [HttpGet("UserPwdLastSetCheck")]
        [ProducesResponseType(typeof(List<LdapUserPasswordStatus>), 200)] // 宣告成功時的回傳類型
        [ProducesResponseType(typeof(string), 500)] // 宣告失敗時的回傳類型
        public async Task<IActionResult> UserPwdLastSetCheck()
        {
            try
            {
                var results = await _ldapService.CheckPasswordStatusAndNotifyAsync();
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 UserPwdLastSetCheck 時發生未預期的錯誤");
                return StatusCode(500, $"An unexpected error occurred: {ex.Message}");
            }
        }

        /// <summary>
        /// 查詢單一員工的密碼到期日(LINE Bot專案有參考到)。
        /// </summary>
        /// <param name="adAccount">要查詢的使用者帳號 (sAMAccountName) 或通用名稱 (cn)。</param>
        /// <returns>
        /// <b>成功時：</b> 回傳 HTTP 200 OK，內容為包含該使用者密碼狀態的 JSON 物件 (<see cref="LdapUserPasswordStatus"/>)。
        /// <br/>
        /// <b>找不到時：</b> 回傳 HTTP 404 Not Found。
        /// </returns>
        [HttpGet("UserPwdLastSetCheckOne")]
        [ProducesResponseType(typeof(LdapUserPasswordStatus), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> UserPwdLastSetCheckOne(string adAccount)
        {
            try
            {
                var result = await _ldapService.GetUserPasswordStatusAsync(adAccount);

                if (result == null)
                {
                    // 如果服務層回傳 null，代表找不到使用者，回傳標準的 404 錯誤。
                    return NotFound($"在 Active Directory 中找不到帳號或姓名為 '{adAccount}' 的使用者。");
                }

                // 使用 StringBuilder 或字串插值，組合成原始的格式
                string responseString = $"• 姓名： {result.User.CommonName}" +
                                        $"\n• 帳號： {result.User.SamAccountName}" +
                                        $"\n• 密碼最大有效期： {result.PasswordMaxAgeDays?.ToString() ?? "N/A"}天" +
                                        $"\n• 密碼最後設定日期： {result.PasswordLastSet?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"}" +
                                        $"\n• 密碼到期日期： {result.PasswordExpiresOn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"}" +
                                        $"\n• 密碼將於幾天後到期？： {result.DaysUntilExpiration?.ToString() ?? "N/A"}";

                // 回傳格式化後的字串，維持 API 合約不變
                return Ok(responseString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 UserPwdLastSetCheckOne 時發生未預期的錯誤。");
                return StatusCode(500, $"查詢單一使用者時發生未預期的錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 檢查 FEB_CMS User 資料是否存在於 AD 中，(1)找出離職未移除的並更新Status (2)找出職稱異動的並更新。
        /// </summary>
        /// <returns>一個包含同步作業詳細結果的 JSON 物件。</returns>
        [HttpGet("CheckFebCmsUserFromAD")]
        [ProducesResponseType(typeof(FebCmsUserSyncResult), 200)]
        public async Task<IActionResult> CheckFebCmsUserFromAD()
        {
            try
            {
                var result = await _febCmsUserService.SyncUsersFromAdAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 CheckFebCmsUserFromAD 時發生未預期的錯誤。");
                return StatusCode(500, new FebCmsUserSyncResult { Message = $"同步作業時發生未預期的錯誤: {ex.Message}" });
            }
        }

        /// <summary>
        /// 將AD(LDAP) Users帳號資訊(主要是群組隸屬、員工編號)紀錄於資料庫，以便後續比對或證據。
        /// </summary>
        /// <returns>一個包含同步作業詳細結果的 JSON 物件。</returns>
        [HttpGet("SyncAdUsersToDatabase")]
        public async Task<IActionResult> SyncAdUsersToDatabase()
        {
            try
            {
                var result = await _ldapService.SyncUsersToDatabaseAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                // 如果 Service 層發生任何無法處理的錯誤，統一在此捕捉並回傳 500 錯誤。
                _logger.LogError(ex, "執行 SyncAdUsersToDatabase 時發生未預期的錯誤。");
                return StatusCode(500, $"同步作業時發生未預期的錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// (HTML版)取得 AD(LDAP) User 帳號資訊及其密碼到期日。
        /// </summary>
        /// <returns>一個包含所有使用者資訊的 HTML 表格。</returns>
        [HttpGet("GetAdUserInfoHTML")]
        [Produces("text/html")] // 明確宣告回傳類型
        public async Task<IActionResult> GetAdUserInfoHTML()
        {
            _logger.LogInformation("開始生成 AD 使用者資訊 HTML 報告...");
            try
            {
                // 1. 從 Service 層獲取純資料列表
                var userStatusList = await _ldapService.GetAllUsersWithPasswordStatusAsync();

                // 2. 生成 HTML 表格
                var htmlTable = new StringBuilder();
                htmlTable.Append("<table style='border-collapse: collapse; width: 70%;'>");
                htmlTable.Append("<thead>" +
                    "<tr style='border: 1px solid black; text-align: center;'>" +
                    "<th style='border: 1px solid black; padding: 5px;'>啟用狀態</th>" +
                    "<th style='border: 1px solid black; padding: 5px;'>Common Name</th>" +
                    "<th style='border: 1px solid black; padding: 5px;'>Email</th>" +
                    "<th style='border: 1px solid black; padding: 5px;'>Street Address</th>" + // 員工編號
                    "<th style='border: 1px solid black; padding: 5px;'>Department</th>" +
                    "<th style='border: 1px solid black; padding: 5px;'>密碼到期日</th>" +
                    "</tr>" +
                    "</thead>");
                htmlTable.Append("<tbody>");

                // 按部門降序排序
                var sortedUserStatusList = userStatusList.OrderByDescending(status => status.User?.Department).ToList();

                foreach (var status in sortedUserStatusList)
                {
                    // 使用新模型中的 IsActive 屬性
                    string isActiveDisplay = status.User?.IsActive == "Active" ? "啟用" : "停用";

                    htmlTable.Append("<tr style='border: 1px solid black; text-align: center;'>");
                    htmlTable.Append($"<td style='border: 1px solid black; padding: 5px;'>{isActiveDisplay}</td>");
                    htmlTable.Append($"<td style='border: 1px solid black; padding: 5px;'>{status.User?.CommonName ?? "N/A"}</td>");
                    htmlTable.Append($"<td style='border: 1px solid black; padding: 5px;'>{status.User?.Email ?? "N/A"}</td>");
                    htmlTable.Append($"<td style='border: 1px solid black; padding: 5px;'>{status.User?.StreetAddress ?? "N/A"}</td>");
                    htmlTable.Append($"<td style='border: 1px solid black; padding: 5px;'>{status.User?.Department ?? "N/A"}</td>");
                    htmlTable.Append($"<td style='border: 1px solid black; padding: 5px;'>{status.PasswordExpiresOn?.ToString("yyyy-MM-dd") ?? "N/A"}</td>");
                    htmlTable.Append("</tr>");
                }
                htmlTable.Append("</tbody></table>");

                _logger.LogInformation("成功生成 AD 使用者資訊 HTML 報告。");

                // 3. 回傳 ContentResult
                var utf8Encoding = new UTF8Encoding(false); // 禁用BOM
                return Content(htmlTable.ToString(), "text/html", utf8Encoding);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 GetAdUserInfoHTML 時發生未預期的錯誤。");
                // 可以考慮回傳一個簡單的錯誤 HTML 頁面或純文字錯誤訊息
                return StatusCode(500, $"生成報告時發生未預期的錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 在 Active Directory 中建立一個新的使用者帳號。
        /// </summary>
        /// <param name="newUserModel">從請求主體 (Request Body) 傳入的 LdapUser 物件，包含新使用者的所有必要資訊。</param>
        /// <returns>一個包含建立作業詳細結果的 JSON 物件。</returns>
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("UserLdapCreate")]
        [ProducesResponseType(typeof(AdUserCreateResult), 200)] // 宣告成功時的回傳類型
        [ProducesResponseType(typeof(AdUserCreateResult), 400)] // 宣告失敗時的回傳類型
        public async Task<IActionResult> CreateLdapUser([FromBody] LdapUser newUserModel)
        {
            if (newUserModel == null)
            {
                return BadRequest(new AdUserCreateResult { IsSuccess = false, Message = "請求主體不得為空。" });
            }

            try
            {
                // Controller 的職責：驗證輸入，呼叫服務，並根據結果回傳。
                var result = await _ldapService.CreateLdapUserAsync(newUserModel);

                if (result.IsSuccess)
                {
                    return Ok(result);
                }
                else
                {
                    // 如果服務層回報了業務邏輯上的錯誤（例如部門不存在），則回傳 400 Bad Request。
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                // 如果 Service 層發生了無法處理的嚴重錯誤（例如 LDAP 伺服器無法連線），統一在此捕捉並回傳 500 錯誤。
                _logger.LogError(ex, "呼叫 CreateLdapUser 時發生未預期的錯誤。");
                return StatusCode(500, new AdUserCreateResult { IsSuccess = false, Message = $"伺服器內部發生未預期的錯誤: {ex.Message}" });
            }
        }

        /// <summary>
        /// 更新 Active Directory 中指定使用者的資訊。
        /// </summary>
        /// <remarks>
        /// 僅會更新請求主體中提供的非 null 欄位。
        /// </remarks>
        /// <param name="username">要更新的使用者帳號 (sAMAccountName)。</param>
        /// <param name="updateData">從請求主體 (Request Body) 傳入的 AdUserUpdateModel 物件，包含要更新的欄位及其新值。</param>
        /// <returns>一個包含操作結果的通用 JSON 物件。</returns>
        [HttpPut("UserLdapEdit/{username}")] // 使用 HttpPut 並將 username 放入路由參數
        [ProducesResponseType(typeof(SimpleApiResponse), 200)]
        [ProducesResponseType(typeof(SimpleApiResponse), 400)] // 用於業務邏輯錯誤，例如找不到使用者
        [ProducesResponseType(typeof(SimpleApiResponse), 404)] // 如果路由中的 username 找不到 (雖然我們的邏輯會在 400 回傳)
        public async Task<IActionResult> UpdateLdapUser(string username, [FromBody] AdUserUpdateModel updateData)
        {
            if (updateData == null)
            {
                return BadRequest(new SimpleApiResponse { IsSuccess = false, Message = "請求主體不得為空。" });
            }

            try
            {
                var result = await _ldapService.UpdateLdapUserAsync(username, updateData);

                if (result.IsSuccess)
                {
                    return Ok(result);
                }
                else
                {
                    // 根據服務層回傳的錯誤訊息判斷狀態碼可能更精確，
                    // 但為了簡單起見，我們先統一回傳 400 Bad Request 代表業務邏輯失敗。
                    // 如果需要區分 "找不到使用者(404)" vs "LDAP 修改失敗(400)"，可以在服務層回傳更詳細的錯誤碼。
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 UpdateLdapUser 時發生未預期的錯誤。 Username: {Username}", username);
                return StatusCode(500, new SimpleApiResponse { IsSuccess = false, Message = $"伺服器內部發生未預期的錯誤: {ex.Message}" });
            }
        }

        /// <summary>
        /// 產生並下載 AD 帳號清查的 Excel 報表。
        /// </summary>
        [HttpGet("AdAuditReport")]
        public IActionResult GenerateAdAuditReport()
        {
            _logger.LogInformation("接收到 AD 帳號清查報表生成請求。");

            try
            {
                // 1. 從 LdapService (業務邏輯層) 獲取資料
                var reportData = _ldapService.GenerateAuditReportDataAsync().Result;

                // 2. 將生成 Excel 的任務委派給 ExcelService (通用工具層)
                byte[] excelFile = _excelService.CreateAdAuditReport(reportData);

                // 3. 回傳檔案
                string fileName = $"AD_Users_Audit_Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                _logger.LogInformation("Excel 檔案已由工具層生成，檔名為 '{FileName}'，準備回傳。", fileName);

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "在 {MethodName} 端點處理請求時發生未預期錯誤。", nameof(GenerateAdAuditReport));
                return StatusCode(500, $"產生報表時發生內部錯誤: {ex.Message}");
            }
        }
    }
}