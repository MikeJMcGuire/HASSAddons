using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActronQue
{
	public class AirConditionerUnit
	{
		public string Name;
		public string NextEventURL;
		public string Serial;
		public AirConditionerData Data;
		public Dictionary<int, AirConditionerZone> Zones;		

		public AirConditionerUnit(string strName, string strSerial)
		{
			Name = strName;
			Serial = strSerial;
			NextEventURL = "";
			Data = new AirConditionerData();
			Zones = new Dictionary<int, AirConditionerZone>();
		}
	}
}
