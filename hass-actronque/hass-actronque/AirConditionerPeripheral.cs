using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActronQue
{
	public class AirConditionerPeripheral
	{
		public string SerialNumber; // SerialNumber
		public string DeviceType; // DeviceType
		public double Battery; // RemainingBatteryCapacity_pc

		public AirConditionerPeripheral()
		{
			Battery = 0;
		}
	}
}
