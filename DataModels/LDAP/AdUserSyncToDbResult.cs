namespace API.DataModels.LDAP
{
    /// <summary>
    /// 存放將 Active Directory 使用者同步至資料庫作業的結果。
    /// </summary>
    public class AdUserSyncToDbResult
    {
        /// <summary>
        /// 描述同步作業結果的摘要訊息。
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 本次作業中，從 AD 成功獲取並處理的使用者總數。
        /// </summary>
        public int TotalAdUsersProcessed { get; set; }

        /// <summary>
        /// 本次作業中，因是全新使用者而新增至資料庫的數量。
        /// </summary>
        public int InsertedCount { get; set; }

        /// <summary>
        /// 本次作業中，因資料異動而新增歷史紀錄的使用者數量。
        /// </summary>
        public int UpdatedCount { get; set; }

        /// <summary>
        /// 本次作業中，因在 AD 中不存在而被標記為 'inActive' (離職) 的使用者數量。
        /// </summary>
        public int InactivatedCount { get; set; }
    }
}