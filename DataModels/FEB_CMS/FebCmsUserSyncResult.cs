namespace API.DataModels.FEB_CMS
{
    /// <summary>
    /// 存放將 FebCms 使用者與 Active Directory 進行校對和同步作業的結果。
    /// </summary>
    public class FebCmsUserSyncResult
    {
        /// <summary>
        /// 描述同步作業結果的摘要訊息。
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 本次作業中，從 FebCms 資料庫中讀取並檢查的使用者總數。
        /// </summary>
        public int TotalCmsUsersChecked { get; set; }

        /// <summary>
        /// 本次作業中，因在 AD 中不存在而被標記為離職的使用者數量。
        /// </summary>
        public int ResignedUsersUpdatedCount { get; set; }

        /// <summary>
        /// 本次作業中，因職稱不一致而更新的使用者數量。
        /// </summary>
        public int JobTitlesUpdatedCount { get; set; }

        /// <summary>
        /// 整個同步作業的總執行時間（秒）。
        /// </summary>
        public double ExecutionTimeSeconds { get; set; }
    }
}