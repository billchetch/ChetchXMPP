using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.ChetchXMPP
{
    internal class ChetchXMPPServiceException : ChetchXMPPException
    {
        public ChetchXMPPServiceException(String message) : base(message) { }
    }
}
