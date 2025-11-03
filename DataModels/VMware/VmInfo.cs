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
    }

    // 註解：
    // 以下的 'internal' 類別是用於反序列化的輔助模型。
    // 它們的職責是精準地映射 vCenter 特定 API 端點回傳的、結構不同的 JSON。
    // 例如，/vm/{vm_id} 回傳的 JSON 物件非常龐大，但我們只關心 'boot_time'，
    // 因此建立一個只包含 BootTime 屬性的 VmDetail 模型來解析它。
    // 這可以避免主模型 VmInfo 被不相關的屬性污染，讓程式碼更清晰、更健壯。

    /// <summary>
    /// 【輔助模型】用於從 /api/vcenter/vm/{vm_id}/guest/identity 端點解析 'ip_address'。
    /// </summary>
    internal class VmGuestIdentity
    {
        [JsonPropertyName("ip_address")]
        public string IpAddress { get; set; }
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