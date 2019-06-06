using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.De;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Support;

namespace HeikoHinz.LuceneIndexService
{
    public class IndexingServiceInstance
    {
        public bool IsStopping { get; set; } = false;

        public IndexWriter Writer { get; set; }

        public IndexReader Reader
        {
            get
            {
                IndexReader reader = Writer.GetReader();
                if (!reader.IsCurrent())
                    reader.Reopen();
                return reader;
            }
        }

        public IndexSearcher Searcher
        {
            get
            {
                return new IndexSearcher(Reader);
            }
        }

        public IndexingServiceInstance(Settings.ServiceIndex index)
        {
            Writer = index.CreateWriter();

        }

        public QueryParser GetStandardParser(string fieldName)
        {
            return new QueryParser(Settings.ServiceIndex.Version, fieldName, Writer.Analyzer);
        }


        public void Stop()
        {
            IsStopping = true;
            if (Writer != null)
            {
                Directory directory = Writer.Directory;

                Writer.Commit();
                Writer.Dispose(true);

                directory.Dispose();
            }

        }
    }
}