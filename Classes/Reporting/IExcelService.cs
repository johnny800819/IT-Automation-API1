using API.Models.MIS;
using System.Collections.Generic;
using API.DataModels.LDAP;

namespace API.Classes.Reporting
{
    /// <summary>
    /// 定義報表生成工具的介面。
    /// 作為一個通用的基礎設施服務，它負責將資料轉換為特定的檔案格式。
    /// 
    /// [架構決策] 下列方法被設計為同步 (Synchronous) 方法，原因如下：
    /// 1. 依賴的函式庫 EPPlus.Free (基於 v4 核心) 提供的是同步的 GetAsByteArray() API。
    /// 2. 在記憶體中生成 Excel 檔案是一個 CPU 密集型 (CPU-Bound) 操作，而非 I/O 密集型。
    ///    執行緒在此期間會持續忙碌，使用非同步 (async) 不會帶來顯著的吞吐量優勢。
    /// 3. 對於此專案的低併發、小資料量場景，同步執行對效能影響極小，同時讓程式碼更簡潔。
    /// </summary>
    public interface IExcelService
    {
        /// <summary>
        /// 根據 AD 稽核資料生成 Excel 檔案的 byte 陣列。
        /// </summary>
        /// <param name="data">從 LdapService 取得的報表資料列表。</param>
        /// <returns>包含 Excel 檔案內容的 byte 陣列。</returns>
        byte[] CreateAdAuditReport(IEnumerable<AdAuditReportItem> data);

        /// <summary>
        /// 根據多台伺服器的資料庫帳號清查結果，生成包含多個頁籤的 Excel 檔案。
        /// </summary>
        /// <param name="dataMap">字典，Key 為分頁名稱 (工作表名稱)，Value 為該分頁的稽核紀錄列表。</param>
        /// <returns>包含 Excel 檔案內容的 byte 陣列。</returns>
        byte[] CreateDbAuditReport(IDictionary<string, IEnumerable<AuditDbAccountHistory>> dataMap);

        /// <summary>
        /// 根據虛擬機本機帳號清查結果生成 Excel 檔案的 byte 陣列。
        /// </summary>
        /// <param name="data">從 VmAuditService 取得的報表資料列表。</param>
        /// <returns>包含 Excel 檔案內容的 byte 陣列。</returns>
        byte[] CreateVmAuditReport(IEnumerable<AuditVmAccountHistory> data);

        /// <summary>
        /// 根據應用系統帳號清查結果，生成包含多個頁籤的 Excel 稽核報表。
        /// </summary>
        /// <param name="systemTitle">報表大標題，例如 "FEB CMS 應用系統"。</param>
        /// <param name="dataMap">字典，Key 為頁籤名稱，Value 為該頁籤的清查紀錄列表。</param>
        /// <returns>包含 Excel 檔案內容的 byte 陣列。</returns>
        byte[] CreateAppAuditReport(string systemTitle, IDictionary<string, IEnumerable<AuditAppAccountHistory>> dataMap);
    }
}