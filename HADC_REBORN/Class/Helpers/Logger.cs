using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Printing;
using System.Text;
using System.Threading.Tasks;

namespace HADC_REBORN.Class.Helpers
{
    public class Logger
    {
        private const long maxLogFileBytes = 5 * 1024 * 1024;
        private const int keepLogDays = 3;

        //Detailed logging, enabled with "debug: true" in configuration.yaml
#if DEBUG
        public static bool verbose = true;
#else
        public static bool verbose = false;
#endif

        private static bool isInistialized = false;
        private readonly object writeLock = new object();
#if DEBUG
        private string appDir = Directory.GetCurrentDirectory();
#else
        private string appDir = AppDomain.CurrentDomain.BaseDirectory;
#endif
        private string logFilePath;
        private DateTime lastInitializeDateTime;
        private static string[] secreetsStrings = new string[] { };

        public Logger() {
            logFilePath = initialize();
        }

        public void setSecreets(string[] strings)
        {
            if (!isInistialized)
            {
                logFilePath = initialize();
            }
            secreetsStrings = strings.Where(x => !string.IsNullOrEmpty(x)).ToArray();
        }

        public string getLogPath()
        {
            return logFilePath;
        }

        private string initialize()
        {
            string logFolderPath = Path.Combine(appDir, "logs");
            Debug.WriteLine(logFolderPath);

            if (!Directory.Exists(logFolderPath))
            {
                Directory.CreateDirectory(logFolderPath);
            }

            string logFileName = "log_" + ((DateTime.Now).ToString("MM_dd_yyyy")) + ".log";
            string logFilePath = Path.Combine(logFolderPath, logFileName);

            if (!File.Exists(logFilePath))
            {
                File.WriteAllText(logFilePath, getLogMessage("Initializing",0), System.Text.Encoding.UTF8);
            }

            removeOldLogFiles(logFolderPath);

            lastInitializeDateTime = DateTime.Now;
            isInistialized = true;

            return logFilePath;
        }

        //Removes every log file that is older than keepLogDays
        private void removeOldLogFiles(string rootLogFolderPath)
        {
            DateTime oldestToKeep = DateTime.Now.AddDays(-keepLogDays);
            foreach (string logFile in Directory.GetFiles(rootLogFolderPath, "log_*"))
            {
                try
                {
                    if (File.GetLastWriteTime(logFile) < oldestToKeep)
                    {
                        File.Delete(logFile);
                    }
                }
                catch (Exception)
                {
                    //File is in use, try again next time
                }
            }
        }

        //Keeps at most two files of maxLogFileBytes per day
        private void rotateIfTooLarge()
        {
            FileInfo logFile = new FileInfo(logFilePath);
            if (!logFile.Exists || logFile.Length < maxLogFileBytes)
            {
                return;
            }

            string previousLogFilePath = logFilePath + ".1";
            File.Delete(previousLogFilePath);
            File.Move(logFilePath, previousLogFilePath);
        }

        private string getLogMessage(string text, int type = 0)
        {
            string parsedText = text;
            if (secreetsStrings.Length > 0)
            {
                foreach (string secret in secreetsStrings)
                {
                    if (string.IsNullOrEmpty(secret)) continue;
                    parsedText = System.Text.RegularExpressions.Regex.Replace(parsedText, System.Text.RegularExpressions.Regex.Escape(secret), "***SECRET****", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    // Also handle JSON escaped version (backslashes)
                    string escapedSecret = secret.Replace("/", "\\/");
                    if (escapedSecret != secret)
                    {
                        parsedText = System.Text.RegularExpressions.Regex.Replace(parsedText, System.Text.RegularExpressions.Regex.Escape(escapedSecret), "***SECRET****", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }
                }
            }

            return DateTime.Now.ToString("[MM/dd/yyyy-HH:mm.ss]") + "[" + type + "]" + parsedText + "\n";
        }

        public void writeLine(string msg, int type = 0)
        {
            Debug.WriteLine(msg);

            lock (writeLock)
            {
                try
                {
                    //Start a new file on a new day
                    if (!isInistialized || DateTime.Now.Date != lastInitializeDateTime.Date)
                    {
                        logFilePath = initialize();
                    }

                    rotateIfTooLarge();

                    using (FileStream fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (StreamWriter streamWriter = new StreamWriter(fileStream))
                    {
                        streamWriter.Write(getLogMessage(msg, type));
                    }
                }
                catch (Exception)
                {
                    //Logging must never stop the app
                }
            }
        }

        //Only written when detailed logging is enabled
        public void writeDebug(string msg, int type = 0)
        {
            if (verbose)
            {
                writeLine(msg, type);
            }
        }
    }
}
