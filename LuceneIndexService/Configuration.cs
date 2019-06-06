using HeikoHinz.LuceneIndexService.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace HeikoHinz.LuceneIndexService
{
    public class Configuration
    {
        [XmlElement("Indexes")]
        public List<ServiceIndex> Indexes { get; set; } = new List<ServiceIndex>();

        [XmlAttribute]
        public string ServiceName { get; set; }

        public Configuration() { }
        public Configuration(string serviceName, XElement settings)
        {
            ServiceName = serviceName;

            if (settings.Element("scheduler") != null)
            {
                XElement schedulerConfig = settings.Element("scheduler");

                SchedulingServiceInstance scheduler = Main.SchedulerService;

                HeikoHinz.Helper.SetMembersValue(schedulerConfig, "timeout", scheduler, 30);
                HeikoHinz.Helper.SetMembersValue(schedulerConfig, "maxJobs", scheduler, 1);
                HeikoHinz.Helper.SetMembersValue(schedulerConfig, "logDirectoryPath", scheduler, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", ServiceName));
                HeikoHinz.Helper.SetMembersValue(schedulerConfig, "jobLogEntriesSlidingExpiration", scheduler, 30);
                HeikoHinz.Helper.SetMembersValue(schedulerConfig, "exceptionLogEntriesSlidingExpiration", scheduler, 15);
                HeikoHinz.Helper.SetMembersValue(schedulerConfig, "enableLogging", scheduler, true);
                HeikoHinz.Helper.SetMembersValue(schedulerConfig, "logConvertLogFileToXmlJob", scheduler, false);
                HeikoHinz.Helper.SetMembersValue(schedulerConfig, "logSaveJobLogEntriesJob", scheduler, false);
                HeikoHinz.Helper.SetMembersValue(schedulerConfig, "logCheckLogFilesJob", scheduler, false);
                HeikoHinz.Helper.SetMembersValue(schedulerConfig, "logSaveExceptionLogEntriesJob", scheduler, false);

                if (!System.IO.Directory.Exists(scheduler.LogDirectoryPath))
                    System.IO.Directory.CreateDirectory(scheduler.LogDirectoryPath);


                if (settings.Element("indexes") != null)
                {
                    foreach (XElement index in settings.Element("indexes").Elements("index"))
                        Indexes.Add(new ServiceIndex(index)); // index));
                }
            }
        }
    }
}
