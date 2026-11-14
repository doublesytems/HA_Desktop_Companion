using System;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace HADC_REBORN.Class.Helpers
{
    //Stores sensitive settings encrypted with Windows DPAPI, readable only by the current Windows user
    public class SecretStore
    {
        private const string prefix = "dpapi:";

        public static bool isProtected(string storedValue)
        {
            return !String.IsNullOrEmpty(storedValue) && storedValue.StartsWith(prefix);
        }

        public static string protect(string plainValue)
        {
            if (String.IsNullOrEmpty(plainValue))
            {
                return "";
            }

            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainValue), null, DataProtectionScope.CurrentUser);
            return prefix + Convert.ToBase64String(encrypted);
        }

        public static string unprotect(string storedValue)
        {
            if (String.IsNullOrEmpty(storedValue))
            {
                return "";
            }

            //Values saved by older versions are still plain text
            if (!isProtected(storedValue))
            {
                return storedValue;
            }

            try
            {
                byte[] decrypted = ProtectedData.Unprotect(Convert.FromBase64String(storedValue.Substring(prefix.Length)), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex)
            {
                //Happens when the settings file comes from another user or computer
                App.log.writeLine("Stored secret can not be decrypted: " + ex.Message);
                return "";
            }
        }

        public static string read(Configuration config, string key)
        {
            return unprotect(config.AppSettings.Settings[key].Value);
        }

        public static void write(Configuration config, string key, string plainValue)
        {
            config.AppSettings.Settings[key].Value = protect(plainValue);
        }

        //Encrypts values that are still stored as plain text, returns true when something changed
        public static bool migrate(Configuration config, string[] keys)
        {
            bool changed = false;
            foreach (string key in keys)
            {
                string storedValue = config.AppSettings.Settings[key].Value;
                if (!String.IsNullOrEmpty(storedValue) && !isProtected(storedValue))
                {
                    write(config, key, storedValue);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
