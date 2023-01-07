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
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
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
    public abstract class BaseAnalysingJob : BaseJob
    {
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

        [XmlAttribute]
        public DocumentTypes DocumentType { get; set; }

        [XmlElement("FolderAuthorizedRole")]
        public List<string> FolderAuthorizedRoles { get; set; } = new List<string>();

        [XmlAttribute]
        public bool IsSearchable { get; set; } = true;

        [XmlIgnore]
        public Dictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();

        [XmlIgnore]
        List<Tuple<string, string, bool>> ReadingRights { get; set; }

        #region Konstruktor

        public BaseAnalysingJob()
        {
            Init();
        }
        public BaseAnalysingJob(ServiceIndex index, Web web, Settings.FileType fileSettings, string path, Uri url, List<string> folderAuthorizedRoles)
        {
            this.Index = index;
            this.Web = web;
            this.FileSettings = fileSettings;
            this.Path = path;
            this.Url = url;
            this.FolderAuthorizedRoles = folderAuthorizedRoles;

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
            DocumentType = DocumentTypes.Unknown;
            Description = "Ist ein Basismodul zum Analysieren von Dokumenten";
        }
        #endregion

        #region CommonAnalyzing
        public void CommonAnalyzing(Document document, BooleanQuery identitiesQuery)
        {
            try
            {
                if (Index.IndexingService.IsStopping)
                    return;

                FileInfo fi = new FileInfo(Path);
                ReadingRights = ReadingRights = Helper.FileSystem.GetReadingRights(fi);
                Uri url = new Uri(Url.AbsoluteUri);

                foreach (SimpleProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.Job))
                {
                    PropertyInfo member = HeikoHinz.Helper.GetMembersPropertyInfo(this, property.MemberName);
                    object value = member.GetValue(this);
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
                foreach (SimpleProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.FileInfo))
                {
                    PropertyInfo member = fi.GetType().GetProperty(property.MemberName);
                    object value = member.GetValue(fi);
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
                foreach (SimpleProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.Uri))
                {
                    PropertyInfo member = Url.GetType().GetProperty(property.MemberName);
                    object value = member.GetValue(Url);
                    value = property.PerformTasks(value);

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
                /*foreach (ListProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.List))
                {
                    if (property.Name == "AuthorizedGroups")
                    {
                        List<string> value = new List<string>();

                        List<string> groups = property.Filter.Split(property.SplitChars.ToCharArray()).ToList();
                        foreach (string group in ReadingRights.Where(gr => gr.Item3 && groups.Contains(gr.Item2)).Select(gr => gr.Item2))
                        {
                            Lucene.Net.Documents.Field field = new Lucene.Net.Documents.Field(property.Name, group, Lucene.Net.Documents.Field.Store.YES, property.Index, property.TermVector);
                            field.Boost = property.Boost;
                            document.Add(field);
                        }
                    }
                }*/
            }
            catch (Exception exc)
            {
                HasError = true;
                Properties.AddProperty("Source", GetType().Namespace);
                Service.LogError(DateTime.Now, Properties, exc);
            }
        }

        #endregion

        #region ShellPropertyAnalyzing
        public void ShellPropertyAnalyzing(Document document, BooleanQuery identitiesQuery)
        {
            try
            {
                if (Index.IndexingService.IsStopping)
                    return;

                using (var file = ShellFile.FromFilePath(Path))
                {
                    if (file != null)
                    {
                        foreach (SimpleProperty property in FileSettings.Properties.Where(p => p.Source == DataSources.DocumentInfo))
                        {
                            try
                            {
                                PropertyInfo member = file.Properties.System.GetType().GetProperty(property.MemberName);
                                object memberValue = member.GetValue(file.Properties.System);

                                object value = null;
                                if (memberValue is ShellProperty<string>)
                                    value = ((ShellProperty<string>)memberValue).Value;
                                else if (memberValue is ShellProperty<string[]> && memberValue != null)
                                {
                                    string[] memberValueArray = ((ShellProperty<string[]>)memberValue).Value;
                                    if (memberValueArray != null && !memberValueArray.Any())
                                        value = String.Join(", ", memberValueArray);
                                }

                                if (value != null)
                                {
                                    value = property.PerformTasks(value);

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
                    }
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

        public object GetTasksMembersValue(BaseProperty property)
        {
            object membersValue = null;
            PropertyTask task = property.Tasks.SingleOrDefault(t => t.Condition != PropertyTaskConditions.NoSet && !String.IsNullOrEmpty(t.Name));
            if (task != null)
            {
                if (task.Source == PropertyTaskSources.Job)
                {
                    PropertyInfo member = HeikoHinz.Helper.GetMembersPropertyInfo(this, task.Name);
                    membersValue = member.GetValue(this);
                }
                else if (task.Source == PropertyTaskSources.Variable && this.Variables.ContainsKey(task.Name))
                    membersValue = this.Variables[task.Name];
            }
            return membersValue;
        }
    }
}
