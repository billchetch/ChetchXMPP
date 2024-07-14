using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.ChetchXMPP
{
    public class ChetchXMPPException : Exception
    {
        public ChetchXMPPException(String message) : base(message) { }
    }
}
