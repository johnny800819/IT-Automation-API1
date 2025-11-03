using API.Classes;
using API.Classes.LDAP;
using API.DataModels;
using API.DataModels.LDAP;
using API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;
using Org.BouncyCastle.Asn1.Cms;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;

namespace API.Services.LDAP
{
    /// <summary>
    /// 實現 LDAP 相關業務邏輯的服務
    /// </summary>
    public class LdapService : ILdapService
    {
        private readonly LdapConfig _ldapConfig;
        private readonly AdAuditConfig _adAuditConfig;
        private readonly IConfiguration _config;
        private readonly MISContext _misContext;
        private readonly IMailSend _mailSend;
        private readonly ILogger<LdapService> _logger;
        private readonly ILogger _ldapHistoryLogger; // 用於記錄AD同步至DB的專用 Logger

        /// <summary>
        /// LdapService 的建構函式，透過依賴注入初始化所有必要的服務。
        /// </summary>
        /// <param name="ldapConfigOptions">LDAP 連線設定。</param>
        /// <param name="adAuditConfig">AD audit 連線設定。</param>
        /// <param name="config">應用程式的通用設定。</param>
        /// <param name="misContext">MIS 資料庫的 Entity Framework Context。</param>
        /// <param name="mailSend">郵件發送服務。</param>
        /// <param name="logger">此服務專用的日誌記錄器。</param>
        /// <param name="loggerFactory">用於創建自訂名稱 Logger 的工廠。</param>
        public LdapService(
            IOptions<LdapConfig> ldapConfigOptions,
            IOptions<AdAuditConfig> adAuditConfig,
            IConfiguration config,
            MISContext misContext,
            IMailSend mailSend,
            ILogger<LdapService> logger,
            ILoggerFactory loggerFactory)
        {
            _ldapConfig = ldapConfigOptions.Value;
            _adAuditConfig = adAuditConfig.Value;
            _config = config;
            _misContext = misContext;
            _mailSend = mailSend;
            _logger = logger;
            _ldapHistoryLogger = loggerFactory.CreateLogger("LdapUserHistoryLogger"); // 使用 loggerFactory 創建指定的自訂名稱 Logger
        }

        /// <inheritdoc/>
        public async Task<List<LdapUserPasswordStatus>> CheckPasswordStatusAndNotifyAsync()
        {
            _logger.LogInformation("開始執行密碼到期日檢查...");

            try
            {
                return await Task.Run(() =>
                {
                    // --- 1. 讀取設定 ---
                    var accountsToCheck = _config.GetSection("AdControllerConfig:UserPwdLastSetCheckAccount").Get<string[]>();
                    if (accountsToCheck == null || !accountsToCheck.Any())
                    {
                        _logger.LogWarning("在 appsettings.json 中找不到需要檢查的帳號清單 (AdControllerConfig:UserPwdLastSetCheckAccount)。");
                        return new List<LdapUserPasswordStatus>();
                    }

                    var regularMailMapping = _config.GetSection("AdControllerConfig:TranslateMailDict").Get<Dictionary<string, string>>()
                                           ?? new Dictionary<string, string>();
                    var mailMapping = new Dictionary<string, string>(regularMailMapping, StringComparer.OrdinalIgnoreCase);

                    int passwordMaxAgeDays = _config.GetValue("AdControllerConfig:PasswordMaxAgeDays", 90);
                    int notificationDays = _config.GetValue("AdControllerConfig:NotificationForwardDays", 14);

                    // --- 2. 建立 LDAP 連線與查詢 ---
                    using var ldapConnection = new LdapConnection();
                    ldapConnection.Connect(_ldapConfig.Host, LdapConnection.DefaultPort);
                    ldapConnection.Bind(_ldapConfig.AdminBaseDC, _ldapConfig.AdminPassword);

                    string[] attributesToFetch = { "pwdLastSet", "sAMAccountName", "userPrincipalName", "mail", "userAccountControl", "cn", "displayName" };
                    var filterBuilder = new StringBuilder("(|");
                    foreach (var account in accountsToCheck)
                    {
                        filterBuilder.Append($"(sAMAccountName={account})");
                    }
                    filterBuilder.Append(')');

                    var searchResults = ldapConnection.Search(
                        _ldapConfig.BaseDC, LdapConnection.ScopeSub, filterBuilder.ToString(), attributesToFetch, false);

                    // --- 3. 處理查詢結果並建立狀態列表 ---
                    var results = new List<LdapUserPasswordStatus>();
                    var notificationsToSend = new List<(LdapUserPasswordStatus status, string email)>(); // 暫存需要通知的項目

                    while (searchResults.HasMore())
                    {
                        var entry = searchResults.Next();
                        if (entry == null) continue;

                        // 1. 呼叫純計算方法，獲取基礎狀態
                        var status = CalculateAndBuildPasswordStatus(entry, passwordMaxAgeDays);
                        results.Add(status); // 將計算結果加入列表

                        // 2. 自行判斷是否需要通知
                        bool isExpiringSoon = status.DaysUntilExpiration.HasValue &&
                                              status.DaysUntilExpiration.Value <= notificationDays;

                        if (isExpiringSoon)
                        {
                            // 3. 自行決定通知 Email (優先使用對應表)
                            string notificationEmail = "";
                            if (mailMapping.TryGetValue(status.User.SamAccountName, out var mappedMail))
                            {
                                notificationEmail = mappedMail;
                            }
                            else
                            {
                                string potentialEmail = status.User.Email;
                                if (string.IsNullOrEmpty(potentialEmail))
                                {
                                    // UPN 通常是用來登入的帳號，格式常常類似於電子郵件地址（例如 username@yourdomain.com）
                                    potentialEmail = status.User.UserPrincipalName;
                                }
                                notificationEmail = potentialEmail ?? "";
                            }

                            // 如果需要通知且 Email 有效，則加入待發送列表
                            if (!string.IsNullOrEmpty(notificationEmail))
                            {
                                notificationsToSend.Add((status, notificationEmail));
                            }
                            else
                            {
                                _logger.LogWarning("帳號 {Account} 密碼即將到期，但找不到可用的通知信箱。", status.User.SamAccountName);
                            }
                        }
                    }

                    // 4. 迴圈結束後，統一發送郵件
                    foreach (var notification in notificationsToSend)
                    {
                        SendNotificationEmail(notification.status, notification.email);
                    }

                    _logger.LogInformation("密碼到期日檢查執行完畢。共處理 {Count} 個帳號。\n", results.Count);
                    return results;
                });
            }
            catch (LdapException ex)
            {
                _logger.LogError(ex, "LDAP 操作失敗: {ErrorMessage}", ex.LdapErrorMessage);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行密碼檢查時發生未預期的錯誤。");
                throw;
            }
        }

