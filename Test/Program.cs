using HADC_REBORN.Class.Helpers;
using HADC_REBORN.Class.Sensors;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

int failures = 0;

void check(string name, bool passed)
{
    Console.WriteLine((passed ? "PASS  " : "FAIL  ") + name);
    if (!passed)
    {
        failures++;
    }
}

JToken load(string path)
{
    return JToken.Parse(JsonConvert.SerializeObject(new YamlLoader(path).getConfigurationData()));
}

//Writing a configuration and reading it again has to give the same data
bool roundTrip(string path)
{
    string copyPath = Path.Combine(Path.GetTempPath(), "hadc_test_" + Guid.NewGuid().ToString("N") + ".yaml");
    try
    {
        File.WriteAllText(copyPath, ConfigDocument.fromLoader(new YamlLoader(path)).toYaml());
        return JToken.DeepEquals(load(path), load(copyPath));
    }
    finally
    {
        File.Delete(copyPath);
    }
}

check("round trip of configuration.yaml", roundTrip("./configuration.yaml"));

string tempPath = Path.Combine(Path.GetTempPath(), "hadc_test_" + Guid.NewGuid().ToString("N") + ".yaml");
try
{
    File.WriteAllText(tempPath, string.Join("\n", new string[] {
        "debug: true # comment",
        "microphone: \"{0.0.1.00000000}.{7a1b2c3d-0000-4000-8000-123456789abc}\"",
        "keys:",
        "sensor:",
        "  - platform: wmic",
        "    wmic_class: Win32_LogicalDisk",
        "    wmic_namespace: root\\CIMV2",
        "    name: \"Disk C: free\"",
        "    unique_id: disk",
        "    icon: \"mdi:tape-drive\"",
        "    filters:",
        "      - divide: 1073741824",
        "      - add: 1.5",
        "    accuracy_decimals: 1",
        "  - platform: uptime",
        "    name: Uptime",
        "    unique_id: uptime",
        "  - platform: wmic",
        "    wmic_class: Win32_Battery",
        "    name: Battery",
        "    unique_id: battery",
        "binary_sensor:",
        "  - platform: microphone",
        "    selector: in_use",
        "    name: Selected Microphone in use",
        "    unique_id: selected_microphone_in_use",
    }));

    check("round trip with options, keys and filters", roundTrip(tempPath));

    YamlLoader loader = new YamlLoader(tempPath);
    ConfigDocument document = ConfigDocument.fromLoader(loader);

    check("option is read", loader.getFlag("debug") && document.getFlag("debug"));
    check("missing option uses the default", loader.getFlag("autostart", true) && !loader.getFlag("autostart"));
    check("microphone id is read", loader.getOption("microphone") == "{0.0.1.00000000}.{7a1b2c3d-0000-4000-8000-123456789abc}");
    check("keys switch is read", document.EmptySections.Contains("keys") && loader.getConfigurationData().ContainsKey("keys"));
    check("all sensors are read", document.Entries.Count == 4);
    check("value with a colon keeps its spaces", document.Entries.Any(entry => entry.get("name") == "Disk C: free"));
    check("filters are read", document.Entries.Any(entry => entry.get("divide", "filters") == "1073741824" && entry.get("add", "filters") == "1.5"));

    //Change it the way the settings window does and read the saved file back
    document.EmptySections.Remove("keys");
    document.Options["autostart"] = "false";
    document.Entries.RemoveAll(entry => entry.get("unique_id") == "uptime");
    ConfigEntry added = new ConfigEntry() { Section = "sensor", Platform = "microphone" };
    added.set("selector", "volume");
    added.set("name", "Selected Microphone volume");
    added.set("unique_id", "selected_microphone_volume");
    added.set("unit_of_measurement", "%");
    document.Entries.Add(added);
    document.Entries.First(entry => entry.get("unique_id") == "disk").set("divide", "", "filters");
    document.save(tempPath);

    check("backup is written", File.Exists(tempPath + ".bak"));
    check("file can be loaded again", loader.reload());

    ConfigDocument saved = ConfigDocument.fromLoader(loader);
    check("keys switch is removed", !loader.getConfigurationData().ContainsKey("keys"));
    check("changed option is saved", !loader.getFlag("autostart", true));
    check("removed sensor is gone", !saved.Entries.Any(entry => entry.get("unique_id") == "uptime"));
    check("added sensor is saved", saved.Entries.Any(entry => entry.Platform == "microphone" && entry.Section == "sensor" && entry.get("unit_of_measurement") == "%"));
    check("removed filter is gone", saved.Entries.First(entry => entry.get("unique_id") == "disk").get("divide", "filters") == "" && saved.Entries.First(entry => entry.get("unique_id") == "disk").get("add", "filters") == "1.5");
    check("other sensors are kept", saved.Entries.Count == 4 && saved.Entries.Any(entry => entry.get("unique_id") == "battery"));

    //Without any sensor the file has no sensor section at all
    document.Entries.Clear();
    document.save(tempPath);
    check("file without sensors can be loaded", loader.reload() && !loader.getConfigurationData().ContainsKey("sensor"));
}
finally
{
    File.Delete(tempPath);
    File.Delete(tempPath + ".bak");
}

//Not a pass or fail, this depends on the computer
try
{
    List<MicrophoneInfo> microphones = Microphone.list();
    Console.WriteLine("INFO  microphones found: " + microphones.Count);
    foreach (MicrophoneInfo info in microphones)
    {
        MicrophoneStatus status = Microphone.getStatus(info.Id);
        Console.WriteLine("INFO    " + info.Name + " - in use: " + status.InUse + ", muted: " + status.Muted + ", volume: " + status.Volume + "%");
    }

    MicrophoneStatus defaultStatus = Microphone.getStatus("");
    Console.WriteLine("INFO  default microphone: " + (defaultStatus.Connected ? defaultStatus.Name : "none"));
    Console.WriteLine("INFO  in_use sensor value: " + Microphone.GetValue("in_use"));
    check("unknown microphone is reported as not connected", !Microphone.getStatus("{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}").Connected);
}
catch (Exception ex)
{
    check("microphones can be read: " + ex.Message, false);
}

Console.WriteLine(failures == 0 ? "All checks passed" : failures + " check(s) failed");
return failures == 0 ? 0 : 1;
