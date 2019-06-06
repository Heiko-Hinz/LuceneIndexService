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
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace HeikoHinz.LuceneIndexService.Jobs
{
    public class PageAnalysingJob : BaseJob
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

        public PageAnalysingJob()
        {
            Init();
        }
        public PageAnalysingJob(ServiceIndex index, Web web, Settings.FileType fileSettings, string path, Uri url)
        {
            this.Index = index;
            this.Web = web;
            this.FileSettings = fileSettings;
            this.Path = path;
            this.Url = url;

            this.Frequency = Frequency.Once;
            this.StartDate = DateTime.Now.AddSeconds(10);
            this.EndDate = DateTime.Now.AddSeconds(10);
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
            Description = "Analysiert die Webseite und fügt sie dem Index hinzu.";
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

                FileInfo fi = new FileInfo(Path);
                
                HtmlDocument fDoc = new HtmlDocument();
                fDoc.Load(Path);
                
                HtmlDocument pDoc = new HtmlDocument();
                WebClient wc = new WebClient();
                if ((Web.UseRoleBasedCredentials || Web.UseCredentials) && Web.RequestCredentials.Any())
                {
                    Credential credential = Web.RequestCredentials.First();
                    wc.Credentials = new NetworkCredential(credential.UserName, credential.Password, credential.Domain);
                }
                Uri url = new Uri(Url.AbsoluteUri);
                if(Web.ExtendRequest)
                {
                    foreach (KeyValuePair<string, string> parameter in Web.RequestParameters)
                        url = url.AddParameter(parameter.Key, parameter.Value);

                    if(Web.EncryptQuery && Web.EncryptionAlgorithms.ContainsKey("RijndaelManaged"))
                    {
                        string[] p = url.AbsoluteUri.Split("?".ToCharArray());
                        url = new Uri(p[0] + "?" + Encryption.UrlEncrypt(p[1], Web.EncryptionAlgorithms["RijndaelManaged"]));
                    }
                }
                wc.Encoding = Encoding.UTF8;
                pDoc.LoadHtml(wc.DownloadString(url));

                foreach (SimpleProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.FileInfo))
                {
                    PropertyInfo member = fi.GetType().GetProperty(property.MemberName);
                    object value = member.GetValue(fi);

                    if (value != null)
                    {
                        AbstractField field = property.GetDocumentField(value);
                        document.Add(field);
                        
                        if (property.Identity)
                            identitiesQuery.Add(property.CreateQuery(value), Occur.MUST);
                    }
                }
                foreach (SimpleProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.Uri))
                {
                    PropertyInfo member = Url.GetType().GetProperty(property.MemberName);
                    object value = member.GetValue(Url);

                    if (value != null)
                    {
                        AbstractField field = property.GetDocumentField(value);
                        document.Add(field);

                        if (property.Identity)
                            identitiesQuery.Add(property.CreateQuery(value), Occur.MUST);
                    }
                }
                foreach (ListProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.List))
                {
                    List<string> value = new List<string>();

                    if (property.Name == "AuthorizedGroups")
                    {
                        List<Tuple<string, string, bool>> groupRights = Helper.FileSystem.GetReadingRights(fi);
                        List<string> groups = property.Filter.Split(";,".ToCharArray()).ToList();
                        foreach(string group in groupRights.Where(gr => gr.Item3 && groups.Contains(gr.Item2)).Select(gr => gr.Item2))
                        {
                            Lucene.Net.Documents.Field field = new Lucene.Net.Documents.Field(property.Name, group, Lucene.Net.Documents.Field.Store.YES, property.Index, property.TermVector);
                            field.Boost = property.Boost;
                            document.Add(field);
                        }
                    }
                }
                
                if (fDoc.DocumentNode != null)
                {
                    HtmlNode root = fDoc.DocumentNode;

                    foreach (DataProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.FileContent))
                    {
                        HtmlNode erg = root.SelectSingleNode(property.Xpath);
                        if(erg != null && erg.Attributes[property.MemberName] != null)
                        {
                            string value = erg.Attributes[property.MemberName].Value;
                            AbstractField field = property.GetDocumentField(value);
                            document.Add(field);

                            if (property.Identity)
                                identitiesQuery.Add(property.CreateQuery(value), Occur.MUST);
                        }
                    }
                }
                if (pDoc.DocumentNode != null)
                {
                    HtmlNode root = pDoc.DocumentNode;

                    foreach (DataProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.PageContent))
                    {
                        if (property.SingleContent)
                        {
                            HtmlNode node = root.SelectSingleNode(property.Xpath);
                            if (node != null)
                            {
                                if (property.RemoveContent)
                                    node.Remove();
                                else
                                    AddPageContentField(node, property, document);
                            }
                        }
                        else
                        {
                            HtmlNodeCollection list = root.SelectNodes(property.Xpath);
                            if (list != null)
                            {
                                foreach(HtmlNode node in list)
                                {
                                    if (property.RemoveContent)
                                        node.Remove();
                                    else
                                        AddPageContentField(node, property, document);
                                }
                            }
                        }
                    }
                }

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

        private void AddPageContentField(HtmlNode node, DataProperty property, Document document)
        {
            string value = node.InnerText.Trim();
            Lucene.Net.Documents.Field field = new Lucene.Net.Documents.Field(property.Name, value, Lucene.Net.Documents.Field.Store.YES, property.Index, property.TermVector);
            field.Boost = property.Boost;
            document.Add(field);
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
