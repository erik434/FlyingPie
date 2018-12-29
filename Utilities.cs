using System;
using System.Configuration;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace FlyingPie
{
    static class Utilities
    {
        private const string EmailPasswordConfigKey = "EmailPassword";

        public static void SetAppConfig(string key, string value)
        {
            var fullExePath = Assembly.GetEntryAssembly().Location;
            Configuration configuration = ConfigurationManager.OpenExeConfiguration(fullExePath);
            configuration.AppSettings.Settings.Remove(key);
            configuration.AppSettings.Settings.Add(key, value);
            configuration.Save();
            ConfigurationManager.RefreshSection("appSettings");
        }

        /// <summary>
        /// Returns the email password if we have it stored. Optionally prompts the user for the password if we don't already have it stored.
        /// Returns null if 
        /// </summary>
        /// <param name="promptForPassword"></param>
        /// <returns></returns>
        public static SecureString GetEmailPassword(bool promptForPassword)
        {
            //First see if we've already saved the password
            var encryptedPassword = ConfigurationManager.AppSettings[EmailPasswordConfigKey];
            if (encryptedPassword != null)
            {
                var decryptedPassword = DecryptString(encryptedPassword);
                if (decryptedPassword != null && decryptedPassword.Length > 0)
                {
                    return decryptedPassword;
                }
            }

            //If we didn't already have it saved, or decrypting failed, we'll need to prompt the user (if allowed).
            if (!promptForPassword) return null;
            var passwordFromUser = PromptForEmailPassword();

            //Encrypt the password and save it for later, then return it.
            SetAppConfig(EmailPasswordConfigKey, EncryptString(passwordFromUser));
            return passwordFromUser;
        }

        public static SecureString PromptForEmailPassword()
        {
            bool done = false;
            Console.Write("First-time setup: what is the **app-specific** password for sending email? ");
            var password = new SecureString();

            while (!done)
            {
                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        done = true;
                        continue;

                    case ConsoleKey.Backspace:
                        if (password.Length > 0)
                        {
                            Console.Write("\b \b");
                            password.RemoveAt(password.Length - 1);
                        }
                        continue;
                }

                //Ignore the keypress if the character is 0 - that means it was something special we can ignore.
                var c = key.KeyChar;
                if (c == 0) continue;

                //If we get here, the keypress should be part of the password. Show a * for the user and save it.
                Console.Write("*");
                password.AppendChar(key.KeyChar);
            }
            password.MakeReadOnly();
            return password;
        }

        private static readonly byte[] Entropy = Encoding.Unicode.GetBytes(ConfigurationManager.AppSettings["SaltyString"]);

        private static string EncryptString(SecureString input)
        {
            byte[] encryptedData = ProtectedData.Protect(
                Encoding.Unicode.GetBytes(ToInsecureString(input)),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedData);
        }

        private static SecureString DecryptString(string encryptedData)
        {
            try
            {
                byte[] decryptedData = ProtectedData.Unprotect(
                    Convert.FromBase64String(encryptedData),
                    Entropy,
                    DataProtectionScope.CurrentUser);
                return ToSecureString(Encoding.Unicode.GetString(decryptedData));
            }
            catch
            {
                return null;
            }
        }

        private static SecureString ToSecureString(this string input)
        {
            SecureString secure = new SecureString();
            foreach (char c in input)
            {
                secure.AppendChar(c);
            }
            secure.MakeReadOnly();
            return secure;
        }

        private static string ToInsecureString(this SecureString input)
        {
            string returnValue = string.Empty;
            IntPtr ptr = Marshal.SecureStringToBSTR(input);
            try
            {
                returnValue = Marshal.PtrToStringBSTR(ptr);
            }
            finally
            {
                Marshal.ZeroFreeBSTR(ptr);
            }
            return returnValue;
        }

        private static TimeZoneInfo FlyingPieTimeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("Mountain Standard Time");

        public static DateTime FlyingPieLocalDateToUtcDateTime(int localYear, int localMonth, int localDay)
        {
            //This date will have an unspecified time zone, so we have to convert it to UTC using an explicit time zone below,
            // instead of using the ToUniversalTime() method (which I think would assume it's in the local time zone - which
            // won't necessarily match Flying Pie time).
            var dateUnspecified = new DateTime(localYear, localMonth, localDay);

            //Use Flying Pie's time zone info to convert the above zone-less DateTime to UTC
            var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(dateUnspecified, FlyingPieTimeZoneInfo);
            return utcDateTime;
        }
    }
}
