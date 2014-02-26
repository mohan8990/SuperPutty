﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using System.Windows.Forms;
using System.IO;
using System.Xml;
using log4net;
using System.Collections;

namespace SuperPutty.Utils
{
    /// <summary>
    /// PortableSettingsProvider
    /// 
    /// Based on 
    /// http://www.codeproject.com/Articles/20917/Creating-a-Custom-Settings-Provider
    /// </summary>
    public class PortableSettingsProvider : SettingsProvider
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PortableSettingsProvider));

        private static bool ForceRoamingSettings = Convert.ToBoolean(ConfigurationManager.AppSettings["SuperPuTTY.ForceRoamingSettings"] ?? "True");

        public const string SettingsRoot = "Settings";

        private XmlDocument settingsXML;

        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {
            base.Initialize(this.ApplicationName, config);
        }

        public override string ApplicationName
        {
            get
            {
                if (Application.ProductName.Trim().Length > 0)
                {
                    return Application.ProductName;
                }
                else
                {
                    FileInfo fi = new FileInfo(Application.ExecutablePath);
                    return fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length);
                }
            }
            set { }
        }


        /// <summary>
        /// Return a list of possible locations for the settings file.  If non are found, create the 
        /// default in the first location
        /// </summary>
        /// <returns></returns>
        public virtual string[] GetAppSettingsPaths()
        {
            string[] paths = new string[2];
            paths[0] = Environment.GetEnvironmentVariable("USERPROFILE");
            paths[1] = Path.GetDirectoryName(Application.ExecutablePath);
            return paths;
        }

        public virtual string GetAppSettingsFileName()
        {
            return ApplicationName + ".settings";
        }

        /// <summary>
        /// Return first existing file path or the first if none found
        /// </summary>
        /// <returns></returns>
        string GetAppSettingsFilePath()
        {
            string[] paths = GetAppSettingsPaths();
            string fileName = GetAppSettingsFileName();

            string path = Path.Combine(paths[0], fileName);
            foreach (string dir in paths)
            {
                string filePath = Path.Combine(dir, fileName);
                if (File.Exists(filePath))
                {
                    path = filePath;
                    break;
                }
            }
            return path;
        }

        public string SettingsFilePath { get; private set; }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            foreach (SettingsPropertyValue propVal in collection)
            {
                SetValue(propVal);
            }

            try
            {
                //SettingsXML.Save(Path.Combine(GetAppSettingsPath(), GetAppSettingsFileName()));
                SettingsXML.Save(GetAppSettingsFilePath());
            }
            catch(Exception ex){
                Log.Error("Error saving settings", ex);
            }
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            // Create new collection of values
            SettingsPropertyValueCollection values = new SettingsPropertyValueCollection();

            // Iterate through the settings to be retrieved
            foreach (SettingsProperty setting in collection)
            {
                SettingsPropertyValue value = new SettingsPropertyValue(setting);
                value.IsDirty = true;
                value.SerializedValue = GetValue(setting);
                values.Add(value);
            }

            return values;
        }

        public XmlDocument SettingsXML
        {
            get
            {
                // If we dont hold an xml document, try opening one.
                // If it doesnt exist then create a new one ready.
                if (this.settingsXML == null)
                {
                    this.settingsXML = new XmlDocument();
                    //string settingsFile = Path.Combine(GetAppSettingsPath(), GetAppSettingsFileName());
                    string settingsFile = GetAppSettingsFilePath();
                    this.SettingsFilePath = settingsFile;
                    try
                    {
                        this.settingsXML.Load( settingsFile );
                        Log.InfoFormat("Loaded settings from {0}", settingsFile);
                    }
                    catch (Exception)
                    {
                        Log.InfoFormat("Could not load file ({0}), creating settings file", settingsFile);
                        // Create new document
                        XmlDeclaration declaration = this.settingsXML.CreateXmlDeclaration("1.0", "utf-8", String.Empty);
                        this.settingsXML.AppendChild(declaration);

                        XmlNode nodeRoot = this.settingsXML.CreateNode(XmlNodeType.Element, SettingsRoot, String.Empty);
                        this.settingsXML.AppendChild(nodeRoot);
                    }
                }

                return this.settingsXML;
            }
        }

        private string GetValue(SettingsProperty setting)
        {
            string value = String.Empty;

            try
            {
                if (UseRoamingSettings(setting))
                {
                    XmlNode node = SettingsXML.SelectSingleNode(SettingsRoot + "/" + setting.Name);
                    if (node == null)
                    {
                        // try go by host...backwards compatibility
                        node = SettingsXML.SelectSingleNode(SettingsRoot + "/" + GetHostName() + "/" + setting.Name);
                    }
                    value = node.InnerText;
                }
                else
                {
                    value = SettingsXML.SelectSingleNode(SettingsRoot + "/" + GetHostName() + "/" + setting.Name).InnerText;
                }
            }
            catch (Exception)
            {
                if (setting.DefaultValue != null)
                {
                    value = setting.DefaultValue.ToString();
                }
                else
                {
                    value = String.Empty;
                }
            }

            return value;
        }

        private void SetValue(SettingsPropertyValue propVal)
        {
            XmlNode machineNode;
            XmlNode settingNode;

            // Determine if the setting is roaming.
            // If roaming then the value is stored as an element under the root
            // Otherwise it is stored under a machine name node 
            try
            {
                if (UseRoamingSettings(propVal.Property))
                    settingNode = (XmlElement)SettingsXML.SelectSingleNode(SettingsRoot + "/" + propVal.Name);
                else
                    settingNode = (XmlElement)SettingsXML.SelectSingleNode(SettingsRoot + "/" + GetHostName() + "/" + propVal.Name);
            }
            catch (Exception)
            {
                settingNode = null;
            }


            // Check to see if the node exists, if so then set its new value
            if (settingNode != null)
            {
                settingNode.InnerText = propVal.SerializedValue.ToString();
            }
            else
            {
                if (UseRoamingSettings(propVal.Property))
                {
                    // Store the value as an element of the Settings Root Node
                    settingNode = SettingsXML.CreateElement(propVal.Name);
                    settingNode.InnerText = propVal.SerializedValue.ToString();
                    SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(settingNode);
                }
                else
                {
                    // Its machine specific, store as an element of the machine name node,
                    // creating a new machine name node if one doesnt exist.
                    string nodePath = SettingsRoot + "/" + GetHostName();
                    try
                    {
                        machineNode = (XmlElement)SettingsXML.SelectSingleNode(nodePath);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Error selecting node, " + nodePath, ex);
                        machineNode = SettingsXML.CreateElement(GetHostName());
                        SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(machineNode);
                    }

                    if (machineNode == null)
                    {
                        machineNode = SettingsXML.CreateElement(GetHostName());
                        SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(machineNode);
                    }

                    settingNode = SettingsXML.CreateElement(propVal.Name);
                    settingNode.InnerText = propVal.SerializedValue.ToString();
                    machineNode.AppendChild(settingNode);
                }
            }
        }

        private static string GetHostName()
        {
            return Environment.MachineName;
        }

        private bool UseRoamingSettings(SettingsProperty prop)
        {
            if (ForceRoamingSettings || string.IsNullOrEmpty(Environment.MachineName) || Char.IsDigit(Environment.MachineName[0]))
            {
                return true;
            }

            // Determine if the setting is marked as Roaming
            foreach (DictionaryEntry de in prop.Attributes)
            {
                Attribute attr = (Attribute) de.Value;
                if (attr is SettingsManageabilityAttribute)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
