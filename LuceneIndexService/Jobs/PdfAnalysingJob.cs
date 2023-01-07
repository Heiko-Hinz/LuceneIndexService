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
//using UglyToad.PdfPig;
//using UglyToad.PdfPig.Content;
//using iTextSharp.text;
//using iTextSharp.text.pdf;
//using PdfDocument = iTextSharp.text.pdf.PdfDocument;
using System.Collections;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace HeikoHinz.LuceneIndexService.Jobs
{
    public class PdfAnalysingJob : BaseAnalysingJob
    {
        private delegate void MethodDelegate();
        private MethodDelegate invoker;

        #region Konstruktor

        public PdfAnalysingJob() : base()
        {
            Init();
        }
        public PdfAnalysingJob(ServiceIndex index, Web web, Settings.FileType fileSettings, string path, Uri url, List<string> folderAuthorizedRoles) :
            base(index, web, fileSettings, path, url, folderAuthorizedRoles)
        {
            Init();
        }

        #endregion

        #region Init
        private void Init()
        {
            DocumentType = DocumentTypes.Document;
            Description = "Analysiert das PDF-Dokument und fügt es dem Index hinzu.";
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

                try
                {
                    using (PdfReader pdfReader = new PdfReader(Path))
                    {
                        using (PdfDocument pdfDoc = new PdfDocument(pdfReader))
                        {
                            PdfDocumentInfo info = pdfDoc.GetDocumentInfo();
                            
                            foreach (SimpleProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.DocumentInfo))
                            {
                                try
                                {
                                    MethodInfo method = info.GetType().GetMethod(property.MemberName);
                                    object value = method.Invoke(info, null);
                                    object membersValue = GetTasksMembersValue(property);
                                    value = property.PerformTasks(value, membersValue);

                                    if (value != null)
                                    {
                                        if (property.IsVariable)
                                            Variables.Add(property.Name, value);
                                        else
                                        {
                                            AbstractField field = property.GetDocumentField(value);
                                            document.Add(field);

                                            if (property.Identity)
                                                identitiesQuery.Add(property.CreateQuery(value), Occur.MUST);
                                        }
                                    }
                                }
                                catch (Exception exc)
                                {
                                    ;
                                }
                            }

                            foreach (DataProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.DocumentContent))
                            {
                                if (property.Name == "content")
                                {
                                    try
                                    {
                                        object value = "";
                                        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                                        {
                                            PdfPage page = pdfDoc.GetPage(i);
                                            try
                                            {
                                                value += String.Format(" {0}", PdfTextExtractor.GetTextFromPage(page));
                                            }
                                            catch (Exception exc)
                                            {
                                                ;
                                            }
                                        }

                                        value = property.PerformTasks(value.ToString().Trim());

                                        AbstractField field = property.GetDocumentField(value);
                                        document.Add(field);

                                        if (property.Identity)
                                            identitiesQuery.Add(property.CreateQuery(value), Occur.MUST);
                                    }
                                    catch (Exception exc)
                                    {
                                        ;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception exc)
                {
                    throw exc;
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

        const string PdfTableFormat = @"\(.*\)Tj";
        Regex PdfTableRegex = new Regex(PdfTableFormat, RegexOptions.Compiled);

        List<string> ExtractPdfContent(string rawPdfContent)
        {
            var matches = PdfTableRegex.Matches(rawPdfContent);

            var list = matches.Cast<Match>()
                .Select(m => m.Value
                    .Substring(1) //remove leading (
                    .Remove(m.Value.Length - 4) //remove trailing )Tj
                    .Replace(@"\)", ")") //unencode parens
                    .Replace(@"\(", "(")
                    .Trim()
                )
                .ToList();
            return list;
        }

        string ReplaceOctalCode(string text)
        {
            Regex RxCode = new Regex(@"\\([0-9]{3})");
            string erg = RxCode.Replace(
                text,
                delegate (Match match) {
                    return "" + (char)Convert.ToInt32(match.Groups[1].Value, 8);
                }
            );
            return erg;
        }

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
