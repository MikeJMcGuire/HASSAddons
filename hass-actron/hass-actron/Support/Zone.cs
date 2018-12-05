using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HMX.HASSActron
{
    public class Zone
    {
		public string Name;
		public int Id;

		public Zone()
		{

		}

		public Zone(string strName, int iId)
		{
			Name = strName;
			Id = iId;
		}
	}	
}
