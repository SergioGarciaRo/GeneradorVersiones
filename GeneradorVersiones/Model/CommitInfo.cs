using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeneradorVersiones.Model
{
    public class CommitInfo
    {
        public string Sha { get;set; }
        public string Author { get;set; }
        public string Message { get; set; }
    }
}
