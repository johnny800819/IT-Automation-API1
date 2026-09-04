namespace API.DataModels.VMware
{
    /// <summary>
    /// 代表 ESXi 實體主機的硬體規格與運作狀態。
    /// </summary>
    public class EsxiHostInfo
    {
        /// <summary>
        /// 主機代碼 (例如 "host-21")。
        /// </summary>
        public string HostId { get; set; }

        /// <summary>
        /// 主機名稱或 IP (例如 "esxi01.boe.gov.tw")。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 連線狀態 (例如 "CONNECTED")。
        /// </summary>
        public string ConnectionState { get; set; }

        /// <summary>
        /// 電源狀態 (例如 "POWERED_ON")。
        /// </summary>
        public string PowerState { get; set; }

        /// <summary>
        /// 是否處於維護模式 (true 代表維護中，false 代表正常運作)。
        /// </summary>
        public bool InMaintenanceMode { get; set; }

        /// <summary>
        /// 該實體主機上搭載的虛擬機數量。
        /// </summary>
        public int VmCount { get; set; }

        /// <summary>
        /// 實體主機總 CPU 核心數 (Physical CPU Cores)。
        /// </summary>
        public int? CpuCores { get; set; }

        /// <summary>
        /// 實體主機總記憶體大小 (Bytes)。
        /// </summary>
        public long? MemorySizeBytes { get; set; }

        /// <summary>
        /// 實體主機總記憶體大小 (GB，自動依 Bytes / (1024^3) 換算四捨五入至小數點後兩位)。
        /// </summary>
        public double? MemorySizeGB => MemorySizeBytes.HasValue ? Math.Round((double)MemorySizeBytes.Value / (1024.0 * 1024.0 * 1024.0), 2) : null;
    }

    /// <summary>
    /// 包含 VM 清單與 ESXi 實體主機硬體統計之綜合報告模型。
    /// </summary>
    public class VmHardwareReportData
    {
        /// <summary>
        /// 虛擬機清單。
        /// </summary>
        public List<VmInfo> VmList { get; set; } = new();

        /// <summary>
        /// 符合條件（非維護模式且有搭載 VM）的 ESXi 實體主機清單。
        /// </summary>
        public List<EsxiHostInfo> ActiveEsxiHosts { get; set; } = new();

        /// <summary>
        /// 符合條件之 ESXi 實體主機總實體記憶體 (GB)。
        /// </summary>
        public double TotalHostMemoryGB => ActiveEsxiHosts.Sum(h => h.MemorySizeGB ?? 0);

        /// <summary>
        /// 符合條件之 ESXi 實體主機總實體 CPU 核心數。
        /// </summary>
        public int TotalHostCpuCores => ActiveEsxiHosts.Sum(h => h.CpuCores ?? 0);
    }
}
