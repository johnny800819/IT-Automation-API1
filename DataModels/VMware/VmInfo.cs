using System.Text.Json;
using System.Text.Json.Serialization;

namespace API.DataModels.VMware
{
    /// <summary>
    /// 代表從 vCenter API 獲取的單一虛擬機的核心資訊。
    /// </summary>
    public class VmInfo
    {
        /// <summary>
        /// 虛擬機的名稱。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// 虛擬機的電源狀態 (例如 "POWERED_ON", "POWERED_OFF")。
        /// </summary>
        [JsonPropertyName("power_state")]
        public string PowerState { get; set; }

        /// <summary>
        /// 虛擬機的唯一識別碼。
        /// </summary>
        [JsonPropertyName("vm")]
        public string VmId { get; set; }

        /// <summary>
        /// 虛擬機的上次開機時間 (本地時間)。
        /// </summary>
        public DateTime? BootTime { get; set; }

        /// <summary>
        /// 虛擬機的主要 IP 位址。
        /// </summary>
        /// <remarks>
        /// 只有在該虛擬機上安裝並運行 VMware Tools 時才能獲取到此資訊。
        /// </remarks>
        public string IpAddress { get; set; }

        /// <summary>
        /// 虛擬機的所有有效 IPv4 位址清單（已依環境優先級排序，Primary IP 排在首位）。
        /// </summary>
        public List<string> IpAddresses { get; set; } = new();

        /// <summary>
        /// 虛擬機的 CPU 核心數 (vCPU)。
        /// </summary>
        [JsonPropertyName("cpu_count")]
        public int? CpuCount { get; set; }

        /// <summary>
        /// 虛擬機的記憶體配置大小 (MiB)。
        /// </summary>
        [JsonPropertyName("memory_size_MiB")]
        public long? MemorySizeMiB { get; set; }

        /// <summary>
        /// 虛擬機的記憶體配置大小 (GB，自動依 MiB / 1024.0 換算並四捨五入至小數點後兩位)。
        /// </summary>
        public double? MemorySizeGB => MemorySizeMiB.HasValue ? Math.Round(MemorySizeMiB.Value / 1024.0, 2) : null;

        /// <summary>
        /// 客體作業系統完整描述 (Guest OS Full Name)。
        /// </summary>
        public string GuestOS { get; set; }
    }

    // 註解：
    // 以下的 'internal' 類別是用於反序列化的輔助模型。
    // 它們的職責是精準地映射 vCenter 特定 API 端點回傳的、結構不同的 JSON。
    // 例如，/vm/{vm_id} 回傳的 JSON 物件非常龐大，但我們只關心 'boot_time'，
    // 因此建立一個只包含 BootTime 屬性的 VmDetail 模型來解析它。
    // 這可以避免主模型 VmInfo 被不相關的屬性污染，讓程式碼更清晰、更健壯。

    /// <summary>
    /// 【輔助模型】用於從 /api/vcenter/vm/{vm_id}/guest/identity 端點解析 'ip_address' 與作業系統名稱。
    /// </summary>
    internal class VmGuestIdentity
    {
        [JsonPropertyName("ip_address")]
        public string IpAddress { get; set; }

        [JsonPropertyName("full_name")]
        public JsonElement? FullNameElement { get; set; }

        /// <summary>
        /// 取得客體作業系統完整名稱，相容不同 vCenter 版本格式（字串或包含 default_message 的物件）。
        /// </summary>
        public string GetGuestFullName()
        {
            if (!FullNameElement.HasValue) return null;
            var elem = FullNameElement.Value;
            if (elem.ValueKind == JsonValueKind.String)
            {
                return elem.GetString();
            }
            if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty("default_message", out var msg))
            {
                return msg.GetString();
            }
            return null;
        }
    }

    /// <summary>
    /// 【輔助模型】用於從 /api/vcenter/vm/{vm_id}/power 端點解析
    /// </summary>
    internal class VmPowerInfo
    {
        [JsonPropertyName("state")]
        public string State { get; set; }
    }
}