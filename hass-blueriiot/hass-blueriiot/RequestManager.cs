using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSBlueriiot
{
    public class RequestManager
    {
		private static long _lRequestId = 0;
		private static object _oLockRequestId = new object();

		public static long GetRequestId(long lRequestId)
		{
			if (lRequestId == 0)
				return GetRequestId();
			else
				return lRequestId;
		}

		public static long GetRequestId()
		{
			lock (_oLockRequestId)
			{
				return _lRequestId++;
			}		
		}
	}
}
