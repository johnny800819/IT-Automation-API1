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

        /// <summary>
        /// 從指定的 vCenter 環境中獲取所有虛擬機的完整硬體配置與規格資訊（包含記憶體、CPU、作業系統、IP 與開機狀態）。
        /// </summary>
        /// <param name="environmentKey">要查詢的環境鍵值 (例如 "Production" 或 "Test")。</param>
        /// <returns>一個包含完整硬體與資源配置資訊的 VmInfo 物件列表。</returns>
        Task<List<VmInfo>> GetVmsHardwareInfoAsync(string environmentKey);

        /// <summary>
        /// 從指定的 vCenter 環境中，取得符合條件（非維護模式且有搭載 VM）的 ESXi 實體主機真實硬體規格（實體總 RAM、實體總 CPU 核心等）。
        /// </summary>
        /// <param name="environmentKey">要查詢的環境鍵值 (例如 "Production" 或 "Test")。</param>
        /// <returns>符合條件之 ESXi 主機資訊清單。</returns>
        Task<List<EsxiHostInfo>> GetActiveEsxiHostsHardwareAsync(string environmentKey);

        /// <summary>
        /// 取得整合虛擬機硬體清單與 ESXi 實體主機資源總量的綜合報告資料。
        /// </summary>
        /// <param name="environmentKey">要查詢的環境鍵值 (例如 "Production" 或 "Test")。</param>
        /// <returns>包含 VM 清單與 ESXi 實體主機匯總之 VmHardwareReportData 物件。</returns>
        Task<VmHardwareReportData> GetVmHardwareReportDataAsync(string environmentKey);
    }
}