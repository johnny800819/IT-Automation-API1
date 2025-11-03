namespace API.Classes
{
    /// <summary>
    /// 定義郵件發送服務的契約
    /// </summary>
    public interface IMailSend
    {
        /// <summary>
        /// 執行郵件發送
        /// </summary>
        /// <param name="emailSubject">信件主旨</param>
        /// <param name="emailBody">信件內容 (HTML)</param>
        /// <param name="receiverAddress">收件人信箱</param>
        void MailSendExecute(string emailSubject, string emailBody, string receiverAddress);
    }
}