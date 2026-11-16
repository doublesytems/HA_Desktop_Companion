using HADC_REBORN.Class.Helpers;
using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;

using From = System.Windows.Forms;
using System.Diagnostics;
using System.Net.Mail;
using AutoUpdaterDotNET;
using System.Reflection.Metadata.Ecma335;
using System.IO;
using System.ComponentModel;
using System.Security.Policy;
using HADC_REBORN.Class.HomeAssistant;
using HADC_REBORN.Class.HomeAssistant.Objects;
using HADC_REBORN.Class.Sensors;
using System.Reflection;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Globalization;
using Windows.Devices.Sensors;
using System.Runtime.ExceptionServices;
using HADC_REBORN.Class.Actions;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Windows.UI.ViewManagement;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Diagnostics.Eventing.Reader;
using System.Windows.Media.Imaging;

namespace HADC_REBORN
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
#if DEBUG
        private string appDir = Directory.GetCurrentDirectory();
#else
        private string appDir = AppDomain.CurrentDomain.BaseDirectory;
#endif

        public static NotifyIcon? icon = null;
        public static Logger log = new Logger();
        public static YamlLoader yamlLoader;
        public bool initializing = true;

        public ApiConnector? haApiConnector = null;
        public static ApiWrapper? apiWrapper = null;

        public WsConnector? haWsConnector = null;
        public static WsWrapper? wsWrapper = null;

        public static string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        //Creates missing settings, so the settings file does not have to be shipped with (and overwritten by) an update
        private static void ensureSettings()
        {
            try
            {
                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                bool changed = false;

                foreach (string key in new string[] { "url", "token", "webhook_id", "remote_url", "cloud_url", "secret" })
                {
                    if (config.AppSettings.Settings[key] == null)
                    {
                        config.AppSettings.Settings.Add(key, "");
                        changed = true;
                    }
                }

                if (changed)
                {
                    config.Save(ConfigurationSaveMode.Modified);
                    log.writeLine("Settings file created");
                }
            }
            catch (Exception ex)
            {
                log.writeLine("Settings file could not be prepared: " + ex.Message);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.FirstChanceException += GlobalExceptionFunction;
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionFunction;

            ensureSettings();

            App.icon = new NotifyIcon();

            icon.DoubleClick += new EventHandler(icon_Click);
            icon.Icon = HADC_REBORN.Resource.ha_icon;

            //Count for icon dakt mode change
            Theme.setTheme(Theme.isLightTheme());
           
            icon.Text = System.AppDomain.CurrentDomain.FriendlyName;
            icon.Visible = true;

            icon.ContextMenuStrip = new ContextMenuStrip();
            icon.ContextMenuStrip.Items.Add("Home Assistant", null, OnHomeAssistant_Click);
            icon.ContextMenuStrip.Items.Add("Settings", null, OnSettings_Click);
            icon.ContextMenuStrip.Items.Add("Log", null, OnLog_Click);
            icon.ContextMenuStrip.Items.Add("Send Test Notification", null, OnTestNotification_Click);
            icon.ContextMenuStrip.Items.Add("Quit", null, OnQuit_Click);

            base.OnStartup(e);

            //On Color mode 
            UISettings settings = new UISettings();
            Theme.setTheme(Theme.isColorLight(settings.GetColorValue(UIColorType.Background)));
            settings.ColorValuesChanged += theme_Changed;

            log.writeLine("starting version: " + version);
        }

        public void loadYAMLComfig(bool force = false)
        {
            if (yamlLoader != null && !force)
            {
                return;
            }

            log.writeLine("looking for 'configuration.yaml'");
            string configFilePath = Path.Combine(appDir, "configuration.yaml");
            if (!File.Exists(configFilePath))
            {
                log.writeLine("'configuration.yaml' not found creating new one!");
                File.WriteAllBytes(configFilePath, HADC_REBORN.Resource.configuration);
            }
            else
            {
                log.writeLine("'configuration.yaml' found!");
            }
            yamlLoader = new YamlLoader(configFilePath);
            applyOptions();
        }

        public string getConfigFilePath()
        {
            return Path.Combine(appDir, "configuration.yaml");
        }

        //Top level options of configuration.yaml that are used outside the sensors
        private void applyOptions()
        {
#if !DEBUG
            Logger.verbose = yamlLoader.getFlag("debug");
#endif
            log.writeLine("detailed logging: " + (Logger.verbose ? "on" : "off (set 'debug: true' in configuration.yaml to enable)"));

            Microphone.selectedDevice = yamlLoader.getOption("microphone") ?? "";
        }

        //Starts with Windows unless configuration.yaml says "autostart: false"
        private void applyAutostart()
        {
            if (yamlLoader.getFlag("autostart", true))
            {
                Autostart.register();
                log.writeLine("Autostart registered");
            }
            else
            {
                Autostart.unregister();
                log.writeLine("Autostart turned off");
            }
        }

        //Takes a changed configuration.yaml into use without restarting the app
        public bool applyConfiguration()
        {
            if (yamlLoader == null)
            {
                loadYAMLComfig();
                return yamlLoader.getConfigurationData() != null;
            }

            if (!yamlLoader.reload())
            {
                log.writeLine("'configuration.yaml' could not be read, keeping the previous configuration");
                return false;
            }

            log.writeLine("'configuration.yaml' loaded again");
            applyOptions();

            try
            {
                applyAutostart();
            }
            catch (Exception ex)
            {
                log.writeLine(ex.Message);
            }

            //New and changed sensors have to be registered before their values are accepted
            apiWrapper?.requestSensorRegistration();
            return true;
        }

        //Stores the connection settings and connects with them
        public bool saveConnection(string url, string token)
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            config.AppSettings.Settings["url"].Value = url.Trim().TrimEnd('/');
            SecretStore.write(config, "token", token);

            config.AppSettings.Settings["remote_url"].Value = "";
            config.AppSettings.Settings["cloud_url"].Value = "";
            config.AppSettings.Settings["webhook_id"].Value = "";
            config.AppSettings.Settings["secret"].Value = "";

            config.Save(ConfigurationSaveMode.Modified);
            log.writeLine("Settings Saved");

            Stop();
            return Start();
        }

        public Dictionary<string, Dictionary<string, Dictionary<string, List<Dictionary<string, dynamic>>>>> getYAMLComfig()
        {
            return yamlLoader.getConfigurationData();
        }

        private void theme_Changed(UISettings sender, object args)
        {
            Theme.setTheme(Theme.isColorLight(sender.GetColorValue(UIColorType.Background)));
        }

        [ThreadStatic]
        private static bool loggingException;

        //Every exception, also the handled ones, only interesting with detailed logging
        static void GlobalExceptionFunction(object source, FirstChanceExceptionEventArgs eventArgs)
        {
            if (!Logger.verbose || loggingException)
            {
                return;
            }

            loggingException = true;
            try
            {
                log.writeLine("[" + AppDomain.CurrentDomain.FriendlyName + "]" + eventArgs.Exception.ToString(), 3);
            }
            finally
            {
                loggingException = false;
            }
        }

        //Exceptions nobody handled are always logged
        static void UnhandledExceptionFunction(object source, UnhandledExceptionEventArgs eventArgs)
        {
            log.writeLine("[" + AppDomain.CurrentDomain.FriendlyName + "] UNHANDLED " + eventArgs.ExceptionObject?.ToString(), 3);
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {

        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            AutoUpdater.ShowRemindLaterButton = false;
            AutoUpdater.Start("https://github.com/doublesytems/HA_Desktop_Companion/releases/latest/download/meta.xml");
        }

        public bool Start()
        {
            loadYAMLComfig(true);

            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            string url = config.AppSettings.Settings["url"].Value;
            //Encrypt secrets that an older version saved as plain text
            if (SecretStore.migrate(config, new string[] { "token", "webhook_id", "secret" }))
            {
                config.Save(ConfigurationSaveMode.Modified);
                log.writeLine("Stored secrets encrypted");
            }

            string token = SecretStore.read(config, "token");
            log.setSecreets(new string[] { token, url.Replace("http://", "").Replace("https://", "")});


            if (String.IsNullOrEmpty(url) || String.IsNullOrEmpty(token))
            {
                log.writeLine("URL or Token not fount!!");
                return false;
            }

            //int pingLoopIndex = 0;
            //do
            //{
            //    log.writeLine("Waiting ntil server response!");
            //    pingLoopIndex++;
            //} while (!Network.PingHost((new Uri(url)).Host) && pingLoopIndex < 5);

            try
            {
                log.writeLine(url);
                haApiConnector = new ApiConnector(url, token);
                apiWrapper = new ApiWrapper(yamlLoader, haApiConnector, config);
                apiWrapper.connect();
                log.writeLine("RestAPI registered");
                log.setSecreets(new string[] { token, url.Replace("http://", "").Replace("https://", ""), haApiConnector.getSecret(), haApiConnector.getWebhookID() });
            }
            catch (Exception ex)
            {
                log.writeLine("Failed to initialize RestAPI" + ex.Message);
                return false;
            }

            if (String.IsNullOrEmpty(haApiConnector.getWebhookID()))
            {
                log.writeLine("Failed to get webhook_id from RestAPI");
                return false;
            }

            try
            {
                string wsUrl = url.Replace("http", "ws");
                log.writeLine(wsUrl);
                haWsConnector = new WsConnector(wsUrl, token, haApiConnector.getWebhookID());
                wsWrapper = new WsWrapper(yamlLoader, haWsConnector);
                wsWrapper.Connect();
                log.writeLine("Websocket registered");
            }
            catch (Exception ex)
            {
                log.writeLine("Failed to initialize WebbSocket" + ex.Message);
                return false;
            }

            NetworkChange.NetworkAvailabilityChanged -= GetNetworkChange_NetworkAvailabilityChanged;
            NetworkChange.NetworkAvailabilityChanged += GetNetworkChange_NetworkAvailabilityChanged;

            try
            {
                applyAutostart();
            }
            catch (Exception ex)
            {
                log.writeLine("Autostart registration failed" + ex.Message);
                return false;
            }

            log.writeLine("Initialization Compleeted");
            return true;
        }

        private void GetNetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            //Runs on a background thread, an unhandled exception here would crash the app
            try
            {
                if (e.IsAvailable)
                {
                    log.writeLine("Network Connected!");
                    wsWrapper?.restart();
                    apiWrapper?.restart();
                }
                else
                {
                    log.writeLine("Network Disconnected!");
                    wsWrapper?.Disconnect();
                    apiWrapper?.disconnect();
                }
            }
            catch (Exception ex)
            {
                log.writeLine("Network change handling failed: " + ex.Message);
            }
        }

        public void Stop()
        {
            log.writeLine("stoping RestAPI");

            NetworkChange.NetworkAvailabilityChanged -= GetNetworkChange_NetworkAvailabilityChanged;

            try
            {
                wsWrapper?.Disconnect();
                apiWrapper?.disconnect();
            }
            catch (Exception ex)
            {
                log.writeLine("Stop failed: " + ex.Message);
            }
        }

        public bool isRunning()
        {
            return (haApiConnector.connected() && haWsConnector.connected());
        }

        protected override void OnExit(ExitEventArgs e)
        {
            icon.Dispose();

            base.OnExit(e);
        }

        private void OnSettings_Click(object? sender, EventArgs e)
        {
            SettingsWindow.open();
        }

        private void OnLog_Click(object? sender, EventArgs e)
        {
            Process.Start("notepad", log.getLogPath());
        }

        private void OnHomeAssistant_Click(object? sender, EventArgs e)
        {
            string url = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).AppSettings.Settings["url"].Value;

            //Only open real web addresses
            if (System.Uri.TryCreate(url, UriKind.Absolute, out System.Uri? haUri) && (haUri.Scheme == System.Uri.UriSchemeHttp || haUri.Scheme == System.Uri.UriSchemeHttps))
            {
                Process.Start(new ProcessStartInfo(haUri.ToString()) { UseShellExecute = true });
            }
            else
            {
                log.writeLine("Home Assistant URL is not set");
            }
        }

        private void OnQuit_Click(object? sender, EventArgs e)
        {
            Close();
        }

        private void OnTestNotification_Click(object? sender, EventArgs e)
        {
            Notification.Spawn("test");
        }

        private void icon_Click(Object? sender, EventArgs e)
        {
            MainWindow main = App.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (main != null)
            {
                main.Focus();
            }
            else
            {
                new MainWindow().Show();
            }
        }

        public static void Close()
        {
            Environment.Exit(0);
        }
    }
}
