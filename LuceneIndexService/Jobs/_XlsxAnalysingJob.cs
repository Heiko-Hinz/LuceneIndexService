using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace HeikoHinz.LuceneIndexService.Jobs
{
    public class XlsxAnalysingJob : BaseAnalysingJob
    {
        private delegate void MethodDelegate();
        private MethodDelegate invoker;

        #region Konstruktor

        public XlsxAnalysingJob() : base()
        {
            Init();
        }
        public XlsxAnalysingJob(ServiceIndex index, Web web, Settings.FileType fileSettings, string path, Uri url, List<string> folderAuthorizedRoles) : 
            base(index, web, fileSettings, path, url, folderAuthorizedRoles)
        {
            Init();
        }

        #endregion

        #region Init
        private void Init()
        {
            DocumentType = DocumentTypes.Document;
            Description = "Analysiert das Excel-Dokument und fügt es dem Index hinzu.";
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
                
                using (SpreadsheetDocument excelDoc = SpreadsheetDocument.Open(Path, false))
                {
                    try
                    {
                        if (excelDoc != null)
                        {
                            PackageProperties pp = excelDoc.PackageProperties;                           

                            foreach (SimpleProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.DocumentInfo))
                            {
                                PropertyInfo member = pp.GetType().GetProperty(property.MemberName);
                                try
                                {
                                    object value = member.GetValue(pp);

                                    if (value != null)
                                    {
                                        value = property.TakeOverModifications(value);

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
                                        SharedStringTable sharedStringTable = excelDoc.WorkbookPart.SharedStringTablePart.SharedStringTable;
                                        foreach (WorksheetPart worksheetPart in excelDoc.WorkbookPart.WorksheetParts)
                                        {
                                            foreach (SheetData sheetData in worksheetPart.Worksheet.Elements<SheetData>())
                                            {
                                                if (sheetData.HasChildren)
                                                {
                                                    foreach (Row row in sheetData.Elements<Row>())
                                                    {
                                                        foreach (Cell cell in row.Elements<Cell>())
                                                        {
                                                            if (cell.DataType != null && cell.DataType == CellValues.SharedString)
                                                            {
                                                                int ssid = int.Parse(cell.CellValue.Text);
                                                                value += String.Format("{0} ", sharedStringTable.ElementAt(ssid).InnerText.Trim());
                                                            }
                                                            else if (cell.CellValue != null)
                                                                value += String.Format("{0} ", cell.CellValue.Text.Trim());
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        value = property.TakeOverModifications(value);

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
                    catch (Exception exc)
                    {
                        ;
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
