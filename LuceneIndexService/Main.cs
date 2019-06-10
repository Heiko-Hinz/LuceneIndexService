using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using HeikoHinz.LuceneIndexService.Settings;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using Lucene.Net.Store;

namespace HeikoHinz.LuceneIndexService
{
    //C:\Users\apps-trurnit-admin\AppData\Local\Apps\2.0\ZDX6Y6NX.HYL\2MCVZKLB.MGJ\luce..tion_d941f24f8e144190_0001.0000_84c18aaddbfcf4db
    //C:\Windows\Microsoft.NET\Framework\v4.0.30319\installutil LuceneIndexService.exe
    public partial class Main : ServiceBase
    {
        public static List<Watcher> Watchers { get; set; } = new List<Watcher>();
        public static EventLog EventLogger { get; set; }
        Configuration Settings { get; set; }
        public static SchedulingServiceInstance SchedulerService { get; set; }

        internal void TestStartupAndStop(string[] args)
        {
            this.OnStart(args);
            Console.ReadLine();
            this.OnStop();
        }
        public Main(string[] args)
        {
            InitializeComponent();

            //Setup Service
            this.ServiceName = "LuceneIndexService";
            this.CanStop = true;
            this.CanPauseAndContinue = false;

            //Setup logging
            this.AutoLog = false;

            //OnStart(new List<string>().ToArray());
            //return;


            ((ISupportInitialize)this.EventLog).BeginInit();
            if (!EventLog.SourceExists(this.ServiceName))
            {
                EventLog.CreateEventSource(this.ServiceName, "Application");
            }
            ((ISupportInitialize)this.EventLog).EndInit();

            this.EventLog.Source = this.ServiceName;
            this.EventLog.Log = "Application";
            EventLogger = EventLog;
        }

        protected override void OnStart(string[] args)
        {
            this.EventLog.WriteEntry("Service started");

            try
            {
                SchedulingServiceInstance scheduler = new SchedulingServiceInstance();
                SchedulingServiceInstance.Instance = scheduler;
                SchedulerService = scheduler;

                string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.xml");

                if (!System.IO.File.Exists(configFile))
                {
                    this.EventLog.WriteEntry("Es wurde keine Konfigurationsdatei gefunden." + configFile, EventLogEntryType.Warning);
                    OnStop();
                    return;
                }
                XDocument doc = XDocument.Load(configFile);
                if(doc == null)
                {
                    this.EventLog.WriteEntry(String.Format("Beim Laden Konfigurationsdatei '{0}' ist ein Fehler aufgetreten.", configFile), EventLogEntryType.Warning);
                    OnStop();
                    return;
                }
                Settings = new Configuration(this.ServiceName, doc.Root);

                SchedulingServiceInstance.Start();

                foreach (ServiceIndex index in Settings.Indexes)
                {
                    foreach (Web web in index.Webs)
                    {
                        Settings.Directory directory = web.Directories.Single(d => d.Include);
                        StartWatcher(
                            index,
                            web,
                            web.Url,
                            new DirectoryInfo(directory.Path),
                            web.Filter.Split(";".ToCharArray()).ToList(),
                            directory.IndexSubfolders,
                            web.Directories.Where(d => !d.Include).Select(d => d.Path).ToList(),
                            index.ConfigurationChanged
                        );
                    }
                }
                var changedIndexes = Settings.Indexes.Where(i => i.ConfigurationChanged);
                if (changedIndexes.Any())
                {
                    doc.Save(configFile);
                    foreach (ServiceIndex index in changedIndexes)
                        index.ConfigurationChanged = false;
                    this.EventLog.WriteEntry("Dateien werden nach Änderung der Konfiguration neu indiziert.");
                }
                this.EventLog.WriteEntry("Monitoring started");
                /*
                BooleanQuery booleanClauses = new BooleanQuery();
                ServiceIndex _index = Settings.Indexes.First();
                Lucene.Net.Analysis.Analyzer standardAnalyzer = _index.IndexingService.Writer.Analyzer;

                booleanClauses.Add(_index.Webs.First().FileSettings.First().Properties.Single(p => p.Name == "active").CreateQuery(0), Occur.MUST);

                //TermQuery _query = new TermQuery(new Term("active", "1"));
                //booleanClauses.Add(_query, Occur.MUST);

                TermQuery _query = new TermQuery(new Term("Path", @"C:\Webs\Intranet\Default.aspx"));
                //booleanClauses.Add(_query, Occur.MUST);

                QueryParser parser = new QueryParser(ServiceIndex.Version, "authorized-groups", standardAnalyzer);
                Query query = parser.Parse("authorized-groups:TEAGVertrieb");
                booleanClauses.Add(query, Occur.MUST);

                BooleanQuery subBooleanClauses = new BooleanQuery();
                List<string> fields = new List<string>() { "content", "title", "keywords" };
                foreach (string field in fields)
                {
                    parser = new QueryParser(ServiceIndex.Version, field, standardAnalyzer);
                    query = parser.Parse("TEAG");
                    subBooleanClauses.Add(query, Occur.SHOULD);
                }

                booleanClauses.Add(subBooleanClauses, Occur.MUST);

                IndexReader reader = DirectoryReader.Open(Settings.Indexes.First().Directory, true);


                Searcher searcher = new IndexSearcher(reader);
                if (searcher.MaxDoc > 0)
                {
                    TopDocs result = searcher.Search(booleanClauses, searcher.MaxDoc);
                    int erg = result.TotalHits;
                }
                */
            }
            catch (Exception exc)
            {
                List<string> stack = new List<string>();
                Exception _exc = exc;
                while(_exc != null)
                {
                    stack.Add(_exc.Message);
                    stack.Add(_exc.StackTrace);
                    _exc = _exc.InnerException;
                }
                this.EventLog.WriteEntry(String.Join("\n", stack), EventLogEntryType.Warning);
            }
            this.EventLog.WriteEntry("Scheduling service started");
        }

