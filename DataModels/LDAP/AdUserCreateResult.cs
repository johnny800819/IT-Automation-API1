namespace API.DataModels.LDAP
{
    /// <summary>
    /// 存放 Active Directory 使用者建立作業的結果。
    /// </summary>
    public class AdUserCreateResult
    {
        /// <summary>
        /// 獲取或設定一個值，該值指示操作是否成功。
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 獲取或設定描述操作結果的摘要訊息（例如成功訊息或詳細的錯誤原因）。
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 如果建立成功，則獲取或設定新建立的使用者的完整識別名 (Distinguished Name)。
        /// </summary>
        public string UserDistinguishedName { get; set; }
    }
}