using System.Collections.Generic;

namespace API.Models.AppAudit
{
    /// <summary>
    /// 對應 appsettings.json -> AppAuditSettings 的設定模型
    /// </summary>
    public class AppAuditSettings
    {
        /// <summary>
        /// 所有已設定的應用系統清查設定清單
        /// </summary>
        public List<AppSystemConfig> Systems { get; set; } = new();
    }

    /// <summary>
    /// 單一應用系統的清查設定
    /// </summary>
    public class AppSystemConfig
    {
        /// <summary>
        /// 系統唯一識別碼，例如 "FEB_CMS"。
        /// 同時用於 API 路由參數與資料庫的 SystemKey 欄位。
        /// </summary>
        public string SystemKey { get; set; }

        /// <summary>
        /// 報表標題顯示名稱，例如 "FEB CMS 應用系統"。
        /// </summary>
        public string SystemTitle { get; set; }

        /// <summary>
        /// 此系統要清查的目標主機清單（可多台）
        /// </summary>
        public List<AppTargetConfig> Targets { get; set; } = new();

        /// <summary>
        /// 要在目標資料庫執行的 SQL 查詢語句。
        /// 需確保結果欄位名稱與 ColumnMapping 對應。
        /// </summary>
        public string SqlQuery { get; set; }

        /// <summary>
        /// 欄位對應，key 為程式內部名稱 (Department/AccountName/RealName)，
        /// value 為 SQL 結果集中的實際欄位名稱。
        /// </summary>
        public Dictionary<string, string> ColumnMapping { get; set; } = new();
    }

    /// <summary>
    /// 單一清查目標主機設定
    /// </summary>
    public class AppTargetConfig
    {
        /// <summary>
        /// 報表頁籤名稱，例如 "正式機-FEB_CMS"
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 對應 ConnectionStrings 的金鑰名稱，例如 "AuditAppConnection_Prod_227"
        /// </summary>
        public string ConnectionStringName { get; set; }

        /// <summary>
        /// 目標主機 IP，用於記錄至 AuditAppAccountHistory.TargetIP
        /// </summary>
        public string IP { get; set; }

        /// <summary>
        /// 環境別，例如 "Prod" 或 "Test"
        /// </summary>
        public string Environment { get; set; }
    }
}