        /// <summary>
        /// 處理單一 LDAP 使用者條目，計算其密碼狀態，並建構成 LdapUserPasswordStatus 物件。
        /// </summary>
        /// <remarks>
        /// 此方法整合了從 LdapEntry 提取屬性以及計算密碼到期日的完整邏輯。
        /// </remarks>
        private LdapUserPasswordStatus CalculateAndBuildPasswordStatus(LdapEntry entry, int passwordMaxAgeDays)
        {
            var status = new LdapUserPasswordStatus
            {
                User = new LdapUser
                {
                    SamAccountName = entry.GetSafeAttribute("sAMAccountName"),
                    DisplayName = entry.GetSafeAttribute("displayName"),
                    CommonName = entry.GetSafeAttribute("cn"),
                    Email = entry.GetSafeAttribute("mail"),
                    UserPrincipalName = entry.GetSafeAttribute("userPrincipalName"),
                    Surname = entry.GetSafeAttribute("sn"),
                    GivenName = entry.GetSafeAttribute("givenName"),
                    TelephoneNumber = entry.GetSafeAttribute("telephoneNumber"),
                    Title = entry.GetSafeAttribute("title"),
                    Department = entry.GetSafeAttribute("department"),
                    Company = entry.GetSafeAttribute("company"),
                    Description = entry.GetSafeAttribute("description"),
                    StreetAddress = entry.GetSafeAttribute("streetAddress"),
                    DistinguishedName = entry.Dn
                },
                IsPasswordNeverExpires = entry.IsPasswordNeverExpires(),
                PasswordMaxAgeDays = passwordMaxAgeDays
            };

            // 判斷帳戶是否停用
            var uacStr = entry.GetSafeAttribute("userAccountControl");
            int.TryParse(uacStr, out var uac);
            status.User.IsActive = (uac & 0x0002) == 0 ? "Active" : "Inactive";

            // 計算密碼到期日
            long.TryParse(entry.GetSafeAttribute("pwdLastSet"), out long pwdLastSetValue);

            // 檢查是否為有效的 Win32 FileTime (pwdLastSet > 0) 且密碼非永不過期
            if (pwdLastSetValue > 0 && !status.IsPasswordNeverExpires)
            {
                // 1. 計算密碼最後設定日期 (以 1601 年 1 月 1 日為基準)
                DateTime pwdLastSetDateTimeUtc = DateTime.FromFileTimeUtc(pwdLastSetValue);

                // 2. 計算密碼到期日期
                DateTime passwordExpirationDateUtc = pwdLastSetDateTimeUtc.AddDays(passwordMaxAgeDays);

                // 3. 將時區從 UTC 轉換為當地時區
                status.PasswordLastSet = pwdLastSetDateTimeUtc.ToLocalTime();
                status.PasswordExpiresOn = passwordExpirationDateUtc.ToLocalTime();

                // 4. 計算日期差異(剩餘天數)
                TimeSpan difference = status.PasswordExpiresOn.Value - DateTime.Now;

                // 5. 取得差異的天數(剩餘天數)
                status.DaysUntilExpiration = (int)Math.Floor(difference.TotalDays);
            }

            return status;
        }

        /// <summary>
        /// 根據使用者的密碼狀態，發送通知郵件。
        /// </summary>
        private void SendNotificationEmail(LdapUserPasswordStatus status, string notificationEmail)
        {
            if (string.IsNullOrEmpty(notificationEmail))
            {
                // 理論上不應該執行到這裡
                _logger.LogError("SendNotificationEmail 方法被呼叫，但傳入了無效的 notificationEmail。Account: {Account}", status.User?.SamAccountName ?? "N/A");
                return;
            }

            string subject = $"網域(AD)帳戶密碼將於 {status.DaysUntilExpiration} 天後到期，提醒變更密碼。";

            string responseDetails = $@"<ul>
                                        <li>帳號： {status.User.SamAccountName}</li>
                                        <li>信箱： {notificationEmail}</li>
                                        <li>密碼最大有效期： {status.PasswordMaxAgeDays}天</li>
                                        <li>密碼最後設定日期： {status.PasswordLastSet:yyyy-MM-dd HH:mm:ss}</li>
                                        <li>密碼到期日期： {status.PasswordExpiresOn:yyyy-MM-dd HH:mm:ss}</li>
                                        <li>密碼將於幾天後到期？： {status.DaysUntilExpiration}</li>
                                        <li>密碼永不過期： {status.IsPasswordNeverExpires}</li>
                                      </ul>";

            string text1 = $@"<p><span style='color: red;'>您的網域(AD)帳戶密碼將於{status.DaysUntilExpiration}天後到期，提醒變更密碼</span>，<br>
                              逾期未變更將影響局內<ins>電腦登入</ins>、<ins>M356信箱登入</ins>以及<ins>行動辦公室</ins>等服務異常。<br>
                              其餘資訊如下，請參考：</p>";

            string body = text1 + responseDetails;

            _mailSend.MailSendExecute(subject, body, notificationEmail);
        }

