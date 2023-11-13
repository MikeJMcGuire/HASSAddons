using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HMX.HASSActron
{
	public struct AirConditionerData
	{
		public bool bOn;
		public bool bESPOn;
		public int iMode;
		public int iFanSpeed;
		public double dblSetTemperature;
		public double dblRoomTemperature;
		public int iFanContinuous;
		public int iCompressorActivity;
		public string strErrorCode;
		public bool bZone1;
		public bool bZone2;
		public bool bZone3;
		public bool bZone4;
		public bool bZone5;
		public bool bZone6;
		public bool bZone7;
		public bool bZone8;
		public double dblZone1Temperature;
		public double dblZone2Temperature;
		public double dblZone3Temperature;
		public double dblZone4Temperature;
		public double dblZone5Temperature;
		public double dblZone6Temperature;
		public double dblZone7Temperature;
		public double dblZone8Temperature;
		public DateTime dtLastUpdate;
		public DateTime dtLastRequest;
	}
}
