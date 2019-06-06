using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace HeikoHinz.LuceneIndexService.Settings
{
    public class ServiceIndex : Index
    {
        [XmlIgnore]
        public IndexingServiceInstance IndexingService { get; set; }


        [XmlAttribute]
        public bool ConfigurationChanged { get; set; }

        [XmlAttribute]
        public DateTime ConfigurationLastChange { get; set; }


        public ServiceIndex(XElement index) : base(index)
        {
            FileInfo fi = new FileInfo(this.ConfigurationPath);
            HeikoHinz.Helper.SetMembersValue(index, "configurationLastChange", this, fi.LastWriteTime);
                        
            ConfigurationChanged = index.Attribute("configurationLastChange") == null || index.Attribute("configurationLastChange").Value != fi.LastWriteTime.ToString();
            if (ConfigurationChanged)
            {
                ConfigurationLastChange = fi.LastWriteTime;
                if (index.Attribute("configurationLastChange") == null)
                    index.Add(new XAttribute("configurationLastChange", ConfigurationLastChange.ToString()));
                else
                    index.Attribute("configurationLastChange").Value = ConfigurationLastChange.ToString();
            }

            IndexingService = new IndexingServiceInstance(this);
            Main.EventLogger.WriteEntry(String.Format("Indexing für '{0}' wurde initialisiert.", this.Path));
        }
    }
}