        /// <inheritdoc/>
        public async Task<AdUserSyncToDbResult> SyncUsersToDatabaseAsync()
        {
            _ldapHistoryLogger.LogInformation("同步至資料庫開始");
            var result = new AdUserSyncToDbResult();

            try
            {
                // 使用 Task.Run 處理同步的 LDAP 操作
                // 指「（主執行緒）會在這裡暫停，並且等待 Task.Run 裡面的所有工作全部完成。直到它完成，並且把 adUsers 這個 List 回傳之後，才會繼續往下執行後面的資料庫比對程式碼。」
                // 它額外的好處是，在等待的過程中，不會佔用寶貴的伺服器請求執行緒，讓您的 API 能夠同時服務更多的使用者。
                var adUsers = await Task.Run(() =>
                {
                    using var ldapConnection = new LdapConnection();
                    ldapConnection.Connect(_ldapConfig.Host, LdapConnection.DefaultPort);
                    ldapConnection.Bind(_ldapConfig.AdminBaseDC, _ldapConfig.AdminPassword);

                    string searchFilter = "(objectClass=user)";
                    string[] attributesToFetch = { "cn", "department", "mail", "memberOf", "streetAddress", "userAccountControl" };

                    var searchResults = ldapConnection.Search(_ldapConfig.BaseDC, LdapConnection.ScopeSub, searchFilter, attributesToFetch, false);

                    var tempAdUsers = new List<LdapUserHistory>();
                    while (searchResults.HasMore())
                    {
                        LdapEntry entry = searchResults.Next();
                        if (entry == null) continue;

                        // memberOf (隸屬群組) 這個屬性是「多值」的，所以沒有使用GetSafeAttribute(單值)方法取值
                        var attributes = entry.GetAttributeSet();
                        string memberOfValue = string.Empty;
                        if (attributes.ContainsKey("memberOf"))
                        {
                            // 只有在確定存在的情況下，才去執行 GetAttribute 和後續操作
                            memberOfValue = string.Join(", ", entry.GetAttribute("memberOf").StringValueArray
                                ?.Select(m => m.Split(',')[0].Replace("CN=", "")) ?? Array.Empty<string>());
                        }

                        var uacStr = entry.GetSafeAttribute("userAccountControl");
                        int.TryParse(uacStr, out var uac);

                        // 判斷帳戶是否啟用 (ACCOUNTDISABLE flag is 0x0002)
                        var isActive = (uac & 0x0002) == 0 ? "Active" : "Inactive";

                        var adUser = new LdapUserHistory
                        {
                            IsActive = isActive,
                            CommonName = entry.GetSafeAttribute("cn"),
                            Department = entry.GetSafeAttribute("department"),
                            StreetAddress = entry.GetSafeAttribute("streetAddress"),
                            Email = entry.GetSafeAttribute("mail"),
                            MemberOf = memberOfValue,
                            UpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        };
                        tempAdUsers.Add(adUser);
                    }
                    return tempAdUsers;
                });

                result.TotalAdUsersProcessed = adUsers.Count;

                // --- 資料庫操作 ---
                await using var transaction = await _misContext.Database.BeginTransactionAsync();

                // 取得 DB 中每個使用者的最新一筆紀錄
                var dbUsers = await _misContext.LdapUserHistory
                    .GroupBy(u => u.CommonName)
                    .Select(g => g.OrderByDescending(u => u.UpdateTime).First())
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var user in adUsers)
                {
                    var existingUser = dbUsers.FirstOrDefault(u => u.CommonName == user.CommonName);

                    if (existingUser == null)
                    {
                        // 新增 (全新帳戶)
                        await _misContext.AddAsync(user);
                        result.InsertedCount++;
                    }
                    else
                    {
                        // 比對已存在帳戶，資料是否有變動
                        if (existingUser.IsActive != user.IsActive ||
                            existingUser.Department != user.Department ||
                            existingUser.MemberOf != user.MemberOf ||
                            existingUser.StreetAddress != user.StreetAddress ||
                            existingUser.Email != user.Email)
                        {
                            // 新增一筆歷史紀錄
                            await _misContext.AddAsync(user);
                            result.UpdatedCount++;
                        }
                    }
                }

                // 比對離職同仁
                var dbUserNames = dbUsers.Select(u => u.CommonName).ToHashSet();
                var adUserNames = adUsers.Select(u => u.CommonName).ToHashSet();
                // 找出所有存在於 dbUserNames 中，但不存在於 adUserNames 中的用戶名稱。
                var inactiveUserNames = dbUserNames.Except(adUserNames);

                if (inactiveUserNames.Any())
                {
                    var usersToUpdate = await _misContext.LdapUserHistory
                        .Where(u => inactiveUserNames.Contains(u.CommonName) && u.IsActive == "Active")
                        .ToListAsync();

                    foreach (var userToUpdate in usersToUpdate)
                    {
                        userToUpdate.IsActive = "inActive";
                        userToUpdate.UpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        result.InactivatedCount++;
                    }
                }

                await _misContext.SaveChangesAsync();
                await transaction.CommitAsync();

                result.Message = $"同步至資料庫完成，本次處理 AD 使用者共 {result.TotalAdUsersProcessed} 筆，資料庫新增 {result.InsertedCount} 筆，發現異動 {result.UpdatedCount} 筆，標記離職 {result.InactivatedCount} 筆。";
                _ldapHistoryLogger.LogInformation(result.Message);

                return result;
            }
            catch (LdapException ex)
            {
                _ldapHistoryLogger.LogError(ex, "LDAP 錯誤: {LdapErrorMessage}", ex.LdapErrorMessage);
                throw; // 重新拋出，讓 Controller 知道發生錯誤
            }
            catch (Exception ex)
            {
                _ldapHistoryLogger.LogError(ex, "保存資料時發生錯誤");
                throw;
            }
            finally
            {
                _ldapHistoryLogger.LogInformation("同步至資料庫結束\n");
            }
        }

