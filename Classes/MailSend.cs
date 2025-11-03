#nullable enable

// 使用MimeKit套件(官方推薦)
using MimeKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using MailKit.Security;
using Microsoft.Extensions.Logging;

namespace API.Classes
{
    /// <summary>
    /// 寄件共用服務，使用金管會M365 MailRelay SMTP服務。
    /// </summary>
    /// <remarks>
    /// 修改時間：
    ///  - 2024年4月23日
    ///  - 2025年10月15日
    /// ---
    /// 內容概述：
    ///  - FQDN：feb-gov-tw.mail.protection.outlook.com
    ///  - SSL技術問題參考：https://stackoverflow.com/questions/66054848/ssl-or-tls-connection-error-in-mailkit-while-not-using-ssl-or-tls
    /// </remarks>
    public class MailSend : IMailSend
    {
        // 定義私有欄位，用來存放注入的服務
        private readonly IConfiguration _config;
        private readonly ILogger<MailSend> _logger;

        /// <summary>
        /// 透過建構函式，接收 IConfiguration 和 ILogger。
        /// </summary>
        public MailSend(IConfiguration config, ILogger<MailSend> logger)
        {
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// 執行郵件發送。
        /// </summary>
        /// <param name="emailSubject">信件主旨</param>
        /// <param name="emailBody">信件內容 (支援 HTML 格式)</param>
        /// <param name="receiverAddress">收件人信箱地址</param>
        public void MailSendExecute(string emailSubject, string emailBody, string receiverAddress = "")
        {
            // 從注入的 _config 讀取
            string? smtpServer = _config.GetValue<string>("MailSend:SMTPServer");
            int serverPort = _config.GetValue<int>("MailSend:ServerPort");
            string? senderName = _config.GetValue<string>("MailSend:SenderName");
            string? senderAddress = _config.GetValue<string>("MailSend:SenderAddress");
            string? ccReceiverAddress = _config.GetValue<string>("MailSend:CcReceiverAddress");

            // MimeKit 設定
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderAddress));
            message.To.Add(MailboxAddress.Parse(receiverAddress));

            if (!string.IsNullOrEmpty(ccReceiverAddress))
            {
                message.Cc.Add(new MailboxAddress(senderName, ccReceiverAddress));
            }
            message.Subject = emailSubject;

            // 使用 BodyBuilder 建立郵件內容
            var builder = new BodyBuilder { HtmlBody = emailBody };

            // BodyBuilder Attachments
            // builder.Attachments.Add(@"files\test2.pdf");
            // builder.Attachments.Add(@"files\test1.csv");

            message.Body = builder.ToMessageBody();

            try
            {
                using var client = new SmtpClient();
                client.Connect(smtpServer, serverPort, SecureSocketOptions.None);
                client.AuthenticationMechanisms.Remove("XOAUTH2");
                //client.Authenticate(MailAccount, MailPassword);
                client.Send(message);
                client.Disconnect(true);

                _logger.LogInformation("郵件已成功發送至 {Receiver}，主旨為：{Subject}", receiverAddress, emailSubject);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "郵件發送失敗至 {Receiver}，主旨為：{Subject}", receiverAddress, emailSubject);
                throw;
            }
        }
    }
}
