using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActronQue
{
	public class QueueCommand
	{
		public long RequestId;
		public QueueCommandData Data;
		public DateTime Expires;

		public QueueCommand(long lRequestId, DateTime dtExpires)
		{
			RequestId = lRequestId;
			Data = new QueueCommandData();
			Expires = dtExpires;
		}
	}
}