        /// <inheritdoc/>
        public async Task<bool> AuthenticateLdapUserAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("驗證失敗：使用者名稱或密碼為空。");
                return false;
            }

            try
            {
                // 將所有同步的 LDAP 操作包裹在 Task.Run 中
                return await Task.Run(() =>
                {
                    using var connection = new LdapConnection();
                    connection.Connect(_ldapConfig.Host, LdapConnection.DefaultPort);

                    // --- 第一階段：管理者綁定 & 查找使用者 DN ---
                    connection.Bind(_ldapConfig.AdminBaseDC, _ldapConfig.AdminPassword);

                    // 直接精確查找使用者
                    string searchFilter = $"(&(objectClass=user)(sAMAccountName={username}))";
                    var searchResults = connection.Search(
                        _ldapConfig.BaseDC,
                        LdapConnection.ScopeSub,
                        searchFilter,
                        new[] { "dn" }, // 我們只需要 dn 這一個屬性
                        false);

                    string userDn = "";
                    if (searchResults.HasMore())
                    {
                        userDn = searchResults.Next().Dn;
                    }

                    if (string.IsNullOrWhiteSpace(userDn))
                    {
                        _logger.LogWarning("驗證失敗：在 AD 中找不到使用者 '{Username}'。", username);
                        return false;
                    }

                    // --- 第二階段：使用者綁定 & 驗證 ---
                    try
                    {
                        // 使用找到的 userDn 和使用者提供的密碼進行第二次綁定
                        connection.Bind(userDn, password);
                        if (connection.Bound)
                        {
                            _logger.LogInformation("使用者 '{Username}' 驗證成功。", username);
                            return true;
                        }
                    }
                    catch (LdapException ex)
                    {
                        // 這裡捕捉到的 LdapException 通常代表密碼錯誤
                        _logger.LogWarning(ex, "使用者 '{Username}' 驗證失敗：密碼錯誤或帳號問題。 LDAP 錯誤訊息: {LdapErrorMessage}", username, ex.ResultCode.ToString());
                        return false;
                    }

                    return false;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 AuthenticateLdapUserAsync 時發生未預期的錯誤。");
                // 重新拋出，讓 Controller 捕捉並回傳 500 錯誤
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<SimpleApiResponse> AuthenticateDirectLdapUserAsync(string distinguishedName, string password)
        {
            // --- 處理預設值 ---
            if (string.IsNullOrEmpty(distinguishedName))
            {
                distinguishedName = _config.GetValue<string>("AdControllerConfig:DirectAuthServiceAccount:DistinguishedName");
            }
            if (string.IsNullOrEmpty(password))
            {
                password = _config.GetValue<string>("AdControllerConfig:DirectAuthServiceAccount:Password");
            }
            string pattern = @"^CN=[^,]+,OU=[^,]+,DC=[^,]+,DC=[^,]+,DC=[^,]+$";
            if (!Regex.IsMatch(distinguishedName, pattern, RegexOptions.IgnoreCase))
            {
                _logger.LogWarning("直接綁定驗證失敗：提供的識別名 (DN) 格式不正確。DN: {DistinguishedName}", distinguishedName);
                return new SimpleApiResponse { IsSuccess = false, Message = "驗證失敗：提供的識別名 (DN) 格式不正確！" };
            }

            try
            {
                return await Task.Run(() =>
                {
                    using var connection = new LdapConnection();
                    connection.Connect(_ldapConfig.Host, LdapConnection.DefaultPort);

                    // --- 第一階段：使用管理員帳號，確認 DN 是否存在 ---
                    bool userExists = false;
                    try
                    {
                        connection.Bind(_ldapConfig.AdminBaseDC, _ldapConfig.AdminPassword);

                        var searchResults = connection.Search(distinguishedName, LdapConnection.ScopeBase, "(objectClass=*)", new[] { "dn" }, false);

                        // 將 Next() 包裹在 try-catch 中來處理 "No Such Object"
                        try
                        {
                            // 我們嘗試去拿取結果。這個操作會阻塞直到伺服器回應。
                            var entry = searchResults.Next();
                            // 如果上面這行程式碼沒有拋出例外，就代表物件確實存在。
                            userExists = true;
                        }
                        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
                        {
                            // 這是一個預期中的例外
                            // 我們成功捕捉到了伺服器回傳的 "No Such Object" 訊號。
                            // 這明確地告訴我們使用者不存在，所以我們將 userExists 保持為 false。
                            userExists = false;
                        }
                        // 任何其他的 LdapException 都會在這裡被忽略，並由外層的 catch 來處理。
                    }
                    catch (LdapException ex)
                    {
                        _logger.LogError(ex, "直接綁定驗證的第一階段（管理者綁定）失敗。");
                        throw;
                    }

                    // --- 根據第一階段的結果，決定下一步 ---
                    if (!userExists)
                    {
                        _logger.LogWarning("直接綁定驗證失敗：提供的識別名 (DN) 在目錄中不存在。DN: {DistinguishedName}", distinguishedName);
                        return new SimpleApiResponse { IsSuccess = false, Message = "驗證失敗：使用者不存在。" };
                    }

                    // --- 第二階段：DN 已確認存在，現在嘗試用使用者憑證綁定 ---
                    try
                    {
                        connection.Bind(distinguishedName, password);

                        string commonName = distinguishedName.Split(',')[0].Split('=')[1];
                        string successMessage = $"{commonName} Login Successful";
                        _logger.LogInformation("直接綁定驗證成功。DN: {DistinguishedName}", distinguishedName);

                        return new SimpleApiResponse { IsSuccess = true, Message = successMessage };
                    }
                    catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
                    {
                        _logger.LogWarning("直接綁定驗證失敗：密碼錯誤。DN: {DistinguishedName}", distinguishedName);
                        return new SimpleApiResponse { IsSuccess = false, Message = "驗證失敗：密碼錯誤。" };
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行直接綁定驗證時發生未預期的錯誤。DN: {DistinguishedName}", distinguishedName);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<LdapUserPasswordStatus> GetUserPasswordStatusAsync(string accountOrCommonName)
        {
            if (string.IsNullOrWhiteSpace(accountOrCommonName))
            {
                return null;
            }

            _logger.LogInformation("開始查詢單一使用者 '{AccountOrCn}' 的密碼狀態...", accountOrCommonName);

            try
            {
                return await Task.Run(() =>
                {
                    using var ldapConnection = new LdapConnection();
                    ldapConnection.Connect(_ldapConfig.Host, LdapConnection.DefaultPort);
                    ldapConnection.Bind(_ldapConfig.AdminBaseDC, _ldapConfig.AdminPassword);

                    // 建立 OR 條件的查詢
                    string searchFilter = $"(|(sAMAccountName={accountOrCommonName})(cn={accountOrCommonName}))";
                    string[] attributesToFetch = { "pwdLastSet", "sAMAccountName", "userPrincipalName", "mail", "userAccountControl", "cn", "displayName" };

                    var searchResults = ldapConnection.Search(_ldapConfig.BaseDC, LdapConnection.ScopeSub, searchFilter, attributesToFetch, false);

                    if (searchResults.HasMore())
                    {
                        var entry = searchResults.Next();
                        if (entry != null)
                        {
                            // 讀取設定
                            int passwordMaxAgeDays = _config.GetValue("AdControllerConfig:PasswordMaxAgeDays", 90);

                            var status = CalculateAndBuildPasswordStatus(entry, passwordMaxAgeDays);
                            _logger.LogInformation("成功查詢到使用者 '{AccountOrCn}' 的密碼狀態。", accountOrCommonName);
                            return status;
                        }
                    }

                    _logger.LogWarning("在 AD 中找不到使用者 '{AccountOrCn}'。", accountOrCommonName);
                    return null; // 找不到使用者，回傳 null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢單一使用者 '{AccountOrCn}' 時發生未預期的錯誤。", accountOrCommonName);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, LdapUser>> GetUsersInfoAsync(IEnumerable<string> accountNames)
        {
            // 檢查傳入的集合是否為 null 或空。(.Any() 就是「這個集合有東西嗎？」的意思，回傳布林值。)
            if (accountNames == null || !accountNames.Any())
            {
                return new Dictionary<string, LdapUser>(StringComparer.OrdinalIgnoreCase);
            }

            _logger.LogInformation("開始批次查詢 {Count} 個 AD 使用者的資訊。", accountNames.Count());
            var usersDictionary = new Dictionary<string, LdapUser>(StringComparer.OrdinalIgnoreCase);

            try
            {
                return await Task.Run(() =>
                {
                    using var connection = new LdapConnection();
                    connection.Connect(_ldapConfig.Host, LdapConnection.DefaultPort);
                    connection.Bind(_ldapConfig.AdminBaseDC, _ldapConfig.AdminPassword);

                    // 組合查詢條件
                    // • &：AND，全部條件都要符合（常用於單一查詢或多條件查詢）。 範例：$"(&(sAMAccountName={username}))"
                    // • |：OR， 符合任一條件即可（常用於多帳號查詢）。
                    var searchFilter = new StringBuilder("(|");
                    foreach (var username in accountNames)
                    {
                        searchFilter.Append($"(sAMAccountName={username})");
                    }
                    searchFilter.Append(")");

                    // 建立額外條件的物件 (超時設定)
                    LdapSearchConstraints searchConstraints = new LdapSearchConstraints();
                    searchConstraints.TimeLimit = 30000; // 30秒超時

                    // 查詢所有需要的屬性以填充 LdapUser 模型
                    string[] attributesToFetch = {
                        "sAMAccountName", "cn", "sn", "givenName", "userPrincipalName",
                        "displayName", "mail", "telephoneNumber", "title", "department",
                        "company", "description", "streetAddress"
                    };

                    var searchResults = connection.Search(
                        _ldapConfig.BaseDC, // 搜尋的基礎 DN
                        LdapConnection.ScopeSub, // 搜尋的範圍，這裡使用 SUB 表示子樹
                        searchFilter.ToString(), // 搜尋的過濾條件
                        attributesToFetch, // 返回的屬性，若設定 null 表示返回所有屬性
                        false, // 是否返回刪除的條目
                        searchConstraints
                    );

                    while (searchResults.HasMore())
                    {
                        try
                        {
                            var entry = searchResults.Next(); // 避免競態條件(就是避免AD那邊還沒查到資料時，程式跑太快就開始了)
                            if (entry == null) continue;

                            var user = new LdapUser
                            {
                                SamAccountName = entry.GetSafeAttribute("sAMAccountName"),
                                CommonName = entry.GetSafeAttribute("cn"),
                                Surname = entry.GetSafeAttribute("sn"),
                                GivenName = entry.GetSafeAttribute("givenName"),
                                UserPrincipalName = entry.GetSafeAttribute("userPrincipalName"),
                                DisplayName = entry.GetSafeAttribute("displayName"),
                                Email = entry.GetSafeAttribute("mail"),
                                TelephoneNumber = entry.GetSafeAttribute("telephoneNumber"),
                                Title = entry.GetSafeAttribute("title"),
                                Department = entry.GetSafeAttribute("department"),
                                Company = entry.GetSafeAttribute("company"),
                                Description = entry.GetSafeAttribute("description"),
                                StreetAddress = entry.GetSafeAttribute("streetAddress"),
                                DistinguishedName = entry.Dn
                            };

                            if (!string.IsNullOrEmpty(user.SamAccountName))
                            {
                                usersDictionary[user.SamAccountName] = user;
                            }
                        }
                        catch (LdapException ex)
                        {
                            _logger.LogError(ex, "在批次獲取使用者資訊的迴圈中發生 LDAP 錯誤。");
                            continue; // 忽略單一錯誤的條目，繼續處理下一個
                        }
                    }

                    _logger.LogInformation("您提供的清單中，共有 {FoundCount} 筆資料成功在AD中查詢 (您的清單數量為 {TotalCount} 個使用者資訊。", usersDictionary.Count, accountNames.Count());
                    return usersDictionary;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 GetUsersInfoAsync 時發生未預期的錯誤。");
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<List<LdapUserPasswordStatus>> GetAllUsersWithPasswordStatusAsync()
        {
            _logger.LogInformation("開始獲取所有 AD 使用者的資訊及其密碼狀態...");
            var results = new List<LdapUserPasswordStatus>();

            try
            {
                // 將所有同步的 LDAP 操作包裹在 Task.Run 中
                results = await Task.Run(() =>
                {
                    var userStatusList = new List<LdapUserPasswordStatus>();
                    using var connection = new LdapConnection();
                    connection.Connect(_ldapConfig.Host, LdapConnection.DefaultPort);
                    connection.Bind(_ldapConfig.AdminBaseDC, _ldapConfig.AdminPassword);

                    // 1. 查詢 AD 所有使用者資料
                    string searchFilter = "(objectClass=user)";
                    // 需要獲取 CalculateAndBuildPasswordStatus 所需的所有屬性，再加上 userAccountControl
                    string[] attributesToFetch = {
                        "sAMAccountName", "cn", "sn", "givenName", "userPrincipalName",
                        "displayName", "mail", "telephoneNumber", "title", "department",
                        "company", "description", "streetAddress", "pwdLastSet", "userAccountControl"
                    };

                    // 加入超時設定
                    LdapSearchConstraints searchConstraints = new LdapSearchConstraints();
                    searchConstraints.TimeLimit = 30000; // 30秒超時

                    var searchResults = connection.Search(
                        _ldapConfig.BaseDC,
                        LdapConnection.ScopeSub,
                        searchFilter,
                        attributesToFetch,
                        false,
                        searchConstraints
                    );

                    // 讀取計算所需的設定
                    int passwordMaxAgeDays = _config.GetValue("AdControllerConfig:PasswordMaxAgeDays", 90);
                    int notificationDays = _config.GetValue("AdControllerConfig:NotificationForwardDays", 14);

                    // 2. 遍歷結果並處理
                    while (searchResults.HasMore())
                    {
                        try
                        {
                            var entry = searchResults.Next();
                            if (entry == null) continue;

                            var status = CalculateAndBuildPasswordStatus(entry, passwordMaxAgeDays);

                            // 暫時無需求，若有需要時上方要補 memberOf 屬性
                            var attributes = entry.GetAttributeSet();
                            if (attributes.ContainsKey("memberOf"))
                            {
                                status.User.MemberOf = entry.GetAttribute("memberOf").StringValueArray
                                    ?.Select(m => m.Split(',')[0].Replace("CN=", ""))
                                    .ToList() ?? new List<string>();
                            }
                            else
                            {
                                status.User.MemberOf = new List<string>(); // 確保是空列表而不是 null
                            }

                            userStatusList.Add(status);
                        }
                        catch (LdapException ex)
                        {
                            _logger.LogError(ex, "在獲取所有使用者狀態的迴圈中，處理單一條目時發生 LDAP 錯誤。");
                            continue; // 忽略單一錯誤，繼續處理下一個
                        }
                    }

                    _logger.LogInformation("成功獲取並處理了 {Count} 個 AD 使用者的狀態資訊。", userStatusList.Count);
                    return userStatusList;
                });

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 GetAllUsersWithPasswordStatusAsync 時發生未預期的錯誤。");
                throw; // 重新拋出，讓 Controller 捕捉並回傳 500 錯誤
            }
        }

        /// <inheritdoc/>
        public async Task<AdUserCreateResult> CreateLdapUserAsync(LdapUser newUserModel)
        {
            if (newUserModel == null || string.IsNullOrWhiteSpace(newUserModel.SamAccountName))
            {
                return new AdUserCreateResult { IsSuccess = false, Message = "使用者模型或 SamAccountName 不得為空。" };
            }

            try
            {
                // --- 1. 查找組織單位 (OU) 與群組 ---
                // 使用非同步查詢以提升效能
                var roleInfo = await _misContext.LdapUserRole
                    .FirstOrDefaultAsync(r => r.role == newUserModel.Department);

                if (roleInfo == null || string.IsNullOrEmpty(roleInfo.basic_dn))
                {
                    string errorMessage = $"部門名稱 '{newUserModel.Department}' 在資料庫中找不到對應的 BasicDN。";
                    _logger.LogError(errorMessage);
                    return new AdUserCreateResult { IsSuccess = false, Message = errorMessage };
                }

                // --- 2. 執行同步的 LDAP 操作 ---
                // 將所有 LDAP 操作包裹在 Task.Run 中，使其在背景執行緒上運行
                return await Task.Run(() =>
                {
                    using var connection = new LdapConnection();
                    connection.Connect(_ldapConfig.Host, LdapConnection.DefaultPort);
                    connection.Bind(_ldapConfig.AdminBaseDC, _ldapConfig.AdminPassword);

                    string userCN = newUserModel.Surname + newUserModel.GivenName;
                    string userDN = $"cn={userCN},{roleInfo.basic_dn}";

                    // --- 3. 建構使用者屬性 ---
                    var attributeSet = new LdapAttributeSet
                    {
                        new LdapAttribute("objectClass", new[] { "top", "person", "organizationalPerson", "user" }),
                        new LdapAttribute("sAMAccountName", newUserModel.SamAccountName),
                        new LdapAttribute("cn", userCN),
                        new LdapAttribute("sn", newUserModel.Surname),
                        new LdapAttribute("givenName", newUserModel.GivenName),
                        new LdapAttribute("mail", $"{newUserModel.SamAccountName}@feb.gov.tw"),
                        new LdapAttribute("userPrincipalName", $"{newUserModel.SamAccountName}@feb.gov.tw"),
                        new LdapAttribute("userPassword", _config.GetValue<string>("AdControllerConfig:LdapUserDefaultPassword")),
                        new LdapAttribute("displayName", userCN),
                        new LdapAttribute("title", newUserModel.Title),
                        new LdapAttribute("department", newUserModel.Department),
                        new LdapAttribute("description", newUserModel.Description),
                        new LdapAttribute("st", "M365"), // 固定的 "st" 屬性 "M365"
                        new LdapAttribute("streetAddress", newUserModel.StreetAddress),
                        new LdapAttribute("pwdLastSet", "0"), // 強制下次登入時變更密碼
                        new LdapAttribute("userAccountControl", "544") // 用戶帳號啟用並強制密碼變更
                    };

                    // --- 4. 建立使用者 ---
                    var newEntry = new LdapEntry(userDN, attributeSet);
                    connection.Add(newEntry);
                    _logger.LogInformation("用戶 {SamAccountName} 於 DN: {UserDN} 新增成功。", newUserModel.SamAccountName, userDN);

                    // --- 5. 加入群組 ---
                    if (!string.IsNullOrEmpty(roleInfo.memberof))
                    {
                        string[] groupDNs = roleInfo.memberof.Split(';');
                        foreach (var groupDn in groupDNs)
                        {
                            if (string.IsNullOrWhiteSpace(groupDn)) continue;

                            var addModification = new LdapModification(LdapModification.Add, new LdapAttribute("member", userDN));
                            connection.Modify(groupDn, addModification);
                            _logger.LogInformation("用戶 {SamAccountName} 成功加入群組 {GroupDN}。", newUserModel.SamAccountName, groupDn);
                        }
                    }

                    return new AdUserCreateResult
                    {
                        IsSuccess = true,
                        Message = $"用戶 {newUserModel.SamAccountName} 建立成功。",
                        UserDistinguishedName = userDN
                    };
                });
            }
            catch (LdapException ex)
            {
                _logger.LogError(ex, "建立 LDAP 使用者 '{SamAccountName}' 時發生 LDAP 錯誤: {LdapErrorMessage}", newUserModel.SamAccountName, ex.LdapErrorMessage);
                return new AdUserCreateResult { IsSuccess = false, Message = $"LDAP 錯誤: {ex.LdapErrorMessage}" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "建立 LDAP 使用者 '{SamAccountName}' 時發生未預期的錯誤。", newUserModel.SamAccountName);
                return new AdUserCreateResult { IsSuccess = false, Message = $"未預期的錯誤: {ex.Message}" };
            }
        }

        /// <inheritdoc/>
        public async Task<SimpleApiResponse> UpdateLdapUserAsync(string username, AdUserUpdateModel updateData)
        {
            // --- 1. 輸入驗證 ---
            if (string.IsNullOrWhiteSpace(username))
            {
                return new SimpleApiResponse { IsSuccess = false, Message = "使用者名稱不得為空。" };
            }
            if (updateData == null)
            {
                return new SimpleApiResponse { IsSuccess = false, Message = "更新資料不得為空。" };
            }

            _logger.LogInformation("開始更新使用者 '{Username}' 的資訊...", username);

            try
            {
                // 將所有同步的 LDAP 操作包裹在 Task.Run 中
                return await Task.Run(() =>
                {
                    using var connection = new LdapConnection();
                    connection.Connect(_ldapConfig.Host, LdapConnection.DefaultPort);
                    connection.Bind(_ldapConfig.AdminBaseDC, _ldapConfig.AdminPassword);

                    // --- 2. 查找目標使用者 ---
                    // 使用精確查詢找到使用者的 DN
                    string searchFilter = $"(&(objectClass=user)(sAMAccountName={username}))";
                    LdapSearchConstraints searchConstraints = new LdapSearchConstraints { TimeLimit = 30000 };
                    string userDn = null;

                    try
                    {
                        var searchResults = connection.Search(_ldapConfig.BaseDC, LdapConnection.ScopeSub, searchFilter, new[] { "dn" }, false, searchConstraints);
                        if (searchResults.HasMore())
                        {
                            userDn = searchResults.Next()?.Dn;
                        }
                    }
                    catch (LdapException ex)
                    {
                        _logger.LogError(ex, "在查找使用者 '{Username}' 以進行更新時發生 LDAP 錯誤。", username);
                        throw; // 重新拋出，由外層 catch 處理
                    }

                    if (string.IsNullOrWhiteSpace(userDn))
                    {
                        _logger.LogWarning("更新使用者失敗：在 AD 中找不到使用者 '{Username}'。", username);
                        return new SimpleApiResponse { IsSuccess = false, Message = $"找不到使用者 '{username}'。" };
                    }

                    // --- 3. 建立修改集 (Modification Set) ---
                    // 根據 AdUserUpdateModel 中非 null 的屬性，動態建立修改列表
                    var modificationList = new ArrayList();

                    // 輔助方法，用於安全地添加修改項
                    void AddModificationIfNotNull(string attributeName, string value)
                    {
                        if (value != null) // 只處理非 null 的值
                        {
                            modificationList.Add(new LdapModification(LdapModification.Replace, new LdapAttribute(attributeName, value)));
                        }
                    }

                    // 將 AdUserUpdateModel 的屬性映射回 AD 屬性名稱
                    AddModificationIfNotNull("description", updateData.Description);
                    AddModificationIfNotNull("physicalDeliveryOfficeName", updateData.Office);
                    AddModificationIfNotNull("streetAddress", updateData.EmployeeId);
                    AddModificationIfNotNull("department", updateData.Department);
                    AddModificationIfNotNull("title", updateData.Title);

                    if (modificationList.Count == 0)
                    {
                        _logger.LogInformation("使用者 '{Username}' 沒有提供任何需要更新的資訊。", username);
                        return new SimpleApiResponse { IsSuccess = true, Message = "沒有提供任何需要更新的資訊。" };
                    }

                    // --- 4. 執行修改 ---
                    try
                    {
                        LdapModification[] mods = (LdapModification[])modificationList.ToArray(typeof(LdapModification));
                        connection.Modify(userDn, mods);
                        _logger.LogInformation("成功更新使用者 '{Username}' 的資訊。", username);
                        return new SimpleApiResponse { IsSuccess = true, Message = $"使用者 '{username}' 的資訊已成功更新。" };
                    }
                    catch (LdapException ex)
                    {
                        _logger.LogError(ex, "更新使用者 '{Username}' (DN: {UserDn}) 時發生 LDAP 錯誤。", username, userDn);
                        return new SimpleApiResponse { IsSuccess = false, Message = $"更新時發生 LDAP 錯誤: {ex.LdapErrorMessage}" };
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 UpdateLdapUserAsync 時發生未預期的錯誤。");
                throw; // 重新拋出，讓 Controller 捕捉並回傳 500 錯誤
            }
        }

        /// <summary>
        /// 從 AD 中指定的 SearchBase 遞迴獲取所有使用者物件，並包含稽核所需屬性。
        /// </summary>
        /// <returns>LdapUser 物件的列表。若發生錯誤則回傳空列表。</returns>
        private IEnumerable<LdapUser> GetAllUsersForAudit()
        {
            var users = new List<LdapUser>();
            
            using (var conn = new LdapConnection())
            {
                try
                {
                    _logger.LogInformation("正在連線至 AD 並獲取所有稽核所需的使用者...");

                    // 連線與綁定;
                    conn.Connect(_ldapConfig.Host, LdapConnection.DefaultPort);
                    conn.Bind(_ldapConfig.AdminBaseDC, _ldapConfig.AdminPassword);

                    // 定義稽核報表所需回傳的屬性
                    string[] attrs = {
                        "sAMAccountName", "displayName", "distinguishedName",
                        "userAccountControl", "memberOf"
                    };

                    string searchFilter = "(&(objectClass=user)(!(objectClass=computer)))";
                    string searchBase = _adAuditConfig.SearchBase;

                    // 加入超時設定
                    LdapSearchConstraints searchConstraints = new LdapSearchConstraints();
                    searchConstraints.TimeLimit = 30000; // 30秒超時

                    // 執行搜尋
                    var searchResults = conn.Search(
                        searchBase,                   // 使用設定檔中的 SearchBase
                        LdapConnection.ScopeSub,      // 遞迴搜尋所有子 OU
                        searchFilter,                 // 篩選條件：僅使用者物件
                        attrs,                        // 指定要獲取的屬性
                        false,                        // 不只獲取屬性名稱，也要獲取值
                        searchConstraints
                    );

                    // 遍歷搜尋結果並轉換為 LdapUser 物件
                    while (searchResults.HasMore())
                    {
                        LdapEntry entry = null;
                        try
                        {
                            entry = searchResults.Next();
                            if (entry == null) continue;

                            // 判斷帳戶是否停用
                            var uacStr = entry.GetSafeAttribute("userAccountControl");
                            int.TryParse(uacStr, out var uac);
                            var isActive = (uac & 0x0002) == 0 ? "Active" : "Inactive";

                            // memberOf (隸屬群組) 這個屬性是「多值」的，所以沒有使用GetSafeAttribute(單值)方法取值
                            var attributes = entry.GetAttributeSet();
                            var memberOfList = new List<string>();
                            if (attributes.ContainsKey("memberOf"))
                            {
                                // 直接將處理後的結果轉換為 List<string> 並指派
                                memberOfList = entry.GetAttribute("memberOf").StringValueArray
                                    ?.Select(m => m.Split(',')[0].Replace("CN=", ""))
                                    .ToList() ?? new List<string>();
                            }

                            users.Add(new LdapUser
                            {
                                SamAccountName = entry.GetSafeAttribute("sAMAccountName"),
                                DisplayName = entry.GetSafeAttribute("displayName"),
                                DistinguishedName = entry.GetSafeAttribute("distinguishedName"),
                                MemberOf = memberOfList,
                                IsActive = isActive
                            });
                        }
                        catch (LdapException ex)
                        {
                            _logger.LogInformation(ex, "{MethodName} 偵測到一個無法處理的 LDAP 條目並已跳過。這通常是無害的系統物件。", nameof(GetAllUsersForAudit));
                            continue; // 忽略單一錯誤，繼續處理下一個
                        }
                    }
                    _logger.LogInformation("成功從 AD 獲取 {UserCount} 位使用者。", users.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "在 {MethodName} 中獲取 AD 使用者時發生錯誤。", nameof(GetAllUsersForAudit));

                    // 發生錯誤時回傳空列表
                    return new List<LdapUser>();
                }
            } // 'using' 會在此處自動確保 conn.Disconnect() 被呼叫

            return users;
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<AdAuditReportItem>> GenerateAuditReportDataAsync()
        {
            _logger.LogInformation("開始執行 AD 帳號清查報表生成任務...");

            // 使用 Task.Run 確保 LDAP 這個同步操作不會阻塞主執行緒
            return await Task.Run(() =>
            {
                try
                {
                    // 1. 獲取所有 AD 使用者
                    var allUsers = GetAllUsersForAudit();

                    // 將設定檔中完整的 ExcludedGroups DN 列表，轉換為簡潔的 CN 名稱列表
                    var simplifiedExcludedGroups = _adAuditConfig.ExcludedGroups
                        .Select(dn => dn.Split(',')[0].Replace("CN=", ""))
                        .ToList();

                    // 將 PrivilegedGroups 也進行簡化
                    var simplifiedPrivilegedGroups = _adAuditConfig.PrivilegedGroups
                        .Select(dn => dn.Split(',')[0].Replace("CN=", ""))
                        .ToList();

                    // 2. 執行篩選與轉換 (LINQ)
                    var reportData = allUsers
                        .Where(user =>
                        {
                            // 判斷條件一：使用者是否位於被排除的 OU 中
                            // 使用者 DN (DistinguishedName) 若包含任何一個 ExcludedOUs 中的字串，則為 true
                            bool isInExcludedOU = _adAuditConfig.ExcludedOUs
                                .Any(ou => user.DistinguishedName.Contains(ou));

                            // 判斷條件二：使用者是否為被排除的群組成員
                            // 使用者的 MemberOf 屬性與 ExcludedGroups 清單是否有任何交集
                            bool isInExcludedGroup = user.MemberOf
                                .Intersect(simplifiedExcludedGroups, StringComparer.OrdinalIgnoreCase)
                                .Any();

                            // 核心篩選邏輯：當使用者 "不" 在排除的OU 且 "不" 在排除的群組時，才回傳 true，代表應保留此使用者
                            return !isInExcludedOU && !isInExcludedGroup;
                        })
                        .Select(user => new AdAuditReportItem
                        {
                            SamAccountName = user.SamAccountName,
                            DisplayName = user.DisplayName,
                            IsEnabled = user.IsActive == "Active",

                            // 判斷特權帳號
                            IsPrivileged =
                                // 檢查 PrivilegedAccounts 列表是否有值？
                                (_adAuditConfig.PrivilegedAccounts != null && _adAuditConfig.PrivilegedAccounts.Any())

                                // IF TRUE: (有值) -> 則只使用個人列表進行判斷
                                ? _adAuditConfig.PrivilegedAccounts.Contains(user.SamAccountName, StringComparer.OrdinalIgnoreCase)

                                // ELSE: (為空) -> 則回退使用群組列表進行判斷
                                : user.MemberOf.Intersect(simplifiedPrivilegedGroups, StringComparer.OrdinalIgnoreCase).Any()
                        })
                        .ToList();

                    _logger.LogInformation("AD 帳號清查報表資料生成成功，共處理 {DataCount} 筆資料。", reportData.Count);

                    return reportData;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "在 {MethodName} 中生成稽核報表資料時發生錯誤。", nameof(GenerateAuditReportDataAsync));
                    
                    // 發生錯誤時回傳空列表，避免 API 崩潰
                    return Enumerable.Empty<AdAuditReportItem>();
                }
            });
        }
    }
}