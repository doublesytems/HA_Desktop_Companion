using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace HADC_REBORN.Class.Helpers
{
    public class YamlLoader
    {
        private string configurationFilePath;
        private Dictionary<string, Dictionary<string, Dictionary<string, List<Dictionary<string, dynamic>>>>> configurationData;

        public YamlLoader(string configurationFileFullPath)
        {
            if (!File.Exists(configurationFileFullPath))
            {
                throw new Exception("'configuration.yaml' not found");
            }

            configurationFilePath = configurationFileFullPath;
            load();
        }

        private bool load()
        {
            try
            {
                string[] text = File.ReadAllLines(configurationFilePath, System.Text.Encoding.UTF8);

                string section = ""; //ROOT
                int num = 0;
                string subSection = "";
                string category = "";
                string subArray = "";
                Dictionary<string, Dictionary<string, Dictionary<string, List<Dictionary<string, dynamic>>>>> configurationDataTemp = new Dictionary<string, Dictionary<string, Dictionary<string, List<Dictionary<string, dynamic>>>>>();

                foreach (string line in text)
                {
                    string localLine = line;

                    if (line.Contains('#') == true)
                    {
                        localLine = line.Replace('#' + line.Split('#')[1], "");
                    }

                    if (localLine.Contains(':') == true)
                    {
                        string[] splitedString = localLine.Split(':');
                        string parameter = splitedString[0].Trim();
                        //Everything after the first colon, so a value like "Disk C: free" keeps its spaces
                        string value = localLine.Substring(localLine.IndexOf(':') + 1).Trim();

                        if (parameter == "")
                        {
                            continue;
                        }

                        if (subArray != "" && !splitedString[0].Contains("     "))
                        {
                            subArray = "";
                        }

                        if (value == "")
                        {
                            if (splitedString[0].Contains("    "))
                            {
                                subArray = parameter;
                                continue;
                            }
                            else
                            {
                                subArray = "";
                            }
                            if (section != "")
                            {
                                if (!configurationDataTemp.ContainsKey(section))
                                {
                                    configurationDataTemp[section] = null;
                                }
                            }
                            section = parameter;
                            num = 0;
                            category = "";
                            subSection = "";
                            continue;
                        }

                        if (parameter.Contains("- ") == true)
                        {
                            if (subArray == "")
                            {
                                subSection = parameter.Replace("- ", "");

                                if (category == value)
                                {
                                    num++;
                                }
                                else
                                {
                                    if (!configurationDataTemp.ContainsKey(section) || !configurationDataTemp[section].ContainsKey(subSection) || !configurationDataTemp[section][subSection].ContainsKey(value) || configurationDataTemp[section][subSection][value].Count <= 0)
                                    {
                                        num = 0;
                                    }
                                    else
                                    {
                                        num = configurationDataTemp[section][subSection][value].Count;
                                    }
                                }

                                category = value;
                                continue;
                            }
                            else
                            {
                                parameter = parameter.Replace("- ", "");
                            }
                        }

                        if (!configurationDataTemp.ContainsKey(section))
                        {
                            configurationDataTemp[section] = new Dictionary<string, Dictionary<string, List<Dictionary<string, dynamic>>>>();
                        }

                        if (!configurationDataTemp[section].ContainsKey(subSection))
                        {
                            configurationDataTemp[section][subSection] = new Dictionary<string, List<Dictionary<string, dynamic>>>();
                        }

                        if (!configurationDataTemp[section][subSection].ContainsKey(category))
                        {
                            configurationDataTemp[section][subSection][category] = new List<Dictionary<string, dynamic>>(100);
                        }

                        if (configurationDataTemp[section][subSection][category].Count <= num)
                        {
                            configurationDataTemp[section][subSection][category].Add(new Dictionary<string, dynamic>());
                        }

                        if (subArray == "")
                        {
                            configurationDataTemp[section][subSection][category][num][parameter] = value.Replace("\"", "");
                        }
                        else
                        {
                            if (!configurationDataTemp[section][subSection][category][num].ContainsKey(subArray))
                            {
                                configurationDataTemp[section][subSection][category][num][subArray] = new Dictionary<string, string>();
                            }
                            configurationDataTemp[section][subSection][category][num][subArray][parameter] = value.Replace("\"", "");
                        }
                    }
                }

                configurationData = configurationDataTemp;
                return true;
            }
            catch
            {
                return false;
            }
        }

        //Reads the file again, the data stays as it was when the file can not be read
        public bool reload()
        {
            return load();
        }

        //Reads a top level option like "debug: true"
        public bool getFlag(string name, bool defaultValue = false)
        {
            string? value = getOption(name);
            if (String.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            return String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        //Reads a top level option as text, null when it is not in the file
        public string? getOption(string name)
        {
            try
            {
                foreach (Dictionary<string, dynamic> rootOptions in configurationData[""][""][""])
                {
                    if (rootOptions.ContainsKey(name))
                    {
                        return (string)rootOptions[name];
                    }
                }
            }
            catch (Exception)
            {
                //No top level options in the file
            }

            return null;
        }

        public Dictionary<string, Dictionary<string, Dictionary<string, List<Dictionary<string, dynamic>>>>>  getConfigurationData()
        {
            return configurationData;
        }
    }
}
