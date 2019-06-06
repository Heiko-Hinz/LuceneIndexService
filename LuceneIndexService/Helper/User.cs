using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace HeikoHinz.LuceneIndexService.Helper
{
    public static class User
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="user">The user name in the domain. It will probably work with just the user name, if the machine is in the same domain, or it may work with user@domain or domain\user.</param>
        /// <param name="group"></param>
        /// <returns></returns>
        public static bool IsInGroup(string user, string group)
        {
            using (var identity = new WindowsIdentity(user))
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(group);
            }
        }
    }
}
