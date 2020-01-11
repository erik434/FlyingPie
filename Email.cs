using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using log4net;

namespace FlyingPie
{
    public class Email
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static void SendErrorMail(string message, Exception exception)
        {
            log.Error(message, exception);

            var errorRecipients = ConfigurationManager.AppSettings["ErrorEmails"];
            var body = $"Error message: {message}\nException: {exception}";
            SendEmail("IYD Notifier Error", body, errorRecipients, false);
        }

        public static bool SendNotificationMail(string body)
        {
            //TODO: use nice HTML formatting instead of boring plain text - this looks promising for templating: https://github.com/Antaris/RazorEngine

            const string subject = "IYD Update Notification";
            var updateRecipients = ConfigurationManager.AppSettings["UpdateEmails"];
            return SendEmail(subject, body, updateRecipients, false);
        }

        private static bool SendEmail(string subject, string message, string recipients, bool isHtml)
        {
            //TODO: include retries for certain errors? don't want to try indefinitely though

            try
            {
                var fromEmail = ConfigurationManager.AppSettings["FromEmailAddress"];
                var fromAddress = new MailAddress(fromEmail, "Flying Pie IYD Notifier");
                var emailPassword = Utilities.GetEmailPassword(false);

                using (var smtp = new SmtpClient
                {
                    Host = "smtp.gmail.com",
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromEmail, emailPassword)
                })
                using (var email = new MailMessage
                {
                    From = fromAddress,
                    Subject = subject,
                    Body = message,
                    IsBodyHtml = isHtml
                })
                {
                    email.To.Add(recipients);
                    smtp.Send(email);
                    return true;
                }
            }
            catch (Exception e)
            {
                log.ErrorFormat("Failed to send email! Exception:\n{0}", e);
                return false;
            }
        }
    }
}
