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

        public static void SendNotificationMail(List<IydEvent> events)
        {
            //TODO: use nice HTML formatting instead of boring plain text - this looks promising for templating: https://github.com/Antaris/RazorEngine
            if (events.Count == 0)
            {
                log.Debug("No events to announce - skipping email notification.");
                return;
            }

            const string subject = "IYD Update Notification";
            var body = new StringBuilder();
            foreach (var e in events)
            {
                body.AppendFormat("{0:dddd, MMMM d} - {1}\n", e.Date, e.Name);
            }

            var updateRecipients = ConfigurationManager.AppSettings["UpdateEmails"];
            SendEmail(subject, body.ToString(), updateRecipients, false);
        }

        private static void SendEmail(string subject, string message, string recipients, bool isHtml)
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
                }
            }
            catch (Exception e)
            {
                log.ErrorFormat("Failed to send email! Exception:\n{0}", e);
            }
        }
    }
}
