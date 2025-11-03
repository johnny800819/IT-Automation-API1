using API.DataModels.VMware;

namespace API.Services.VMware
{
    /// <summary>
    /// 定義與 VMware vCenter 互動的業務邏輯服務契約。
    /// </summary>
    public interface IVMwareService
    {
        /// <summary>
        /// 從指定的 vCenter 環境中獲取所有虛擬機的列表。
        /// </summary>
        /// <param name="environmentKey">要查詢的環境鍵值 (例如 "Production" 或 "Test")。</param>
        /// <returns>一個包含所有 VmInfo 物件的列表。</returns>
        Task<List<VmInfo>> GetVmsAsync(string environmentKey);
    }
}