using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HADC_REBORN.Class.Helpers
{
    //One "- platform: ..." block of configuration.yaml
    public class ConfigEntry
    {
        public string Section = "sensor";
        public string Kind = "platform";
        public string Platform = "";
        public Dictionary<string, string> Fields = new Dictionary<string, string>();
        //Nested lists like "filters:"
        public Dictionary<string, Dictionary<string, string>> Groups = new Dictionary<string, Dictionary<string, string>>();

        public string get(string key, string? group = null)
        {
            if (group == null)
            {
                return Fields.TryGetValue(key, out string? value) ? value : "";
            }

            return Groups.TryGetValue(group, out Dictionary<string, string>? values) && values.TryGetValue(key, out string? groupValue) ? groupValue : "";
        }

        //An empty value removes the key
        public void set(string key, string value, string? group = null)
        {
            value = (value ?? "").Trim();

            if (group == null)
            {
                if (value == "")
                {
                    Fields.Remove(key);
                }
                else
                {
                    Fields[key] = value;
                }
                return;
            }

            if (value == "")
            {
                if (Groups.TryGetValue(group, out Dictionary<string, string>? values))
                {
                    values.Remove(key);
                    if (values.Count == 0)
                    {
                        Groups.Remove(group);
                    }
                }
                return;
            }

            if (!Groups.ContainsKey(group))
            {
                Groups[group] = new Dictionary<string, string>();
            }
            Groups[group][key] = value;
        }

        public ConfigEntry clone()
        {
            ConfigEntry copy = new ConfigEntry() { Section = Section, Kind = Kind, Platform = Platform };
            copy.Fields = new Dictionary<string, string>(Fields);
            foreach (var group in Groups)
            {
                copy.Groups[group.Key] = new Dictionary<string, string>(group.Value);
            }
            return copy;
        }
    }

    //Editable form of configuration.yaml, written in the layout YamlLoader reads
    public class ConfigDocument
    {
        //Top level options like "debug: true"
        public Dictionary<string, string> Options = new Dictionary<string, string>();
        //Sections without content that work as a switch, like "keys:"
        public List<string> EmptySections = new List<string>();
        public List<ConfigEntry> Entries = new List<ConfigEntry>();

        public static ConfigDocument fromLoader(YamlLoader loader)
        {
            ConfigDocument document = new ConfigDocument();
            var data = loader.getConfigurationData();
            if (data == null)
            {
                throw new Exception("'configuration.yaml' could not be read");
            }

            foreach (var section in data)
            {
                if (section.Value == null)
                {
                    if (section.Key != "")
                    {
                        document.EmptySections.Add(section.Key);
                    }
                    continue;
                }

                foreach (var kind in section.Value)
                {
                    foreach (var platform in kind.Value)
                    {
                        foreach (Dictionary<string, dynamic> definition in platform.Value)
                        {
                            if (section.Key == "")
                            {
                                foreach (var option in definition)
                                {
                                    document.Options[option.Key] = Convert.ToString((object)option.Value) ?? "";
                                }
                                continue;
                            }

                            ConfigEntry entry = new ConfigEntry() { Section = section.Key, Kind = kind.Key, Platform = platform.Key };
                            foreach (var field in definition)
                            {
                                object fieldValue = field.Value;
                                if (fieldValue is Dictionary<string, string> group)
                                {
                                    entry.Groups[field.Key] = new Dictionary<string, string>(group);
                                }
                                else
                                {
                                    entry.Fields[field.Key] = Convert.ToString(fieldValue) ?? "";
                                }
                            }
                            document.Entries.Add(entry);
                        }
                    }
                }
            }

            return document;
        }

        public bool getFlag(string name, bool defaultValue = false)
        {
            if (!Options.TryGetValue(name, out string? value) || value == "")
            {
                return defaultValue;
            }

            return String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        //The loader cuts a line at '#' and drops '"', so those can not be stored
        public static bool isStorable(string value)
        {
            return value.IndexOfAny(new char[] { '#', '"', '\r', '\n' }) < 0;
        }

        private static string format(string value)
        {
            return Regex.IsMatch(value, @"^[A-Za-z0-9_.\\/\-]+$") ? value : "\"" + value + "\"";
        }

        public string toYaml()
        {
            StringBuilder yaml = new StringBuilder();

            //Options have to come first, after a section they would belong to that section
            foreach (var option in Options)
            {
                if (option.Value != "")
                {
                    yaml.AppendLine(option.Key + ": " + format(option.Value));
                }
            }

            //A switch section only counts when another section follows, so these come before the sensors
            foreach (string section in EmptySections.Distinct())
            {
                yaml.AppendLine(section + ":");
            }

            foreach (var section in Entries.GroupBy(entry => entry.Section))
            {
                yaml.AppendLine();
                yaml.AppendLine(section.Key + ":");

                foreach (var platform in section.GroupBy(entry => entry.Kind + "\n" + entry.Platform))
                {
                    foreach (ConfigEntry entry in platform)
                    {
                        yaml.AppendLine("  - " + entry.Kind + ": " + format(entry.Platform));

                        foreach (var field in entry.Fields)
                        {
                            if (field.Value != "")
                            {
                                yaml.AppendLine("    " + field.Key + ": " + format(field.Value));
                            }
                        }

                        foreach (var group in entry.Groups)
                        {
                            if (group.Value.Count == 0)
                            {
                                continue;
                            }

                            yaml.AppendLine("    " + group.Key + ":");
                            foreach (var item in group.Value)
                            {
                                yaml.AppendLine("      - " + item.Key + ": " + format(item.Value));
                            }
                        }
                    }
                }
            }

            return yaml.ToString();
        }

        //Keeps the previous file as "configuration.yaml.bak"
        public void save(string configurationFileFullPath)
        {
            if (File.Exists(configurationFileFullPath))
            {
                File.Copy(configurationFileFullPath, configurationFileFullPath + ".bak", true);
            }

            File.WriteAllText(configurationFileFullPath, toYaml(), new UTF8Encoding(false));
        }
    }
}
