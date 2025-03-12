using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActronQue
{
	public class QueueCommand
	{
		public long RequestId;
		public long OriginalRequestId;
		public QueueCommandData Data;
		public DateTime Expires;
		public AirConditionerUnit Unit;

		public QueueCommand(long lRequestId, AirConditionerUnit unit, DateTime dtExpires)
		{
			RequestId = RequestManager.GetRequestId();
			OriginalRequestId = lRequestId;
			Unit = unit;
			Data = new QueueCommandData();
			Expires = dtExpires;
		}
	}
}
