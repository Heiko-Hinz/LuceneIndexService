using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace HeikoHinz.LuceneIndexService
{
    [RunInstaller(true)]
    public partial class Installer : System.Configuration.Install.Installer
    {
        private ServiceInstaller LuceneIndexServiceInstaller;

        private ServiceProcessInstaller ProcessInstaller;

        public Installer()
        {
            InitializeComponent();

            // Instantiate installers for process and services.
            ProcessInstaller = new ServiceProcessInstaller();
            LuceneIndexServiceInstaller = new ServiceInstaller();

            // The services run under the system account.
            ProcessInstaller.Account = ServiceAccount.LocalSystem;

            // The services are started manually.
            LuceneIndexServiceInstaller.StartType = ServiceStartMode.Manual;

            // ServiceName must equal those on ServiceBase derived classes.            
            LuceneIndexServiceInstaller.ServiceName = "LuceneIndexService";
            LuceneIndexServiceInstaller.DisplayName = "Lucene Index Service";
            LuceneIndexServiceInstaller.Description = "Dienst zum Verwalten von Webseiten, die seitens Lucene.NET indiziert werden";

            // Add installers to collection. Order is not important.
            Installers.Add(ProcessInstaller);
            Installers.Add(LuceneIndexServiceInstaller);
        }

    }
}
