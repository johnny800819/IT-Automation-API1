namespace API.DataModels
{
    /// <summary>
    /// 提供一個簡單、通用的 API 回應結構。
    /// </summary>
    public class SimpleApiResponse
    {
        /// <summary>
        /// 獲取或設定一個值，該值指示操作是否成功。
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 獲取或設定描述操作結果的摘要訊息。
        /// </summary>
        public string Message { get; set; }
    }
}