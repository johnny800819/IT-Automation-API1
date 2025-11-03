using API.Classes.VMware;
using API.DataModels.VMware;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace API.Services.VMware
{
    /// <summary>
    /// 實現與 VMware vCenter 互動的業務邏輯。
    /// </summary>
    public class VMwareService : IVMwareService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly VMwareConfig _vmwareConfig;
        private readonly ILogger<VMwareService> _logger;

        /// <summary>
        /// 初始化 VMwareService 的新執行個體。
        /// </summary>
        public VMwareService(
            IHttpClientFactory httpClientFactory,
            IOptions<VMwareConfig> vmwareConfigOptions,
            ILogger<VMwareService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _vmwareConfig = vmwareConfigOptions.Value;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<List<VmInfo>> GetVmsAsync(string environmentKey)
        {
            _logger.LogInformation("開始從 VMware vCenter ({Environment}) 獲取 VM 列表...", environmentKey);

            // 獲取環境設定
            if (!_vmwareConfig.Environments.TryGetValue(environmentKey, out var envConfig))
            {
                // 這個錯誤理論上不應再發生，因為 Controller 已驗證過，但作為防禦性措施保留
                _logger.LogError("找不到指定的 VMware 環境設定：{Environment}", environmentKey);
                throw new ArgumentException($"無效的環境金鑰: {environmentKey}");
            }

            // 使用您在 Program.cs 中設定的 "NoSSL" HttpClient，它會忽略 SSL 憑證錯誤
            var client = _httpClientFactory.CreateClient("NoSSL");
            string sessionToken = null;

            try
            {
                // --- 1. 獲取 Session Token ---
                sessionToken = await GetSessionTokenAsync(client, envConfig);
                if (sessionToken == null)
                {
                    throw new InvalidOperationException("無法獲取 vmware-api-session-id。");
                }

                // --- 2. 獲取 VM 列表 (原 GetVMsList 邏輯) ---
                _logger.LogInformation("正在使用 Session Token 獲取 VM 列表...");
                // 準備新的請求，移除 Basic 認證，改用 Token
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("vmware-api-session-id", sessionToken);

                var vmResponse = await client.GetAsync($"{envConfig.ApiBaseUrl}/vcenter/vm");
                vmResponse.EnsureSuccessStatusCode();

                string vmListJson = await vmResponse.Content.ReadAsStringAsync();

                // --- 4. 反序列化 (Deserialization) 為強型別模型 ---
                // 此步驟是將從 API 獲取的、純文字的 JSON 字串 (vmListJson)，
                // 轉換為 C# 程式碼可以理解和操作的、強型別的物件列表 (List<VmInfo>)。
                // 這使得我們後續可以使用 vm.Name, vm.PowerState 等語法來安全地存取資料，
                // 而不是手動解析字串。
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var vmList = JsonSerializer.Deserialize<List<VmInfo>>(vmListJson, options);

                _logger.LogInformation("成功獲取並解析了 {Count} 台 VM 的資訊，開始並行查詢詳細資料...", vmList.Count);

                // --- 3. 為每一台 VM 並行查詢詳細資訊 ---
                var detailTasks = vmList.Select(async vm =>
                {
                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                        // 呼叫 /power 端點來獲取開機狀態
                        var powerResponse = await client.GetAsync($"{envConfig.ApiBaseUrl}/vcenter/vm/{vm.VmId}/power");
                        if (powerResponse.IsSuccessStatusCode)
                        {
                            var powerJson = await powerResponse.Content.ReadAsStringAsync();
                            // 使用新的 VmPowerInfo 模型來反序列化
                            var vmPowerInfo = JsonSerializer.Deserialize<VmPowerInfo>(powerJson, options);
                        }

                        // 呼叫 /guest/identity 端點來獲取IP
                        var identityResponse = await client.GetAsync($"{envConfig.ApiBaseUrl}/vcenter/vm/{vm.VmId}/guest/identity");
                        if (identityResponse.IsSuccessStatusCode)
                        {
                            var identityJson = await identityResponse.Content.ReadAsStringAsync();
                            var vmIdentity = JsonSerializer.Deserialize<VmGuestIdentity>(identityJson, options); // 反序列化 VmGuestIdentity
                            vm.IpAddress = vmIdentity?.IpAddress; // 安全地存取屬性
                        }
                    }
                    catch (Exception ex)
                    {
                        // 隔離錯誤：單一 VM 查詢失敗不應影響整個列表
                        _logger.LogWarning(ex, "查詢 VM '{VmName}' ({VmId}) 的詳細資訊時發生錯誤，將跳過此 VM 的詳細資料。", vm.Name, vm.VmId);
                    }
                });

                // 等待所有詳細查詢任務完成
                await Task.WhenAll(detailTasks);

                _logger.LogInformation("所有 VM 詳細資訊查詢完畢。");
                return vmList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "與 VMware vCenter ({Environment}) 互動時發生錯誤。", environmentKey);
                throw; // 重新拋出例外，讓 Controller 層捕捉並回傳 500 錯誤
            }
            finally
            {
                // --- 4. 登出 Session (最佳實踐) ---
                await LogoutSessionAsync(client, envConfig, sessionToken);
            }
        }

        /// <summary>
        /// 獲取 vCenter Session Token
        /// </summary>
        private async Task<string> GetSessionTokenAsync(HttpClient client, VMwareEnvironment envConfig)
        {
            _logger.LogInformation("正在為 {ApiUrl} 獲取 Session Token...", envConfig.ApiBaseUrl);
            try
            {
                // 設定基本驗證
                var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{envConfig.Username}:{envConfig.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

                // 發送請求取得 Token
                var sessionResponse = await client.PostAsync($"{envConfig.ApiBaseUrl}/session", null);

                if (sessionResponse.IsSuccessStatusCode)
                {
                    // 從標頭中取得 vmware-api-session-id
                    if (sessionResponse.Headers.TryGetValues("vmware-api-session-id", out var values))
                    {
                        var token = values.FirstOrDefault();
                        _logger.LogInformation("成功獲取 Session Token。");
                        return token;
                    }
                    else
                    {
                        _logger.LogError("未能從 vCenter API 回應標頭中獲取 Session ID。");
                        return null;
                    }
                }
                else
                {
                    _logger.LogError("取得 Session Token 失敗，HTTP 狀態碼：{StatusCode}", sessionResponse.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "獲取 Session Token 的過程中發生未預期的錯誤。");
                return null;
            }
        }

        /// <summary>
        /// 登出 vCenter Session Token
        /// </summary>
        private async Task LogoutSessionAsync(HttpClient client, VMwareEnvironment envConfig, string sessionToken)
        {
            if (sessionToken == null) return;
            try
            {
                _logger.LogInformation("正在登出 Session Token...");
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("vmware-api-session-id", sessionToken);
                await client.DeleteAsync($"{envConfig.ApiBaseUrl}/session");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "登出 VMware Session 時發生錯誤，但不影響主要流程。");
            }
        }
    }
}