        public static void StartWatcher(ServiceIndex index, Web web, Uri url, DirectoryInfo directory, List<string> extensions, bool indexSubFolder, List<string> excludedPaths, bool reIndex)
        {
            if (directory.Exists && !excludedPaths.Contains(directory.FullName))
            {
                SchedulingServiceInstance.Instance.EnqeueJob(new Jobs.CheckPathJob(index, web, directory.FullName, url, reIndex));

                Watcher watcher = new Watcher()
                {
                    Index = index,
                    Web = web,
                    Path = directory.FullName,
                    Extensions = extensions,
                    Url = url
                };
                watcher.Start();
                Watchers.Add(watcher);

                foreach(DirectoryInfo subfolder in directory.GetDirectories())
                {
                    StartWatcher(index, web, new Uri(url, subfolder.Name + "/"), subfolder, extensions, indexSubFolder, excludedPaths, reIndex);
                }
            }
        }
        public static void StopWatcher(string path)
        {
            Watcher watcher = Watchers.SingleOrDefault(w => w.Path == path);
            if(watcher != null)
            {
                Watchers.Remove(watcher);
                watcher.Stop();
            }
        }

        protected override void OnStop()
        {
            try
            {
                foreach (Watcher watcher in Watchers)
                {
                    watcher.Stop();
                }
                this.EventLog.WriteEntry("Monitoring stopped");

                foreach (ServiceIndex index in Settings.Indexes)
                {
                    index.IndexingService.Stop();
                    this.EventLog.WriteEntry(String.Format("Indexing für '{0}' wurde beendet.", index.Path));

                }

                SchedulingServiceInstance.Stop();
                this.EventLog.WriteEntry("Scheduling service stopped");
            }
            catch(Exception exc)
            {
                this.EventLog.WriteEntry(exc.StackTrace, EventLogEntryType.Warning);
            }
            this.EventLog.WriteEntry("Service stopped");
        }
    }
}
