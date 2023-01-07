using HeikoHinz.JobScheduling;
using HeikoHinz.LuceneIndexService.Settings;
using HtmlAgilityPack;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace HeikoHinz.LuceneIndexService.Jobs
{
    public class CheckPathJob : BaseJob
    {
        private delegate void MethodDelegate();
        private MethodDelegate invoker;

        [XmlIgnore]
        public ServiceIndex Index { get; set; }

        [XmlIgnore]
        public Web Web { get; set; }

        [XmlIgnore]
        public string Path { get; set; }

        [XmlIgnore]
        public Uri Url { get; set; }

        [XmlAttribute]
        public bool ReAnalyze { get; set; }

        [XmlIgnore]
        public List<string> FolderAuthorizedRoles { get; set; } = new List<string>();


        #region Konstruktor

        public CheckPathJob()
        {
            Init();
        }
        public CheckPathJob(ServiceIndex index, Web web, string path, Uri url, List<string> folderAuthorizedRoles, bool reAnalyze)
        {
            this.Index = index;
            this.Web = web;
            this.Path = path;
            this.Url = url;
            this.FolderAuthorizedRoles = folderAuthorizedRoles;
            this.ReAnalyze = reAnalyze;

            this.Frequency = Frequency.Once;
            this.StartDate = DateTime.Now.AddSeconds(10);
            this.EndDate = DateTime.Now.AddSeconds(10);
            Init();
        }
        public CheckPathJob(ServiceIndex index, Web web, string path, Uri url, DateTime startDate, List<string> folderAuthorizedRoles, bool reAnalyze)
        {
            this.Index = index;
            this.Web = web;
            this.Path = path;
            this.Url = url;
            this.StartDate = startDate;
            this.EndDate = startDate;
            this.FolderAuthorizedRoles = folderAuthorizedRoles;
            this.ReAnalyze = reAnalyze;

            this.Frequency = Frequency.Once;
            Init();
        }

        #endregion

        #region Init
        private void Init()
        {
            Properties.AddProperty("Id", Id);
            Properties.AddProperty("Index", Index.Name);
            Properties.AddProperty("Web", Web.Url);
            Properties.AddProperty("Path", Path);
            Properties.AddProperty("Url", Url);
            Description = "Überprüft das Verzeichnis auf neue, geänderte und gelöschte Dateien.";
            invoker = new MethodDelegate(this.Method);
        }
        #endregion

        #region Method
        private void Method()
        {
            try
            {
                if (Index.IndexingService.IsStopping)
                    return;

                DirectoryInfo directory = new DirectoryInfo(Path);


                foreach (Settings.FileType fileSettings in Web.FileSettings)
                {
                    if (ComeToEnd)
                        break;

                    #region Check for deleted files

                    List<BaseProperty> FolderProperties = fileSettings.Properties.Where(p => (p.MemberName == "DirectoryName" || p.MemberName == "FullName") && p.Source == DataSources.FileInfo && p.Index == Lucene.Net.Documents.Field.Index.ANALYZED).ToList();
                    if (FolderProperties.Count >= 2 && Index.IndexingService.Searcher.MaxDoc > 0)
                    {
                        Job jobDeleted = fileSettings.Jobs.SingleOrDefault(j => j.Type == WatcherChangeTypes.Deleted);

                        if (jobDeleted != null)
                        {
                            Type typeDeleted = Type.GetType(jobDeleted.Namespace + "." + jobDeleted.ClassName);

                            BaseProperty directoryProperty = fileSettings.Properties.Single(p => p.MemberName == "DirectoryName" && p.Source == DataSources.FileInfo && p.Index == Lucene.Net.Documents.Field.Index.ANALYZED);
                            BaseProperty fileProperty = fileSettings.Properties.Single(p => p.MemberName == "FullName" && p.Source == DataSources.FileInfo && p.Index == Lucene.Net.Documents.Field.Index.ANALYZED);

                            Query pathQuery = directoryProperty.CreateQuery(Path);
                            IndexSearcher searcher = Index.IndexingService.Searcher;

                            TopDocs result = searcher.Search(pathQuery, searcher.MaxDoc);

                            for (int i = 0; i < result.TotalHits; i++)
                            {
                                if (ComeToEnd)
                                    break;

                                Document document = searcher.Doc(result.ScoreDocs[i].Doc);
                                Lucene.Net.Documents.Field field = document.GetField(fileProperty.Name);

                                FileInfo fi = new FileInfo(field.StringValue);
                                if (!fi.Exists)
                                    ScheduleJob(typeDeleted, fileSettings, fi, null);
                            }
                        }
                    }

                    #endregion

                    #region Check for changed or new files

                    List<BaseProperty> LastChangeProperties = fileSettings.Properties.Where(p => p.MemberName == "LastWriteTime" && p.Source == DataSources.FileInfo && p.Index == Lucene.Net.Documents.Field.Index.ANALYZED).ToList();
                    if (LastChangeProperties.Any())
                    {
                        Job jobChanged = fileSettings.Jobs.SingleOrDefault(j => j.Type == WatcherChangeTypes.Changed);
                        Job jobCreated = fileSettings.Jobs.SingleOrDefault(j => j.Type == WatcherChangeTypes.Created);

                        if (jobChanged != null || jobCreated != null)
                        {
                            Type typeChanged = null;
                            Type typeCreated = null;

                            if (jobChanged != null)
                                typeChanged = Type.GetType(jobChanged.Namespace + "." + jobChanged.ClassName);
                            if (jobCreated != null)
                                typeCreated = Type.GetType(jobCreated.Namespace + "." + jobCreated.ClassName);

                            foreach (string extension in fileSettings.Extensions)
                            {
                                if (ComeToEnd)
                                    break;

                                foreach (FileInfo fi in directory.GetFiles("*." + extension))
                                {
                                    if (ComeToEnd)
                                        break;

                                    BooleanQuery identitiesQuery = new BooleanQuery();

                                    foreach (BaseProperty property in fileSettings.Properties.Where(p => p.Source == DataSources.FileInfo && p.Identity))
                                    {
                                        PropertyInfo member = fi.GetType().GetProperty(property.MemberName);
                                        object value = member.GetValue(fi);

                                        if (value != null)
                                            identitiesQuery.Add(property.CreateQuery(value), Occur.MUST);
                                    }

                                    IndexSearcher searcher = Index.IndexingService.Searcher;
                                    TopDocs result = searcher.Search(identitiesQuery, 1);

                                    if (result.TotalHits > 0)
                                    {
                                        if (typeChanged == null)
                                            continue;

                                        Document document = searcher.Doc(result.ScoreDocs.First().Doc);
                                        Lucene.Net.Documents.Field field = document.GetField(LastChangeProperties.First().Name);

                                        if (ReAnalyze || field.StringValue != fi.LastWriteTime.Ticks.ToString())
                                            ScheduleJob(typeChanged, fileSettings, fi, FolderAuthorizedRoles);
                                    }
                                    else
                                    {
                                        if (typeCreated == null)
                                            continue;

                                        ScheduleJob(typeCreated, fileSettings, fi, FolderAuthorizedRoles);
                                    }
                                }
                            }

                        }
                    }

                    #endregion
                }
            }
            catch (Exception exc)
            {
                HasError = true;
                Properties.AddProperty("Source", GetType().Namespace);
                Service.LogError(DateTime.Now, Properties, exc);
            }
        }

        private void ScheduleJob(Type jobType, Settings.FileType fileSettings, FileInfo fileInfo, List<string> folderAuthorizedRoles)
        {
            if (SchedulingServiceInstance.Instance.QueuedJobs.SingleOrDefault(j => j.GetType() == jobType && jobType.GetProperty("Web").GetValue(j) == Web && jobType.GetProperty("Path").GetValue(j).ToString() == fileInfo.FullName) == null)
            {
                BaseJob bJob = (BaseJob)Activator.CreateInstance(jobType, new object[] { Index, Web, fileSettings, fileInfo.FullName, new Uri(Url, fileInfo.Name), folderAuthorizedRoles });
                SchedulingServiceInstance.Instance.EnqeueJob(bJob);
            }
        }


        #endregion

        #region Execute

        public override void Execute()
        {
            invoker.BeginInvoke(this.CallBack, null);
        }

        #endregion

        #region CallBack

        private void CallBack(IAsyncResult ar)
        {
            invoker.EndInvoke(ar);
            TerminationTime = DateTime.Now;

            this.Done = true;
        }

        #endregion
    }
}
