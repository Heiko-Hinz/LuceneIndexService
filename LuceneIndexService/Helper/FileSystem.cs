using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace HeikoHinz.LuceneIndexService.Helper
{
    public static class FileSystem
    {
        public static List<string> GetFolderAuthorizedRoles(string path, List<string> parentRoles)
        {
            List<string> folderAuthorizedRoles = new List<string>();

            string configFilePath = System.IO.Path.Combine(path, "Web.config");
            if (File.Exists(configFilePath))
            {
                XmlDocument configDoc = new XmlDocument();
                configDoc.Load(configFilePath);
                if (configDoc != null)
                {
                    XmlNode allow = configDoc.DocumentElement.SelectSingleNode("//authorization/allow[@roles]");
                    if (allow != null)
                        folderAuthorizedRoles = allow.Attributes["roles"].Value.Split(",".ToCharArray()).ToList();
                    else
                        folderAuthorizedRoles.AddRange(parentRoles);
                    XmlNode deny = configDoc.DocumentElement.SelectSingleNode("//authorization/deny[@roles]");
                    if (deny != null)
                    {
                        List<string> rolesDenied = deny.Attributes["roles"].Value.Split(",".ToCharArray()).ToList();
                        folderAuthorizedRoles.RemoveAll(r => rolesDenied.Contains(r));
                    }
                }
            }
            else
                folderAuthorizedRoles.AddRange(parentRoles);

            return folderAuthorizedRoles;
        }

        public static List<Tuple<string, string, bool>> GetReadingRights(FileInfo fileInfo)
        {
            List<Tuple<string, string, bool>> readingRights = new List<Tuple<string, string, bool>>();
            FileSecurity fs = fileInfo.GetAccessControl(AccessControlSections.Access);
            foreach (FileSystemAccessRule fsar in fs.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount)))
            {
                string identity = fsar.IdentityReference.Value;
                //string userName = fsar.IdentityReference.Value;
                //string userRights = fsar.FileSystemRights.ToString();
                bool hasReadPermission = fsar.FileSystemRights.HasFlag(FileSystemRights.ReadData);
                //string userAccessType = fsar.AccessControlType.ToString();
                //string ruleSource = fsar.IsInherited ? "Inherited" : "Explicit";
                //string rulePropagation = fsar.PropagationFlags.ToString();
                //string ruleInheritance = fsar.InheritanceFlags.ToString();

                readingRights.Add(Tuple.Create(identity, fsar.IdentityReference.Translate(typeof(System.Security.Principal.NTAccount)).Value, hasReadPermission));
            }
            return readingRights;
        }
        public static bool HasReadPermissions(FileSystemRights toRemainSilent)
        {
            toRemainSilent = MapGenericRightsToFileSystemRights(toRemainSilent);

            if (toRemainSilent.HasFlag(FileSystemRights.ReadData) ||
                toRemainSilent.HasFlag(FileSystemRights.Read) ||
                toRemainSilent.HasFlag(FileSystemRights.Modify) ||
                toRemainSilent.HasFlag(FileSystemRights.ListDirectory) ||
                toRemainSilent.HasFlag(FileSystemRights.ReadAndExecute) ||
                toRemainSilent.HasFlag(FileSystemRights.ReadExtendedAttributes) ||
                toRemainSilent.HasFlag(FileSystemRights.TakeOwnership) ||
                toRemainSilent.HasFlag(FileSystemRights.ChangePermissions) ||
                toRemainSilent.HasFlag(FileSystemRights.FullControl) ||
                toRemainSilent.HasFlag(FileSystemRights.DeleteSubdirectoriesAndFiles) ||
                toRemainSilent.HasFlag(FileSystemRights.Delete))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public enum GenericRights : uint
        {
            GENERIC_READ = 0x80000000,
            GENERIC_WRITE = 0x40000000,
            GENERIC_EXECUTE = 0x20000000,
            GENERIC_ALL = 0x10000000
        }
        private static FileSystemRights MapGenericRightsToFileSystemRights(FileSystemRights OriginalRights)
        {

            FileSystemRights MappedRights = new FileSystemRights();
            bool blnWasNumber = false;
            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.GENERIC_EXECUTE)))
            {
                MappedRights = MappedRights | FileSystemRights.ExecuteFile | FileSystemRights.ReadPermissions | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;
                blnWasNumber = true;
            }

            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.GENERIC_READ)))
            {
                MappedRights = MappedRights | FileSystemRights.ReadAttributes | FileSystemRights.ReadData | FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize;
                blnWasNumber = true;
            }

            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.GENERIC_WRITE)))
            {
                MappedRights = MappedRights | FileSystemRights.AppendData | FileSystemRights.WriteAttributes | FileSystemRights.WriteData | FileSystemRights.WriteExtendedAttributes | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize;
                blnWasNumber = true;
            }

            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.GENERIC_ALL)))
            {
                MappedRights = MappedRights | FileSystemRights.FullControl;
                blnWasNumber = true;
            }

            if (blnWasNumber == false)
            {
                MappedRights = OriginalRights;
            }

            return MappedRights;
        }
    }
}
