using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HADC_REBORN.Class.Helpers
{
    internal class Autostart
    {
#if DEBUG
        private static string appDir = Directory.GetCurrentDirectory();
#else
        private static string appDir = AppDomain.CurrentDomain.BaseDirectory;
#endif

        public static void register()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    string exePath = Path.Combine(appDir, System.AppDomain.CurrentDomain.FriendlyName + ".exe");
                    key.SetValue(System.AppDomain.CurrentDomain.FriendlyName, "\"" + exePath + "\"");
                    key.Close();
                }
            }
            catch (Exception)
            {
                throw new Exception("Autostart Registration Failed!");
            }
        }

        public static void unregister()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    key.DeleteValue(System.AppDomain.CurrentDomain.FriendlyName, false);
                }
            }
            catch (Exception)
            {
                throw new Exception("Autostart could not be turned off!");
            }
        }
    }
}
