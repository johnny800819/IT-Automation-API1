using API.Classes.VMware;
using API.DataModels.VMware;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

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

                        List<string> discoveredIps = new();

                        // 1. 呼叫 /guest/networking/interfaces 端點獲取所有網卡 IP（使用高效輕量 JsonDocument 解析，無需多餘模型）
                        var netResponse = await client.GetAsync($"{envConfig.ApiBaseUrl}/vcenter/vm/{vm.VmId}/guest/networking/interfaces");
                        if (netResponse.IsSuccessStatusCode)
                        {
                            var netJson = await netResponse.Content.ReadAsStringAsync();
                            using var netDoc = JsonDocument.Parse(netJson);
                            if (netDoc.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var iface in netDoc.RootElement.EnumerateArray())
                                {
                                    if (iface.TryGetProperty("ip", out var ipObj) &&
                                        ipObj.TryGetProperty("ip_addresses", out var ipList) &&
                                        ipList.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var ipDetail in ipList.EnumerateArray())
                                        {
                                            if (ipDetail.TryGetProperty("ip_address", out var ipVal))
                                            {
                                                var rawIp = ipVal.GetString()?.Trim();
                                                // 過濾無效 IP、IPv6、127.0.0.1、169.254.x.x
                                                if (!string.IsNullOrEmpty(rawIp) &&
                                                    !rawIp.Contains(':') &&
                                                    !rawIp.StartsWith("127.") &&
                                                    !rawIp.StartsWith("169.254.") &&
                                                    !discoveredIps.Contains(rawIp))
                                                {
                                                    discoveredIps.Add(rawIp);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // 2. 呼叫 /guest/identity 端點來獲取身分資訊與備援 IP
                        var identityResponse = await client.GetAsync($"{envConfig.ApiBaseUrl}/vcenter/vm/{vm.VmId}/guest/identity");
                        string identityIp = null;
                        if (identityResponse.IsSuccessStatusCode)
                        {
                            var identityJson = await identityResponse.Content.ReadAsStringAsync();
                            var vmIdentity = JsonSerializer.Deserialize<VmGuestIdentity>(identityJson, options); // 反序列化 VmGuestIdentity
                            identityIp = vmIdentity?.IpAddress?.Trim();
                            vm.GuestOS = vmIdentity?.GetGuestFullName(); // 取得客體作業系統完整名稱
                        }

                        // 若 identity 端點有提供 IP 且尚未納入，補充至清單中
                        if (!string.IsNullOrEmpty(identityIp) && !identityIp.Contains(':') && !discoveredIps.Contains(identityIp))
                        {
                            discoveredIps.Add(identityIp);
                        }

                        // 3. 依環境規則排序 IP：Primary IP 置頂，其餘次要 IP 接在後方
                        // 正式區：Primary IP 為 10.13.1.X 或 10.13.30.X
                        // 測試區：Primary IP 為 10.13.20.X
                        var sortedIps = discoveredIps.OrderBy(ip =>
                        {
                            if (environmentKey == "Production")
                            {
                                if (ip.StartsWith("10.13.1.") || ip.StartsWith("10.13.30.")) return 1;
                                return 2;
                            }
                            else // Test
                            {
                                if (ip.StartsWith("10.13.20.")) return 1;
                                return 2;
                            }
                        }).ThenBy(ip => ip).ToList();

                        vm.IpAddresses = sortedIps;
                        vm.IpAddress = sortedIps.FirstOrDefault() ?? identityIp;
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

        /// <inheritdoc/>
        public async Task<List<VmInfo>> GetVmsHardwareInfoAsync(string environmentKey)
        {
            _logger.LogInformation("開始獲取 VMware vCenter ({Environment}) 之虛擬機硬體配置資訊...", environmentKey);

            // 呼叫獲取完整硬體規格 (CPU, 記憶體, GuestOS, IP, 開機狀態) 的 VM 列表
            var vmList = await GetVmsAsync(environmentKey);

            var totalMemoryGB = vmList.Sum(v => v.MemorySizeGB ?? 0);
            var totalCpus = vmList.Sum(v => v.CpuCount ?? 0);

            _logger.LogInformation("成功獲取 VMware ({Environment}) 硬體資訊：共 {Count} 台 VM，總配置 CPU: {TotalCpus} 核心，總配置記憶體: {TotalMemoryGB:F2} GB",
                environmentKey, vmList.Count, totalCpus, totalMemoryGB);

            return vmList;
        }

        /// <inheritdoc/>
        public async Task<List<EsxiHostInfo>> GetActiveEsxiHostsHardwareAsync(string environmentKey)
        {
            _logger.LogInformation("開始從 VMware vCenter ({Environment}) 獲取符合條件的 ESXi 實體主機硬體資訊...", environmentKey);

            if (!_vmwareConfig.Environments.TryGetValue(environmentKey, out var envConfig))
            {
                _logger.LogError("找不到指定的 VMware 環境設定：{Environment}", environmentKey);
                throw new ArgumentException($"無效的環境金鑰: {environmentKey}");
            }

            var client = _httpClientFactory.CreateClient("NoSSL");
            string sessionToken = null;
            var hostList = new List<EsxiHostInfo>();

            try
            {
                // 1. 透過 REST API 取得所有主機基本資訊
                sessionToken = await GetSessionTokenAsync(client, envConfig);
                if (sessionToken != null)
                {
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("vmware-api-session-id", sessionToken);

                    var hostResponse = await client.GetAsync($"{envConfig.ApiBaseUrl}/vcenter/host");
                    if (hostResponse.IsSuccessStatusCode)
                    {
                        var hostJson = await hostResponse.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(hostJson);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var h in doc.RootElement.EnumerateArray())
                            {
                                var hostInfo = new EsxiHostInfo
                                {
                                    HostId = h.TryGetProperty("host", out var hostProp) ? hostProp.GetString() : null,
                                    Name = h.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null,
                                    ConnectionState = h.TryGetProperty("connection_state", out var connProp) ? connProp.GetString() : null,
                                    PowerState = h.TryGetProperty("power_state", out var powerProp) ? powerProp.GetString() : null
                                };
                                hostList.Add(hostInfo);
                            }
                        }
                    }
                    else
                    {
                        var errorContent = await hostResponse.Content.ReadAsStringAsync();
                        _logger.LogWarning("REST API /vcenter/host 回傳失敗。HTTP Status: {StatusCode}, Content: {Content}", hostResponse.StatusCode, errorContent);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "透過 REST API 查詢 ESXi 主機清單時發生非阻斷性異常。");
            }
            finally
            {
                await LogoutSessionAsync(client, envConfig, sessionToken);
            }

            if (hostList.Count == 0)
            {
                _logger.LogInformation("未發現任何 ESXi 主機或無法透過 REST API 取得主機清單。");
                return hostList;
            }

            // 2. 透過原生 SOAP Web Service (/sdk) 查詢實體 RAM、CPU、維護模式與 VM 數量
            try
            {
                await QueryEsxiHostDetailsViaSoapAsync(client, envConfig, hostList);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "透過 SOAP Web Service 查詢 ESXi 主機硬體細節時發生異常，將保留已知資訊。");
            }

            // 3. 嚴格依條件篩選：
            foreach (var h in hostList)
            {
                _logger.LogInformation("ESXi 篩選前屬性 - {Name} (ID: {HostId}): ConnectionState={ConnectionState}, InMaintenance={InMaintenanceMode}, VmCount={VmCount}, RAM(GB)={MemorySizeGB}, CPU={CpuCores}",
                    h.Name ?? "N/A", h.HostId ?? "N/A", h.ConnectionState ?? "null", h.InMaintenanceMode, h.VmCount, h.MemorySizeGB, h.CpuCores);
            }

            // 前提條件：1. 連線正常且非維護模式 (!InMaintenanceMode)； 2. 該 ESXi 上有搭載 VM (VmCount > 0)
            var filteredHosts = hostList.Where(h =>
                string.Equals(h.ConnectionState, "CONNECTED", StringComparison.OrdinalIgnoreCase) &&
                !h.InMaintenanceMode &&
                h.VmCount > 0
            ).ToList();

            var totalHostRAM = filteredHosts.Sum(h => h.MemorySizeGB ?? 0);
            var totalHostCPUs = filteredHosts.Sum(h => h.CpuCores ?? 0);

            _logger.LogInformation("ESXi 主機硬體篩選完畢：總主機 {Total} 台，符合條件（非維護且搭載 VM）共 {Count} 台，實體總 RAM: {TotalRAM:F2} GB，實體總 CPU: {TotalCPUs} 核心",
                hostList.Count, filteredHosts.Count, totalHostRAM, totalHostCPUs);

            return filteredHosts;
        }

        /// <inheritdoc/>
        public async Task<VmHardwareReportData> GetVmHardwareReportDataAsync(string environmentKey)
        {
            _logger.LogInformation("開始並行獲取 VM 列表與 ESXi 實體主機硬體匯總 ({Environment})...", environmentKey);

            var vmTask = GetVmsHardwareInfoAsync(environmentKey);
            var hostTask = GetActiveEsxiHostsHardwareAsync(environmentKey);

            await Task.WhenAll(vmTask, hostTask);

            return new VmHardwareReportData
            {
                VmList = await vmTask,
                ActiveEsxiHosts = await hostTask
            };
        }

        /// <summary>
        /// 透過原生 vSphere SOAP Web Service (/sdk) 批次查詢 ESXi 主機的實體記憶體、CPU 核心數、維護模式與 VM 數量。
        /// </summary>
        private async Task QueryEsxiHostDetailsViaSoapAsync(HttpClient client, VMwareEnvironment envConfig, List<EsxiHostInfo> hostList)
        {
            var uri = new Uri(envConfig.ApiBaseUrl);
            var sdkUrl = $"{uri.Scheme}://{uri.Authority}/sdk";
            _logger.LogInformation("正在連線 vSphere SOAP Web Service: {SdkUrl}", sdkUrl);

            // 1. RetrieveServiceContent
            var contentReqXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:urn=""urn:vim25"">
    <soapenv:Body>
        <urn:RetrieveServiceContent>
            <urn:_this type=""ServiceInstance"">ServiceInstance</urn:_this>
        </urn:RetrieveServiceContent>
    </soapenv:Body>
</soapenv:Envelope>";

            var contentResp = await client.PostAsync(sdkUrl, new StringContent(contentReqXml, Encoding.UTF8, "text/xml"));
            if (!contentResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("RetrieveServiceContent 呼叫失敗: {StatusCode}", contentResp.StatusCode);
                return;
            }

            var contentXmlStr = await contentResp.Content.ReadAsStringAsync();
            var contentDoc = XDocument.Parse(contentXmlStr);
            XNamespace urn = "urn:vim25";

            var sessionManagerVal = contentDoc.Descendants(urn + "sessionManager").FirstOrDefault()?.Value ?? "SessionManager";
            var propCollectorVal = contentDoc.Descendants(urn + "propertyCollector").FirstOrDefault()?.Value ?? "propertyCollector";

            // 2. Login
            var loginReqXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:urn=""urn:vim25"">
    <soapenv:Body>
        <urn:Login>
            <urn:_this type=""SessionManager"">{sessionManagerVal}</urn:_this>
            <urn:userName>{System.Security.SecurityElement.Escape(envConfig.Username)}</urn:userName>
            <urn:password>{System.Security.SecurityElement.Escape(envConfig.Password)}</urn:password>
        </urn:Login>
    </soapenv:Body>
</soapenv:Envelope>";

            var loginResp = await client.PostAsync(sdkUrl, new StringContent(loginReqXml, Encoding.UTF8, "text/xml"));
            if (!loginResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("SOAP Login 呼叫失敗: {StatusCode}", loginResp.StatusCode);
                return;
            }

            string soapCookie = null;
            if (loginResp.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
            {
                soapCookie = cookieHeaders.FirstOrDefault();
            }

            try
            {
                // 3. 準備 RetrievePropertiesEx 請求
                var objSetXml = new StringBuilder();
                foreach (var h in hostList)
                {
                    if (!string.IsNullOrEmpty(h.HostId))
                    {
                        objSetXml.Append($@"<urn:objectSet><urn:obj type=""HostSystem"">{h.HostId}</urn:obj></urn:objectSet>");
                    }
                }

                var propReqXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:urn=""urn:vim25"">
    <soapenv:Body>
        <urn:RetrieveProperties>
            <urn:_this type=""PropertyCollector"">{propCollectorVal}</urn:_this>
            <urn:specSet>
                <urn:propSet>
                    <urn:type>HostSystem</urn:type>
                    <urn:pathSet>name</urn:pathSet>
                    <urn:pathSet>hardware.memorySize</urn:pathSet>
                    <urn:pathSet>hardware.cpuInfo.numCpuCores</urn:pathSet>
                    <urn:pathSet>runtime.inMaintenanceMode</urn:pathSet>
                    <urn:pathSet>vm</urn:pathSet>
                </urn:propSet>
                {objSetXml}
            </urn:specSet>
        </urn:RetrieveProperties>
    </soapenv:Body>
</soapenv:Envelope>";

                var propReq = new HttpRequestMessage(HttpMethod.Post, sdkUrl);
                propReq.Content = new StringContent(propReqXml, Encoding.UTF8, "text/xml");
                if (!string.IsNullOrEmpty(soapCookie))
                {
                    propReq.Headers.Add("Cookie", soapCookie.Split(';')[0]);
                }

                var propResp = await client.SendAsync(propReq);
                if (propResp.IsSuccessStatusCode)
                {
                    var propXmlStr = await propResp.Content.ReadAsStringAsync();
                    var propDoc = XDocument.Parse(propXmlStr);

                    // 解析每台主機的屬性
                    foreach (var objElem in propDoc.Descendants(urn + "returnval"))
                    {
                        var objId = objElem.Element(urn + "obj")?.Value;
                        var host = hostList.FirstOrDefault(h => h.HostId == objId);
                        if (host == null) continue;

                        foreach (var propElem in objElem.Descendants(urn + "propSet"))
                        {
                            var name = propElem.Element(urn + "name")?.Value;
                            var valElem = propElem.Element(urn + "val");

                            switch (name)
                            {
                                case "name":
                                    if (string.IsNullOrEmpty(host.Name)) host.Name = valElem?.Value;
                                    break;
                                case "hardware.memorySize":
                                    if (long.TryParse(valElem?.Value, out var memBytes)) host.MemorySizeBytes = memBytes;
                                    break;
                                case "hardware.cpuInfo.numCpuCores":
                                    if (int.TryParse(valElem?.Value, out var cores)) host.CpuCores = cores;
                                    break;
                                case "runtime.inMaintenanceMode":
                                    if (bool.TryParse(valElem?.Value, out var inMaint)) host.InMaintenanceMode = inMaint;
                                    break;
                                case "vm":
                                    // ManagedObjectReference 陣列
                                    host.VmCount = valElem?.Elements().Count() ?? 0;
                                    break;
                            }
                        }
                    }
                    _logger.LogInformation("成功解析 {Count} 台 ESXi 主機之 SOAP 實體硬體屬性。", hostList.Count);
                }
                else
                {
                    var errorContent = await propResp.Content.ReadAsStringAsync();
                    _logger.LogWarning("SOAP API RetrievePropertiesEx 回傳失敗。HTTP Status: {StatusCode}, Content: {Content}", propResp.StatusCode, errorContent);
                }
            }
            finally
            {
                // 4. Logout Session
                try
                {
                    var logoutXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:urn=""urn:vim25"">
    <soapenv:Body>
        <urn:Logout>
            <urn:_this type=""SessionManager"">{sessionManagerVal}</urn:_this>
        </urn:Logout>
    </soapenv:Body>
</soapenv:Envelope>";
                    var logoutReq = new HttpRequestMessage(HttpMethod.Post, sdkUrl);
                    logoutReq.Content = new StringContent(logoutXml, Encoding.UTF8, "text/xml");
                    if (!string.IsNullOrEmpty(soapCookie)) logoutReq.Headers.Add("Cookie", soapCookie.Split(';')[0]);
                    await client.SendAsync(logoutReq);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SOAP Logout 時發生非阻斷性異常。");
                }
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