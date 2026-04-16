using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API.Models.MIS
{
    /// <summary>
    /// 【資料表】AuditDbAccountHistory
    /// 用途：儲存資料庫伺服器 (SQL Server) 登入帳號稽核記錄。
    /// 每次執行清查時，系統會透過 SQL 腳本取得各伺服器的登入帳號清單，
    /// 並以 Upsert 邏輯同步至此資料表，做為稽核報表的資料來源。
    /// 人工維護欄位 (Assignee、ActionDecision) 受保護，不會被自動掃描覆蓋。
    /// </summary>
    [Table("AuditDbAccountHistory")]
    public class AuditDbAccountHistory
    {
        /// <summary>
        /// 主鍵，由資料庫自動遞增產生。
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// 清查環境別，例如 "Prod"（正式環境）或 "UAT"（測試環境）。
        /// 用於區隔不同環境的資料，避免資料混用。
        /// </summary>
        public string Environment { get; set; }

        /// <summary>
        /// 目標資料庫伺服器名稱，例如 "FEBSQL01"、"FEBSQL02"。
        /// 對應到 SQL 腳本設定中的伺服器識別名稱。
        /// </summary>
        public string TargetServer { get; set; }

        /// <summary>
        /// 資料庫登入帳號名稱，例如 "sa"、"BOE\SystemAdmin"。
        /// 與 TargetServer 合併作為唯一識別鍵 (Upsert 的比對基準)。
        /// </summary>
        public string AccountName { get; set; }

        /// <summary>
        /// 帳號的啟用狀態，例如 "啟用" 或 "停用"，來源為 SQL Server 掃描結果。
        /// 每次掃描後由程式自動更新。
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// 帳號在 SQL Server 中的伺服器角色，例如 "sysadmin"、"public"。
        /// 來源為 SQL Server 掃描結果，每次掃描後自動更新。
        /// </summary>
        public string ServerRole { get; set; }

        /// <summary>
        /// 帳號說明，優先顯示稽核者人工填寫的說明；若人工欄位為空，
        /// 才會自動填入 SQL Server 中 sys.server_principals 的登入說明。
        /// 人工填寫後，不會被後續掃描覆蓋（受保護欄位）。
        /// </summary>
        public string AccountDescription { get; set; }

        /// <summary>
        /// 此帳號的業務承辦人或負責人姓名，例如 "王小明"。
        /// 完全由人工維護，任何自動掃描均不會修改此欄位。
        /// 報表中依此欄位分群顯示，便於追蹤責任歸屬。
        /// </summary>
        public string Assignee { get; set; }

        /// <summary>
        /// 稽核後的處置決策，例如 "保留"、"刪除"、"停用"。
        /// 完全由人工維護，自動掃描不會修改此欄位。
        /// 若此欄位有值，報表將優先顯示此值；否則顯示預設 checkbox 文字供手動勾選。
        ///
        /// 【目前狀態：暫時空值】
        /// 原因：此欄位需要一個人工寫入的管道才有意義，而直接操作 DB 對稽核人員不友善。
        /// 設計規劃：待後續實作「稽核決策管理 UI」，提供介面讓稽核人員點選決策後，
        ///           透過 PATCH /api/account-audit/db/{id}/decision 端點寫入此欄位。
        /// TODO: 搭配管理 UI 完成後，此欄位即可投入正式使用。
        /// </summary>
        public string ActionDecision { get; set; }

        /// <summary>
        /// 本筆記錄最後一次被掃描或建立的時間。
        /// 每次 Upsert 成功時，即使狀態未改變也會更新此欄位。
        /// </summary>
        public DateTime AuditDate { get; set; }

        /// <summary>
        /// 是否為本次清查的現存有效帳號。
        /// true  = 目前仍存在於資料庫伺服器上。
        /// false = 帳號已從資料庫移除，此為軟刪除紀錄，報表不顯示。
        /// </summary>
        public bool IsActive { get; set; }
    }
}