using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeikoHinz.LuceneIndexService.Helper
{
    public static class Document
    {
        public static void AddPageContentField(HtmlNode node, DataProperty property, Lucene.Net.Documents.Document document)
        {
            string content = String.Join("\n", node.InnerText.Replace("\r", "").Split("\n".ToCharArray()).Where(l => l.Trim().Length > 0).Select(l => l.Trim()));
            object value = HtmlEntity.DeEntitize(content); // node.InnerText.Replace("\r", "").Trim();
            //value = property.PerformTasks(value);

            Lucene.Net.Documents.Field field = new Lucene.Net.Documents.Field(property.Name, (string)value, Lucene.Net.Documents.Field.Store.YES, property.Index, property.TermVector);
            field.Boost = property.Boost;
            document.Add(field);
        }

    }
}
