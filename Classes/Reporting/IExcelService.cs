using API.DataModels.LDAP;

namespace API.Classes.Reporting
{
    /// <summary>
    /// 定義報表生成工具的介面。
    /// 作為一個通用的基礎設施服務，它負責將資料轉換為特定的檔案格式。
    /// </summary>
    public interface IExcelService
    {
        /// <summary>
        /// 根據 AD 稽核資料生成 Excel 檔案的 byte 陣列。
        /// </summary>
        /// <remarks>
        /// [架構決策] 此方法被設計為同步 (Synchronous) 方法，原因如下：
        /// 1. 依賴的函式庫 EPPlus.Free (基於 v4 核心) 提供的是同步的 GetAsByteArray() API。
        /// 2. 在記憶體中生成 Excel 檔案是一個 CPU 密集型 (CPU-Bound) 操作，而非 I/O 密集型。
        ///    執行緒在此期間會持續忙碌，使用非同步 (async) 不會帶來顯著的吞吐量優勢。
        /// 3. 對於此專案的低併發、小資料量場景，同步執行對效能影響極小，同時讓程式碼更簡潔。
        /// </remarks>
        /// <param name="data">從 LdapService 取得的報表資料列表。</param>
        /// <returns>包含 Excel 檔案內容的 byte 陣列。</returns>
        byte[] CreateAdAuditReport(IEnumerable<AdAuditReportItem> data);
    }
}