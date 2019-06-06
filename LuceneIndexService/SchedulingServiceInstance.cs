using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeikoHinz.LuceneIndexService
{
    public class SchedulingServiceInstance : HeikoHinz.JobScheduling.Service
    {
        public SchedulingServiceInstance()
        {
            InstanceCreated?.Invoke(this, new EventArgs());
        }

 
    }
}
