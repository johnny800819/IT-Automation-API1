using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API.Models.MIS
{
    /// <summary>
    /// 【資料表】AuditAppAccountHistory
    /// 用途：儲存各項應用系統（如 FEB_CMS）的帳號清查記錄。
    /// 採用通用設計，透過 SystemKey 區分不同系統，支援組態驅動的自動清查。
    /// </summary>
    [Table("AuditAppAccountHistory")]
    public class AuditAppAccountHistory
    {
        /// <summary>
        /// 主鍵，由資料庫自動遞增產生。
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// 應用系統識別碼，例如 "FEB_CMS"。
        /// 用於區分不同應用系統的稽核資料。
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string SystemKey { get; set; }

        /// <summary>
        /// 清查環境別，例如 "Prod" 或 "Test"。
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Environment { get; set; }

        /// <summary>
        /// 目標伺服器 IP，例如 "10.13.30.227"。
        /// </summary>
        [MaxLength(50)]
        public string TargetIP { get; set; }

        /// <summary>
        /// 組室名稱 (來源：Department)。
        /// </summary>
        [MaxLength(100)]
        public string Department { get; set; }

        /// <summary>
        /// 應用系統帳號名稱 (來源：Account)。
        /// 與 SystemKey, Environment, TargetIP 合併作為唯一識別基準 (Upsert)。
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string AccountName { get; set; }

        /// <summary>
        /// 使用者真實姓名 (來源：Name)。
        /// </summary>
        [MaxLength(200)]
        public string RealName { get; set; }

        /// <summary>
        /// 稽核決策，例如 "■續用 □刪除"。
        /// 完全由人工維護，自動掃描不會修改此欄位。
        /// </summary>
        [MaxLength(100)]
        public string ActionDecision { get; set; }

        /// <summary>
        /// 最後一次清查掃描時間。
        /// </summary>
        public DateTime AuditDate { get; set; }

        /// <summary>
        /// 是否為本次清查的有效帳號。
        /// </summary>
        public bool IsActive { get; set; } = true;
    }
}
