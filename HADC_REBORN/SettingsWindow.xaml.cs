using HADC_REBORN.Class.Helpers;
using HADC_REBORN.Class.Sensors;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace HADC_REBORN
{
    //One row of the sensor list
    public class SensorItem : INotifyPropertyChanged
    {
        public ConfigEntry Entry;

        public SensorItem(ConfigEntry entry)
        {
            Entry = entry;
        }

        public string Title
        {
            get
            {
                string name = Entry.get("name");
                return (name == "" ? "(no name)" : name) + "  [" + (Entry.Section == "binary_sensor" ? "binary, " : "") + Entry.Platform + "]";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void refresh()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private enum FieldKind { Text, Choice, Check, Device }

        private class FieldDef
        {
            public string Key = "";
            public string Label = "";
            public FieldKind Kind = FieldKind.Text;
            public string? Group = null;
            //Only shown for these platforms, null shows it for all
            public string[]? Platforms = null;
            //Hidden for binary sensors, they have no number to work with
            public bool SensorOnly = false;
            public string[]? Options = null;
            public string? Hint = null;
        }

        private static readonly string[] sensorSections = { "sensor", "binary_sensor" };

        private static readonly KeyValuePair<string, string>[] platforms =
        {
            new KeyValuePair<string, string>("wmic", "wmic (Windows WMI query)"),
            new KeyValuePair<string, string>("network_interface", "network_interface (Wi-Fi)"),
            new KeyValuePair<string, string>("current_window", "current_window (active window title)"),
            new KeyValuePair<string, string>("uptime", "uptime (hours since start)"),
            new KeyValuePair<string, string>("consent_store", "consent_store (camera, microphone or location in use)"),
            new KeyValuePair<string, string>("restart_pending", "restart_pending (Windows wants a restart)"),
            new KeyValuePair<string, string>("ping", "ping (host reachable)"),
            new KeyValuePair<string, string>("microphone", "microphone (one microphone)"),
        };

        private static readonly FieldDef[] fields =
        {
            new FieldDef { Key = "name", Label = "Name" },
            new FieldDef { Key = "unique_id", Label = "Unique ID", Hint = "Home Assistant sees a changed ID as a new sensor." },

            new FieldDef { Key = "wmic_class", Label = "WMI class", Platforms = new[] { "wmic" } },
            new FieldDef { Key = "wmic_selector", Label = "WMI property", Platforms = new[] { "wmic" } },
            new FieldDef { Key = "wmic_namespace", Label = "WMI namespace", Platforms = new[] { "wmic" }, Hint = "For example root\\CIMV2. Empty uses root\\wmi." },
            new FieldDef { Key = "selector", Label = "Selector", Platforms = new[] { "network_interface" }, Hint = "Line of 'netsh wlan show interfaces' to read, for example SSID." },
            new FieldDef { Key = "deselector", Label = "Deselector", Platforms = new[] { "network_interface" }, Hint = "Skips lines that contain this, for example BSSID." },
            new FieldDef { Key = "consent_category", Label = "Category", Kind = FieldKind.Choice, Platforms = new[] { "consent_store" }, Options = new[] { "webcam", "microphone", "location" } },
            new FieldDef { Key = "host", Label = "Host", Platforms = new[] { "ping" } },
            new FieldDef { Key = "selector", Label = "Value", Kind = FieldKind.Choice, Platforms = new[] { "microphone" }, Options = new[] { "in_use", "muted", "volume" }, Hint = "'in_use' and 'muted' belong in a binary sensor, 'volume' in a sensor." },
            new FieldDef { Key = "device", Label = "Microphone", Kind = FieldKind.Device, Platforms = new[] { "microphone" } },

            new FieldDef { Key = "icon", Label = "Icon", Hint = "For example mdi:microphone." },
            new FieldDef { Key = "unit_of_measurement", Label = "Unit", SensorOnly = true },
            new FieldDef { Key = "device_class", Label = "Device class" },
            new FieldDef { Key = "state_class", Label = "State class", Kind = FieldKind.Choice, SensorOnly = true, Options = new[] { "", "measurement", "total", "total_increasing" } },
            new FieldDef { Key = "entity_category", Label = "Entity category", Kind = FieldKind.Choice, Options = new[] { "", "diagnostic", "config" } },
            new FieldDef { Key = "update_interval", Label = "Update interval (seconds)", Hint = "Empty sends a new value as soon as it changes, checked every 5 seconds." },
            new FieldDef { Key = "accuracy_decimals", Label = "Decimals", SensorOnly = true },
            new FieldDef { Key = "value_map", Label = "Value map", SensorOnly = true, Hint = "Texts for the values 0, 1, 2, ... separated by |" },
            new FieldDef { Key = "multiply", Label = "Multiply by", Group = "filters", SensorOnly = true },
            new FieldDef { Key = "divide", Label = "Divide by", Group = "filters", SensorOnly = true },
            new FieldDef { Key = "deduct", Label = "Subtract", Group = "filters", SensorOnly = true },
            new FieldDef { Key = "add", Label = "Add", Group = "filters", SensorOnly = true },
            new FieldDef { Key = "disabled", Label = "Disabled in Home Assistant", Kind = FieldKind.Check, Hint = "Only used when the sensor is registered for the first time." },
        };

        //Sensors the Microphone tab can switch on and off
        private static readonly string[] microphoneSensorIds = { "selected_microphone_in_use", "selected_microphone_muted", "selected_microphone_volume" };

        private static SettingsWindow? instance = null;

        private App app;
        private ConfigDocument document;
        private ObservableCollection<SensorItem> sensorItems = new ObservableCollection<SensorItem>();
        private DispatcherTimer statusTimer = new DispatcherTimer();

        //True while the code fills controls, so their change events are ignored
        private bool building = false;

        private string savedYaml = "";
        private string savedUrl = "";
        private string savedToken = "";
        private HashSet<string> savedUniqueIds = new HashSet<string>();

        public static void open()
        {
            if (instance != null)
            {
                if (instance.WindowState == WindowState.Minimized)
                {
                    instance.WindowState = WindowState.Normal;
                }
                instance.Activate();
                return;
            }

            try
            {
                instance = new SettingsWindow();
                instance.Show();
                instance.Activate();
            }
            catch (Exception ex)
            {
                instance = null;
                App.log.writeLine("Settings could not be opened: " + ex.Message);
                MessageBox.Show("Settings could not be opened: " + ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public SettingsWindow()
        {
            InitializeComponent();
            app = (App)App.Current;

            try
            {
                Icon = Imaging.CreateBitmapSourceFromHBitmap(HADC_REBORN.Resource.ha_icon.ToBitmap().GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch (Exception)
            {
                //The default window icon is fine too
            }

            //Read the file itself, it may have been edited by hand since the app started
            app.loadYAMLComfig();
            document = ConfigDocument.fromLoader(new YamlLoader(app.getConfigFilePath()));

            building = true;

            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            savedUrl = config.AppSettings.Settings["url"].Value;
            savedToken = SecretStore.read(config, "token");
            url.Text = savedUrl;
            token.Password = savedToken;

            autostart.IsChecked = document.getFlag("autostart", true);
            debug.IsChecked = document.getFlag("debug");
            keys.IsChecked = document.EmptySections.Contains("keys");

            foreach (ConfigEntry entry in document.Entries)
            {
                if (sensorSections.Contains(entry.Section))
                {
                    sensorItems.Add(new SensorItem(entry));
                }
            }
            sensorList.ItemsSource = sensorItems;

            loadMicrophones();
            loadMicrophoneSensors();

            building = false;

            commitGeneral();
            rememberSaved();

            if (sensorItems.Count > 0)
            {
                sensorList.SelectedIndex = 0;
            }
            else
            {
                buildForm();
            }

            statusTimer.Interval = TimeSpan.FromSeconds(2);
            statusTimer.Tick += statusTimer_Tick;
            statusTimer.Start();
            updateStatus();
        }

        private void rememberSaved()
        {
            savedYaml = document.toYaml();
            savedUniqueIds = new HashSet<string>(sensorItems.Select(item => item.Entry.get("unique_id")));
        }

        //---------------------------------------------------------------- General

        private void commitGeneral()
        {
            document.Options["autostart"] = autostart.IsChecked == true ? "true" : "false";
            document.Options["debug"] = debug.IsChecked == true ? "true" : "false";

            document.EmptySections.RemoveAll(section => section == "keys");
            if (keys.IsChecked == true)
            {
                document.EmptySections.Add("keys");
            }
        }

        private void openYaml_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("notepad", "\"" + app.getConfigFilePath() + "\"");
        }

        //---------------------------------------------------------------- Sensors

        private SensorItem? selectedSensor()
        {
            return sensorList.SelectedItem as SensorItem;
        }

        private string uniqueId(string wanted)
        {
            HashSet<string> used = new HashSet<string>(sensorItems.Select(item => item.Entry.get("unique_id")));
            string candidate = wanted;
            for (int i = 2; used.Contains(candidate); i++)
            {
                candidate = wanted + "_" + i;
            }
            return candidate;
        }

        private void addEntry(ConfigEntry entry, bool select)
        {
            document.Entries.Add(entry);
            SensorItem item = new SensorItem(entry);
            sensorItems.Add(item);

            if (select)
            {
                sensorList.SelectedItem = item;
                sensorList.ScrollIntoView(item);
            }
        }

        private void removeItem(SensorItem item)
        {
            document.Entries.Remove(item.Entry);
            sensorItems.Remove(item);
        }

        private void addSensor_Click(object sender, RoutedEventArgs e)
        {
            ConfigEntry entry = new ConfigEntry() { Section = "sensor", Platform = "wmic" };
            entry.set("name", "New sensor");
            entry.set("unique_id", uniqueId("new_sensor"));
            entry.set("icon", "mdi:gauge");
            addEntry(entry, true);
        }

        private void duplicateSensor_Click(object sender, RoutedEventArgs e)
        {
            SensorItem? item = selectedSensor();
            if (item == null)
            {
                return;
            }

            ConfigEntry entry = item.Entry.clone();
            entry.set("name", entry.get("name") + " copy");
            entry.set("unique_id", uniqueId(entry.get("unique_id") + "_copy"));
            addEntry(entry, true);
        }

        private void removeSensor_Click(object sender, RoutedEventArgs e)
        {
            SensorItem? item = selectedSensor();
            if (item == null)
            {
                return;
            }

            int index = sensorList.SelectedIndex;
            removeItem(item);
            sensorList.SelectedIndex = Math.Min(index, sensorItems.Count - 1);
        }

        private void sensorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            buildForm();
        }

        private void addRow(string label, FrameworkElement control, string? hint = null)
        {
            int row = sensorForm.RowDefinitions.Count;
            sensorForm.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });

            TextBlock text = new TextBlock() { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, hint == null ? 6 : 0) };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 0);
            sensorForm.Children.Add(text);

            control.Margin = new Thickness(0, 0, 0, hint == null ? 6 : 0);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            sensorForm.Children.Add(control);

            if (hint != null)
            {
                addNote(hint, 1);
            }
        }

        private void addNote(string note, int column = 0)
        {
            int row = sensorForm.RowDefinitions.Count;
            sensorForm.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });

            TextBlock text = new TextBlock() { Text = note, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 1, 0, 6) };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, column);
            Grid.SetColumnSpan(text, 2 - column);
            sensorForm.Children.Add(text);
        }

        private ComboBox choice(IEnumerable<KeyValuePair<string, string>> options, string current, Action<string> changed)
        {
            List<KeyValuePair<string, string>> items = options.ToList();
            if (!items.Any(item => item.Key == current))
            {
                //A value written by hand that the list does not know stays selectable
                items.Add(new KeyValuePair<string, string>(current, current));
            }

            ComboBox combo = new ComboBox() { Height = 24, DisplayMemberPath = "Value", SelectedValuePath = "Key", ItemsSource = items, SelectedValue = current };
            combo.SelectionChanged += (sender, e) =>
            {
                if (!building && combo.SelectedValue is string value)
                {
                    changed(value);
                }
            };
            return combo;
        }

        private static IEnumerable<KeyValuePair<string, string>> asOptions(string[] values)
        {
            return values.Select(value => new KeyValuePair<string, string>(value, value == "" ? "(none)" : value));
        }

        private IEnumerable<KeyValuePair<string, string>> microphoneOptions()
        {
            List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
            options.Add(new KeyValuePair<string, string>("", "The one picked on the Microphone tab"));

            try
            {
                foreach (MicrophoneInfo info in Microphone.list())
                {
                    options.Add(new KeyValuePair<string, string>(info.Id, info.Name));
                }
            }
            catch (Exception ex)
            {
                App.log.writeLine("Microphones could not be listed: " + ex.Message);
            }

            return options;
        }

        //Fills the right side of the Sensors tab for the selected sensor
        private void buildForm()
        {
            SensorItem? item = selectedSensor();

            building = true;
            sensorForm.Children.Clear();
            sensorForm.RowDefinitions.Clear();

            duplicateSensor.IsEnabled = item != null;
            removeSensor.IsEnabled = item != null;

            if (item == null)
            {
                addNote("Select a sensor on the left, or add one.");
                building = false;
                return;
            }

            ConfigEntry entry = item.Entry;

            //The form depends on type and platform, so it is built again when one of those changes
            Action rebuild = () => Dispatcher.BeginInvoke(new Action(() =>
            {
                item.refresh();
                if (selectedSensor() == item)
                {
                    buildForm();
                }
            }));

            addRow("Type", choice(asOptions(sensorSections), entry.Section, value => { entry.Section = value; rebuild(); }), "'binary_sensor' is for on/off values.");
            addRow("Platform", choice(platforms, entry.Platform, value => { entry.Platform = value; rebuild(); }));

            HashSet<string> shown = new HashSet<string>();

            foreach (FieldDef field in fields)
            {
                if (field.Platforms != null && !field.Platforms.Contains(entry.Platform))
                {
                    continue;
                }

                if (field.Group == null)
                {
                    shown.Add(field.Key);
                }

                if (field.SensorOnly && entry.Section == "binary_sensor")
                {
                    continue;
                }

                string current = entry.get(field.Key, field.Group);

                switch (field.Kind)
                {
                    case FieldKind.Choice:
                        addRow(field.Label, choice(asOptions(field.Options!), current, value => entry.set(field.Key, value, field.Group)), field.Hint);
                        break;

                    case FieldKind.Device:
                        addRow(field.Label, choice(microphoneOptions(), current, value => entry.set(field.Key, value, field.Group)), field.Hint);
                        break;

                    case FieldKind.Check:
                        CheckBox check = new CheckBox() { IsChecked = String.Equals(current, "true", StringComparison.OrdinalIgnoreCase), VerticalAlignment = VerticalAlignment.Center };
                        check.Click += (sender, e) => entry.set(field.Key, check.IsChecked == true ? "true" : "", field.Group);
                        addRow(field.Label, check, field.Hint);
                        break;

                    default:
                        TextBox box = new TextBox() { Text = current, Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
                        box.TextChanged += (sender, e) =>
                        {
                            if (building)
                            {
                                return;
                            }

                            entry.set(field.Key, box.Text, field.Group);
                            if (field.Key == "name")
                            {
                                item.refresh();
                            }
                        };
                        addRow(field.Label, box, field.Hint);
                        break;
                }
            }

            //Options this form has no field for are left alone
            List<string> other = entry.Fields.Keys.Where(key => !shown.Contains(key)).Concat(entry.Groups.Keys.Where(key => key != "filters")).ToList();
            if (other.Count > 0)
            {
                addNote("Also in configuration.yaml for this sensor, kept as it is: " + String.Join(", ", other));
            }

            building = false;
        }

        //---------------------------------------------------------------- Microphone

        private string selectedMicrophone()
        {
            return document.Options.TryGetValue("microphone", out string? id) ? id : "";
        }

        private void loadMicrophones()
        {
            string selected = selectedMicrophone();

            List<MicrophoneInfo> microphones = new List<MicrophoneInfo>();
            microphones.Add(new MicrophoneInfo() { Id = "", Name = "Windows default microphone" });

            try
            {
                microphones.AddRange(Microphone.list());
            }
            catch (Exception ex)
            {
                App.log.writeLine("Microphones could not be listed: " + ex.Message);
            }

            if (!microphones.Any(info => info.Id == selected))
            {
                microphones.Add(new MicrophoneInfo() { Id = selected, Name = "Not connected (" + selected + ")" });
            }

            bool wasBuilding = building;
            building = true;
            microphone.ItemsSource = microphones;
            microphone.SelectedValue = selected;
            building = wasBuilding;
        }

        private ConfigEntry? findSensor(string uniqueId)
        {
            return sensorItems.FirstOrDefault(item => item.Entry.get("unique_id") == uniqueId)?.Entry;
        }

        private void loadMicrophoneSensors()
        {
            micInUse.IsChecked = findSensor(microphoneSensorIds[0]) != null;
            micMuted.IsChecked = findSensor(microphoneSensorIds[1]) != null;
            micVolume.IsChecked = findSensor(microphoneSensorIds[2]) != null;
        }

        private static ConfigEntry microphoneSensor(string uniqueId)
        {
            ConfigEntry entry = new ConfigEntry() { Platform = "microphone" };

            switch (uniqueId)
            {
                case "selected_microphone_in_use":
                    entry.Section = "binary_sensor";
                    entry.set("selector", "in_use");
                    entry.set("name", "Selected Microphone in use");
                    entry.set("icon", "mdi:microphone");
                    break;
                case "selected_microphone_muted":
                    entry.Section = "binary_sensor";
                    entry.set("selector", "muted");
                    entry.set("name", "Selected Microphone muted");
                    entry.set("icon", "mdi:microphone-off");
                    break;
                default:
                    entry.Section = "sensor";
                    entry.set("selector", "volume");
                    entry.set("name", "Selected Microphone volume");
                    entry.set("icon", "mdi:volume-high");
                    entry.set("unit_of_measurement", "%");
                    entry.set("accuracy_decimals", "0");
                    break;
            }

            entry.set("unique_id", uniqueId);
            return entry;
        }

        private void microphoneSensor_Click(object sender, RoutedEventArgs e)
        {
            bool[] wanted = { micInUse.IsChecked == true, micMuted.IsChecked == true, micVolume.IsChecked == true };

            for (int i = 0; i < microphoneSensorIds.Length; i++)
            {
                string id = microphoneSensorIds[i];
                SensorItem? existing = sensorItems.FirstOrDefault(item => item.Entry.get("unique_id") == id);

                if (wanted[i] && existing == null)
                {
                    addEntry(microphoneSensor(id), false);
                }
                else if (!wanted[i] && existing != null)
                {
                    removeItem(existing);
                }
            }
        }

        private void microphone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (building || !(microphone.SelectedValue is string id))
            {
                return;
            }

            if (id == "")
            {
                document.Options.Remove("microphone");
            }
            else
            {
                document.Options["microphone"] = id;
            }

            updateMicrophoneStatus();
        }

        private void refreshMicrophones_Click(object sender, RoutedEventArgs e)
        {
            loadMicrophones();
            updateMicrophoneStatus();
        }

        private void updateMicrophoneStatus()
        {
            try
            {
                MicrophoneStatus state = Microphone.getStatus(selectedMicrophone());
                if (!state.Connected)
                {
                    microphoneStatus.Text = "This microphone is not connected.";
                    return;
                }

                microphoneStatus.Text = state.Name + "\nIn use: " + (state.InUse ? "yes" : "no") + "    Muted: " + (state.Muted ? "yes" : "no") + "    Volume: " + state.Volume + "%";
            }
            catch (Exception ex)
            {
                microphoneStatus.Text = "The microphone could not be read: " + ex.Message;
            }
        }

        //---------------------------------------------------------------- Status

        private void tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //Lists and dropdowns inside the tabs raise this event too
            if (e.OriginalSource != tabs || building)
            {
                return;
            }

            loadMicrophoneSensors();
            updateStatus();
        }

        private void statusTimer_Tick(object? sender, EventArgs e)
        {
            updateStatus();
        }

        private void updateStatus()
        {
            api_status.Foreground = app.haApiConnector != null && app.haApiConnector.connected() ? Brushes.Green : Brushes.Red;
            ws_status.Foreground = app.haWsConnector != null && app.haWsConnector.connected() ? Brushes.Green : Brushes.Red;

            if (tabs.SelectedIndex == 3)
            {
                updateMicrophoneStatus();
            }
        }

        //---------------------------------------------------------------- Save

        private static bool isNumber(string value)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        //Returns what is wrong, or null when everything can be saved
        private string? validate(out SensorItem? offending)
        {
            HashSet<string> uniqueIds = new HashSet<string>();

            foreach (SensorItem item in sensorItems)
            {
                ConfigEntry entry = item.Entry;
                offending = item;

                if (!Regex.IsMatch(entry.Platform, "^[a-z0-9_]+$"))
                {
                    return "The platform can only contain lower case letters, digits and '_'.";
                }

                if (entry.get("name") == "")
                {
                    return "A sensor needs a name.";
                }

                string id = entry.get("unique_id");
                if (id == "")
                {
                    return "'" + entry.get("name") + "' needs a unique ID.";
                }

                if (!uniqueIds.Add(id))
                {
                    return "The unique ID '" + id + "' is used by more than one sensor.";
                }

                foreach (string value in entry.Fields.Values.Concat(entry.Groups.Values.SelectMany(group => group.Values)))
                {
                    if (!ConfigDocument.isStorable(value))
                    {
                        return "'" + value + "' can not be stored: the characters # and \" are not supported in configuration.yaml.";
                    }
                }

                string[] required = entry.Platform switch
                {
                    "wmic" => new[] { "wmic_class", "wmic_selector" },
                    "network_interface" => new[] { "selector" },
                    "consent_store" => new[] { "consent_category" },
                    "ping" => new[] { "host" },
                    "microphone" => new[] { "selector" },
                    _ => new string[0],
                };

                foreach (string key in required)
                {
                    if (entry.get(key) == "")
                    {
                        FieldDef field = fields.First(definition => definition.Key == key && (definition.Platforms == null || definition.Platforms.Contains(entry.Platform)));
                        return "'" + entry.get("name") + "' needs a value for '" + field.Label + "'.";
                    }
                }

                string interval = entry.get("update_interval");
                if (interval != "" && !isNumber(interval))
                {
                    return "The update interval of '" + entry.get("name") + "' has to be a number of seconds.";
                }

                string decimals = entry.get("accuracy_decimals");
                if (decimals != "" && !int.TryParse(decimals, out _))
                {
                    return "The decimals of '" + entry.get("name") + "' have to be a whole number.";
                }

                foreach (string key in new[] { "multiply", "divide", "deduct", "add" })
                {
                    string filter = entry.get(key, "filters");
                    if (filter != "" && (!isNumber(filter) || (key == "divide" && double.Parse(filter, CultureInfo.InvariantCulture) == 0)))
                    {
                        return "The filters of '" + entry.get("name") + "' have to be numbers, with a dot for decimals. Dividing by 0 is not possible.";
                    }
                }
            }

            offending = null;
            return null;
        }

        private bool connectionChanged()
        {
            return url.Text.Trim().TrimEnd('/') != savedUrl.TrimEnd('/') || token.Password != savedToken;
        }

        private void save_Click(object sender, RoutedEventArgs e)
        {
            commitGeneral();

            string? problem = validate(out SensorItem? offending);
            if (problem != null)
            {
                tabs.SelectedIndex = 2;
                sensorList.SelectedItem = offending;
                status.Text = "Not saved: " + problem;
                MessageBox.Show(problem, "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (connectionChanged() && (url.Text.Trim() == "" || token.Password == ""))
            {
                tabs.SelectedIndex = 0;
                status.Text = "Not saved: fill in the URL and the API token.";
                return;
            }

            try
            {
                document.save(app.getConfigFilePath());
            }
            catch (Exception ex)
            {
                App.log.writeLine("'configuration.yaml' could not be saved: " + ex.Message);
                status.Text = "Not saved: " + ex.Message;
                MessageBox.Show("configuration.yaml could not be saved: " + ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool removed = savedUniqueIds.Except(sensorItems.Select(item => item.Entry.get("unique_id"))).Any();
            rememberSaved();
            App.log.writeLine("'configuration.yaml' saved from settings");

            string result;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                if (connectionChanged())
                {
                    //Connecting again also loads the new configuration and registers every sensor
                    bool started = app.saveConnection(url.Text, token.Password);
                    savedUrl = url.Text.Trim().TrimEnd('/');
                    savedToken = token.Password;
                    result = started ? "Saved and connected." : "Saved, but connecting to Home Assistant failed. See the log.";
                }
                else if (app.applyConfiguration())
                {
                    result = App.apiWrapper != null ? "Saved. The sensors are being registered with Home Assistant." : "Saved.";
                }
                else
                {
                    result = "Saved, but the new configuration could not be loaded. See the log.";
                }
            }
            catch (Exception ex)
            {
                App.log.writeLine("Applying the settings failed: " + ex.Message);
                result = "Saved, but applying it failed: " + ex.Message;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            if (removed)
            {
                result += " Removed sensors stay in Home Assistant until you delete them there.";
            }

            status.Text = result;
            updateStatus();
        }

        private void close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            commitGeneral();
            if (document.toYaml() == savedYaml && !connectionChanged())
            {
                return;
            }

            if (MessageBox.Show("Close without saving the changes?", "Settings", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            statusTimer.Stop();
            instance = null;
        }
    }
}
