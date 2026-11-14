using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace HADC_REBORN.Class.Actions
{
    class Notification
    {
        private static string tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HADC_REBORN");

        //Downloaded images and sounds are kept for one day, the notification can still be visible that long
        private static void cleanTempFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(tempFolder);

                DateTime oldestToKeep = DateTime.Now.AddDays(-1);
                foreach (string file in System.IO.Directory.GetFiles(tempFolder))
                {
                    try
                    {
                        if (System.IO.File.GetLastWriteTime(file) < oldestToKeep)
                        {
                            System.IO.File.Delete(file);
                        }
                    }
                    catch (Exception)
                    {
                        //File is in use, try again next time
                    }
                }
            }
            catch (Exception ex)
            {
                App.log.writeLine("Notification temp folder cleanup failed: " + ex.Message);
            }
        }

        public static void Spawn(string body = "", string title = "", string imageUrl = "", string audioUrl = "", int duration = 500)
        {
            cleanTempFolder();

            ToastContentBuilder toast = new ToastContentBuilder();
            toast.AddText(body);

            if (!String.IsNullOrEmpty(title))
            {
                toast.AddText(title);
            }

            //A failed download must not prevent the notification itself
            if (!String.IsNullOrEmpty(imageUrl) && imageUrl.StartsWith("http"))
            {
                //TODO: REFACTORING
                string fileName = System.IO.Path.Combine(tempFolder, Guid.NewGuid().ToString() + ".png");
                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.DownloadFile(imageUrl, fileName);
                    }

                    toast.AddInlineImage(new Uri("file:///" + fileName));
                }
                catch (Exception ex)
                {
                    App.log.writeLine("Notification image download failed: " + ex.Message);
                }
            }

            if (!String.IsNullOrEmpty(audioUrl) && audioUrl.StartsWith("http"))
            {
                //TODO: REFACTORING
                string fileName = System.IO.Path.Combine(tempFolder, Guid.NewGuid().ToString() + ".wav");
                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.DownloadFile(audioUrl, fileName);
                    }

                    playAudio(fileName, duration);
                }
                catch (Exception ex)
                {
                    App.log.writeLine("Notification audio download failed: " + ex.Message);
                }
            }

            toast.Show();
        }

        private static void playAudio(string fileName, int duration)
        {
            new Thread(() =>
            {
                SoundPlayer player = new SoundPlayer();
                player.SoundLocation = fileName;
                player.Play();
                Thread.Sleep(duration);
                player.Stop();
                player.Dispose();

                //The sound is not needed anymore after playing
                try
                {
                    System.IO.File.Delete(fileName);
                }
                catch (Exception)
                {
                    //Removed later by cleanTempFolder
                }
            }).Start();
        }
    }
}
