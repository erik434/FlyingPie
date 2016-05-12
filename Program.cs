using System;
using System.Configuration;
using System.Reflection;
using log4net;

namespace FlyingPie
{
    class Program
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            log.Debug("Flying Pie checker app started.\n");

            //Ensure that the email password has been provided before proceeding
            Utilities.GetEmailPassword(true);

            new FlyingPieChecker().RunCheck();
        }

        private static bool ShouldSendErrorEmail()
        {
            var interval = TimeSpan.FromDays(1);
            const string key = "LastErrorEmailSent";
            bool sendMail = false;

            try
            {
                //If there is no record of having sent an error email before, we should send one
                var lastSentString = ConfigurationManager.AppSettings[key];
                if (lastSentString == null)
                {
                    sendMail = true;
                }
                else
                {
                    //Send if we can parse the last time we sent an error email and it's at least the required interval in the past
                    DateTime parsedDateTime;
                    if (DateTime.TryParse(lastSentString, out parsedDateTime) &&
                        DateTime.Now >= parsedDateTime.Add(interval))
                    {
                        sendMail = true;
                    }
                }

                //If we're sending an email now, save the current time to the config file
                if (sendMail)
                {
                    Utilities.SetAppConfig(key, DateTime.Now.ToString("o"));
                }
            }
            catch (Exception e)
            {
                log.ErrorFormat("Error trying to read/write configuration:\n{0}", e);
            }
            return sendMail;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            const string message = "Unhandled exception!";
            var exception = e.ExceptionObject as Exception;

            if (ShouldSendErrorEmail())
            {
                Email.SendErrorMail(message, e.ExceptionObject as Exception);
            }
            else
            {
                log.Error(message, exception);
            }
            Environment.Exit(-1);
        }
    }
}