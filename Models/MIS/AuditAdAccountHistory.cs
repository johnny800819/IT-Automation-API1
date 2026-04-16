using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API.Models.MIS
{
    /// <summary>
    /// 【資料表】AuditAdAccountHistory
    /// 用途：儲存 Active Directory (AD) 帳號稽核記錄。
    /// 每次執行清查時，系統會透過 LDAP 取得 AD 帳號清單，
    /// 做為稽核報表的資料來源（目前此 Model 為快照紀錄，不做 Upsert）。
    /// </summary>
    [Table("AuditAdAccountHistory")]
    public class AuditAdAccountHistory
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
        /// AD 帳號的 SAM Account Name（登入帳號），例如 "john.doe"、"svc-backup"。
        /// 為 AD 中的唯一識別名稱。
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// 是否為特權帳號（如系統管理員、網域管理員等）。
        /// true  = 特權帳號，報表中以 "■" 標示，排序時優先顯示。
        /// false = 一般帳號，報表中以 "□" 標示。
        /// </summary>
        public bool IsPrivileged { get; set; }

        /// <summary>
        /// AD 帳號的顯示名稱 (DisplayName)，例如 "王小明"。
        /// 若此欄位為空，報表會將該帳號歸類至「名稱空白帳號」工作表。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 帳號的啟用狀態，例如 "[啟用]" 或 "[停用]"。
        /// 括號格式為報表色彩標記所需，來源為 AD 的 Enabled 屬性。
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// 稽核後的建議處置，例如 "■保留 □刪除"。
        /// 此為預設的 checkbox 文字，供稽核人員列印後手動勾選。
        /// </summary>
        public string ProposedAction { get; set; }

        /// <summary>
        /// 本筆記錄的清查時間，記錄此筆資料是在何時從 AD 取得的。
        /// </summary>
        public DateTime AuditDate { get; set; }
    }
}