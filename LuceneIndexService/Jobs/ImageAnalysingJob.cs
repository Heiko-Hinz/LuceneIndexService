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
using System.IO.Packaging;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using HeikoHinz.IFilter;

namespace HeikoHinz.LuceneIndexService.Jobs
{
    public class ImageAnalysingJob : BaseAnalysingJob
    {
        private delegate void MethodDelegate();
        private MethodDelegate invoker;

        #region Konstruktor

        public ImageAnalysingJob() : base()
        {
            Init();
        }
        public ImageAnalysingJob(ServiceIndex index, Web web, Settings.FileType fileSettings, string path, Uri url, List<string> folderAuthorizedRoles) : 
            base(index, web, fileSettings, path, url, folderAuthorizedRoles)
        {
            Init();
        }

        #endregion

        #region Init
        private void Init()
        {
            //IsSingleThreaded = true;
            DocumentType = DocumentTypes.Image;
            Description = "Analysiert eine Bilddatei und fügt sie dem Index hinzu.";
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

                BooleanQuery identitiesQuery = new BooleanQuery();
                Document document = new Document();

                CommonAnalyzing(document, identitiesQuery);

                ShellPropertyAnalyzing(document, identitiesQuery);

                IndexSearcher searcher = Index.IndexingService.Searcher;
                TopDocs result = searcher.Search(identitiesQuery, 1);

                IndexWriter writer = Index.IndexingService.Writer;
                if (result.TotalHits > 0)
                    writer.DeleteDocuments(identitiesQuery);
                writer.AddDocument(document);
                    
                writer.Commit();
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
