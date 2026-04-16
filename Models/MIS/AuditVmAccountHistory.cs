using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API.Models.MIS
{
    /// <summary>
    /// 【資料表】AuditVmAccountHistory
    /// 用途：儲存虛擬機 (VMware VM) 本機帳戶稽核記錄。
    /// 每次執行清查時，PowerShell 腳本會透過 vCenter 取得各 VM 的本機帳號清單，
    /// 並以 Upsert 邏輯同步至此資料表，做為稽核報表的資料來源。
    /// </summary>
    [Table("AuditVmAccountHistory")]
    public class AuditVmAccountHistory
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
        /// 虛擬伺服器的 IP 位址，例如 "10.13.1.10"。
        /// 對於雲端等無法透過 PowerShell 掃描的主機，此 IP 由人工維護。
        /// </summary>
        public string VirtualServerIP { get; set; }

        /// <summary>
        /// 本機使用者帳號名稱，例如 "Administrator"、"feb16"、"nsroot"。
        /// 與 VirtualServerIP 合併作為唯一識別鍵 (Upsert 的比對基準)。
        /// </summary>
        public string LocalAccountName { get; set; }

        /// <summary>
        /// 帳號的啟用狀態，值為 "[啟用]" 或 "[停用]"。
        /// 括號格式為報表色彩標記所需，每次掃描後由程式自動更新。
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// 稽核人員的處置設定，預設值為 "■保留帳號 □刪除帳號"。
        /// 供稽核者手動勾選，不會被自動掃描覆蓋。
        /// </summary>
        public string Setting { get; set; }

        /// <summary>
        /// 本筆記錄最後一次被掃描或建立的時間。
        /// 每次 Upsert 成功時，即使狀態未改變也會更新此欄位。
        /// </summary>
        public DateTime AuditDate { get; set; }

        /// <summary>
        /// 是否為本次清查的現存有效帳號。
        /// true  = 目前仍存在於 VM 上（或為手動維護的永久紀錄）。
        /// false = 帳號已從 VM 移除，此為軟刪除紀錄，報表不顯示。
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 是否為人工手動建立的紀錄（非 PowerShell 自動掃描）。
        /// true  = 手動維護（如雲端主機），永久豁免自動軟刪除邏輯，確保記錄不因掃描失敗而消失。
        /// false = 由 PowerShell 自動掃描建立，受 Upsert 邏輯管理。
        /// </summary>
        public bool IsManual { get; set; } = false;
    }
}