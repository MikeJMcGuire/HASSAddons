using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HMX.HASSActron
{
	public struct AirConditionerCommand
	{
		public bool amOn;
		public double tempTarget;
		public int fanSpeed;
		public int mode;
		public string enabledZones;
	}
}
