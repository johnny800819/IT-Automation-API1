using API.DataModels.FEB_CMS;
using API.Models;
using API.Services.LDAP;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace API.Services.FEB_CMS
{
    /// <summary>
    /// 實現與 FebCms 使用者管理相關的業務邏輯。
    /// </summary>
    public class FebCmsUserService : IFebCmsUserService
    {
        private readonly FEB_CMSContext _febCmsContext;
        private readonly ILdapService _ldapService;
        private readonly ILogger<FebCmsUserService> _logger;

        /// <summary>
        /// 初始化 FebCmsUserService 的新執行個體。
        /// </summary>
        public FebCmsUserService(
            FEB_CMSContext febCmsContext,
            ILdapService ldapService,
            ILogger<FebCmsUserService> logger)
        {
            _febCmsContext = febCmsContext;
            _ldapService = ldapService;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<FebCmsUserSyncResult> SyncUsersFromAdAsync()
        {
            _logger.LogInformation("開始執行 FebCms 使用者與 AD 的校對同步作業...");
            var stopwatch = Stopwatch.StartNew();
            var result = new FebCmsUserSyncResult();
            bool hasChanges = false;

            try
            {
                // 1. 取得 FEB_CMS User 資料(帳號是啟用的)
                var cmsUsers = await _febCmsContext.User
                    .Where(s => s.Status != 0)
                    .ToListAsync();
                result.TotalCmsUsersChecked = cmsUsers.Count;

                // 2. 過濾例外清單 (建議未來將此清單移至 appsettings.json)
                var exceptionAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "febsys", "sp1", "sp2", "febsharepoint",
                };

                // 只查詢非例外 FEB_CMS 帳號
                var accountsToCheck = cmsUsers
                    .Where(u => !exceptionAccounts.Contains(u.Account))
                    .Select(u => u.Account)
                    .ToList();

                // 3. 呼叫 LdapService 獲取 AD 資料 
                var adUsersInfo = await _ldapService.GetUsersInfoAsync(accountsToCheck);

                // 4. 執行比對與更新
                foreach (var cmsUser in cmsUsers)
                {
                    if (exceptionAccounts.Contains(cmsUser.Account))
                        continue;

                    // 檢查 AD 中是否存在該 FebCms 帳號
                    adUsersInfo.TryGetValue(cmsUser.Account, out var adUser);

                    if (adUser == null) // 比對 A (離職人員)
                    {
                        cmsUser.Status = 0;
                        cmsUser.UpdateTime = DateTime.Now;
                        result.ResignedUsersUpdatedCount++;
                        hasChanges = true;
                        _logger.LogInformation("檢查 User Table 員工：{Name} ({Account}) 離職未移除，已啟用自動更新 Status = 0。", cmsUser.Name, cmsUser.Account);
                    }
                    else // 比對 B (職稱異動) 2025/06/19需求：更新職稱資訊
                    {
                        if (cmsUser.JobTitle != adUser.Title)
                        {
                            string oldTitle = cmsUser.JobTitle;
                            cmsUser.JobTitle = adUser.Title;
                            cmsUser.UpdateTime = DateTime.Now;
                            result.JobTitlesUpdatedCount++;
                            hasChanges = true;
                            _logger.LogInformation("檢查 User Table 員工：{Name} ({Account}) 職稱異動，已啟用自動更新職稱資訊，由「{OldTitle}」改為「{NewTitle}」。", cmsUser.Name, cmsUser.Account, oldTitle, adUser.Title);
                        }
                    }
                }

                // 5. 儲存與日誌
                if (hasChanges)
                {
                    //await _febCmsContext.SaveChangesAsync();
                    result.Message = $"本次自動更新已結束，共計 {result.ResignedUsersUpdatedCount} 筆離職未移除，{result.JobTitlesUpdatedCount} 筆職稱異動。";
                    _logger.LogInformation(result.Message);
                }
                else
                {
                    result.Message = "檢查 User Table 確認正常，未包含任何離職未移除資料或職稱異動。";
                    _logger.LogInformation(result.Message);
                }

                stopwatch.Stop();
                result.ExecutionTimeSeconds = stopwatch.Elapsed.TotalSeconds;
                _logger.LogInformation("執行總花費時間: {ElapsedSeconds} 秒。\n", result.ExecutionTimeSeconds);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 FebCms 使用者與 AD 的同步作業時發生未預期的錯誤。");
                throw;
            }
        }
    }
}