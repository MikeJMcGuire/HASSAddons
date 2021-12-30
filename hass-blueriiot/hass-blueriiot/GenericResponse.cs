using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HMX.HASSBlueriiot
{
    public class GenericResponse
    {
		public bool Successful;
		public string Reason;

		public GenericResponse(bool bSuccessful, string strReason)
		{
			this.Successful = bSuccessful;
			this.Reason = strReason;
		}
    }
}
