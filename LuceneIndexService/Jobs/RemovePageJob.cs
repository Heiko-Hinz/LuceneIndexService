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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace HeikoHinz.LuceneIndexService.Jobs
{
    public class RemovePageJob : BaseJob
    {
        private delegate void MethodDelegate();
        private MethodDelegate invoker;

        [XmlIgnore]
        public ServiceIndex Index { get; set; }

        [XmlIgnore]
        public Web Web { get; set; }

        [XmlIgnore]
        public Settings.FileType FileSettings { get; set; }

        [XmlIgnore]
        public string Path { get; set; }

        [XmlIgnore]
        public Uri Url { get; set; }

        #region Konstruktor

        public RemovePageJob()
        {
            Init();
        }
        public RemovePageJob(ServiceIndex index, Web web, Settings.FileType fileSettings, string path, Uri url)
        {
            this.Index = index;
            this.Web = web;
            this.FileSettings = fileSettings;
            this.Path = path;
            this.Url = url;

            this.Frequency = Frequency.Once;
            this.StartDate = DateTime.Now;
            this.EndDate = DateTime.Now;
            Init();
        }

        #endregion

        #region Init
        private void Init()
        {
            Properties.AddProperty("Id", Id);
            Properties.AddProperty("Index", Index.Name);
            Properties.AddProperty("Web", Web.Url);
            Properties.AddProperty("FileSettings", (object)FileSettings.Name);
            Properties.AddProperty("Path", Path);
            Properties.AddProperty("Url", Url);
            Description = "Löscht die Seite aus dem Index.";
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

                BooleanQuery multiQuery = new BooleanQuery();
                FileInfo fi = new FileInfo(Path);

                foreach (SimpleProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.FileInfo && p.Identity))
                {
                    PropertyInfo member = fi.GetType().GetProperty(property.MemberName);
                    object value = member.GetValue(fi);

                    if (value != null)
                        multiQuery.Add(property.CreateQuery(value), Occur.MUST);
                }

                TopDocs result = Index.IndexingService.Searcher.Search(multiQuery, 1);

                if (result.TotalHits > 0)
                {
                    IndexWriter writer = Index.IndexingService.Writer;
                    writer.DeleteDocuments(multiQuery);
                    writer.Commit();
                }
            }
            catch (Exception exc)
            {
                HasError = true;
                Properties.AddProperty("Source", GetType().Namespace);
                Service.LogError(DateTime.Now, Properties, exc);
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
