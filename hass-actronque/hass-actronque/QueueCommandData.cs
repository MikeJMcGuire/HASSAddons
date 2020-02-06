using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActronQue
{
	public class QueueCommandData
	{
		public Dictionary<string, object> command;

		public QueueCommandData()
		{
			command = new Dictionary<string, object>();
		}
	}
}
