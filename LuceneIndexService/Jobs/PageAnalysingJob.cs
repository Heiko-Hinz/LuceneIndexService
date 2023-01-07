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
    public class PageAnalysingJob : BaseAnalysingJob
    {
        private delegate void MethodDelegate();
        private MethodDelegate invoker;
        #region Konstruktor

        public PageAnalysingJob() : base()
        {
            Init();
        }
        public PageAnalysingJob(ServiceIndex index, Web web, Settings.FileType fileSettings, string path, Uri url, List<string> folderAuthorizedRoles) :
            base(index, web, fileSettings, path, url, folderAuthorizedRoles)
        {
            Init();
        }

        #endregion

        #region Init
        private void Init()
        {
            DocumentType = DocumentTypes.Page;
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

                CommonAnalyzing(document, identitiesQuery);

                HtmlDocument fDoc = new HtmlDocument();
                fDoc.Load(Path);
                
                HtmlDocument pDoc = new HtmlDocument();
                Helper.AnalyzerWebClient wc = new Helper.AnalyzerWebClient();
                wc.Encoding = Encoding.UTF8;
                //wc.UseCookieContainer = true;
                if (Web.UseCredentials && Web.RequestCredentials.Any())
                {
                    Credential credential = Web.RequestCredentials.FirstOrDefault(c => c.AuthenticationMode == Web.AuthenticationMode);
                    if (credential != null)
                    {
                        if (Web.AuthenticationMode == AuthenticationModes.Basic)
                        {
                            string encoded = Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(credential.UserName + ":" + credential.Password));
                            wc.Headers.Add("Authorization", "Basic " + encoded);
                        }
                        else if (Web.AuthenticationMode == AuthenticationModes.Windows)
                            wc.Credentials = new NetworkCredential(credential.UserName, credential.Password, credential.Domain);

                        if (Web.FormsAuthentication != null)
                        {
                            wc.UseCookieContainer = Web.FormsAuthentication.UseCookieContainer;
                            if(Web.FormsAuthentication.Cookie != null)
                                wc.Headers.Add(HttpRequestHeader.Cookie, Web.FormsAuthentication.Cookie);
                        }
                    }
                }
                Uri url = new Uri(Url.AbsoluteUri);
                if(Web.ExtendRequest)
                {
                    foreach (KeyValuePair<string, string> parameter in Web.RequestParameters)
                        url = url.AddParameter(parameter.Key, parameter.Value);

                    if(Web.EncryptQuery)
                    {
                        string[] p = url.AbsoluteUri.Split("?".ToCharArray());
                        string query = "";

                        if (Web.EncryptionAlgorithm == "RijndaelManaged" && Web.EncryptionAlgorithms.ContainsKey(Web.EncryptionAlgorithm))
                            query = Encryption.UrlEncrypt_RijndaelManaged(p[1], Web.EncryptionAlgorithms[Web.EncryptionAlgorithm]);
                        else if (Web.EncryptionAlgorithm == "DES" && Web.EncryptionAlgorithms.ContainsKey(Web.EncryptionAlgorithm))
                            query = Encryption.UrlEncrypt_DESCryptoServiceProvider(p[1], Web.EncryptionAlgorithms[Web.EncryptionAlgorithm]);

                        url = new Uri(p[0] + "?" + query);
                    }
                }

                string response = null;
                try
                {
                    response = wc.DownloadString(url);
                }
                catch(Exception exc)
                {
                    ;
                }
                if (Web.FormsAuthentication != null && !String.IsNullOrEmpty(Web.FormsAuthentication.QueryField))
                {
                    if (response == null && wc.Address != null && !String.IsNullOrEmpty(wc.Address.Query) && wc.Address.Query.Contains(Web.FormsAuthentication.QueryField + "="))
                    {
                        response = wc.DownloadString(wc.Address);
                        Web.FormsAuthentication.AquireCookies(wc.ResponseHeaders);
                    }
                    else if(wc.ResponseUri != null && !String.IsNullOrEmpty(wc.ResponseUri.Query) && wc.ResponseUri.Query.Contains(Web.FormsAuthentication.QueryField + "="))
                    {
                        if (wc.Headers.HasKeys() && wc.Headers.AllKeys.ToList().Contains(HttpRequestHeader.Cookie.ToString()))
                            wc.Headers.Remove(HttpRequestHeader.Cookie);
                        wc.Headers.Add(HttpRequestHeader.Cookie, Web.RequestAuthenticationCookie());
                        response = wc.DownloadString(url);
                    }
                }
                pDoc.LoadHtml(response);

               
                if (fDoc.DocumentNode != null)
                {
                    HtmlNode root = fDoc.DocumentNode;

                    foreach (DataProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.FileContent))
                    {
                        HtmlNode erg = root.SelectSingleNode(property.Xpath);
                        if(erg != null && erg.Attributes[property.MemberName] != null)
                        {
                            object value = HtmlEntity.DeEntitize(erg.Attributes[property.MemberName].Value);
                            object membersValue = GetTasksMembersValue(property);
                            value = property.PerformTasks(value, membersValue);

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
                        HtmlNodeCollection list = root.SelectNodes(property.Xpath);
                        if (list != null)
                        {
                            foreach (HtmlNode node in list)
                            {
                                property.PerformTasks(node);
                                Helper.Document.AddPageContentField(node, property, document);
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
