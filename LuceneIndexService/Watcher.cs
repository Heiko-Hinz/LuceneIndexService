using HeikoHinz.JobScheduling;
using HeikoHinz.LuceneIndexService.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HeikoHinz.LuceneIndexService
{
    public class Watcher
    {
        FileSystemWatcher watcher { get; set; } = new FileSystemWatcher();

        public string Path { get; set; }

        public List<string> Extensions { get; set; } = new List<string>();

        public ServiceIndex Index { get; set; }

        public Web Web { get; set; }

        public Uri Url { get; set; }

        public List<string> FolderAuthorizedRoles { get; set; } = new List<string>();

        public Watcher()
        {

        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        public void Start()
        {
            watcher.Path = Path;
            watcher.Filter = "*.*";
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            watcher.Changed += ScheduleJob;
            watcher.Created += ScheduleJob;
            watcher.Deleted += ScheduleJob;

            watcher.EnableRaisingEvents = true;
        }

        private void ScheduleJob(object sender, FileSystemEventArgs e)
        {
            Match match = Regex.Match(e.FullPath, Web.Filter);
            if (match.Success)
            {
                FileInfo fi = new FileInfo(e.FullPath);
                Settings.FileType file = Web.FileSettings.SingleOrDefault(f => f.Extensions.Contains(fi.Extension.TrimStart(".".ToCharArray())));
                if (file != null)
                {
                    Job job = file.Jobs.SingleOrDefault(j => j.Type == e.ChangeType);
                    if (job != null)
                    {
                        Type bType = Type.GetType(job.Namespace + "." + job.ClassName);
                        if (SchedulingServiceInstance.Instance.QueuedJobs.SingleOrDefault(j => j.GetType() == bType && bType.GetProperty("Web").GetValue(j) == Web && bType.GetProperty("Path").GetValue(j).ToString() == e.FullPath) == null)
                        {
                            BaseJob bJob = (BaseJob)Activator.CreateInstance(bType, new object[] { Index, Web, file, e.FullPath, new Uri(Url, e.Name), FolderAuthorizedRoles });
                            SchedulingServiceInstance.Instance.EnqeueJob(bJob);
                        }
                    }
                }
            }
            else if (!e.Name.Contains("."))
            {
                if (e.ChangeType == WatcherChangeTypes.Created)
                {
                    DirectoryInfo di = new DirectoryInfo(e.FullPath);
                    Settings.Directory directory = Web.Directories.Single(d => d.Include);
                    List<string> _folderAuthorizedRoles = Helper.FileSystem.GetFolderAuthorizedRoles(di.FullName, FolderAuthorizedRoles);
                    Main.StartWatcher(Index, Web, new Uri(Url, e.Name + "/"), di, Web.Filter.Split(";".ToCharArray()).ToList(), directory.IndexSubfolders, Web.Directories.Where(d => !d.Include).Select(d => d.Path).ToList(), _folderAuthorizedRoles, false);
                }
                else if (e.ChangeType == WatcherChangeTypes.Deleted)
                {
                    SchedulingServiceInstance.Instance.EnqeueJob(new Jobs.CheckPathJob(Index, Web, e.FullPath, new Uri(Url, e.Name + "/"), DateTime.Now, FolderAuthorizedRoles, false), true);

                    Main.StopWatcher(e.FullPath);
                }
            }
        }
        public void Stop()
        {
            watcher.Dispose();

        }
    }
}
