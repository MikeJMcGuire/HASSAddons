using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActronQue
{
	public class AirConditionerPeripheral
	{
		public string SerialNumber; // SerialNumber
		public double Battery; // RemainingBatteryCapacity_pc

		public AirConditionerPeripheral(string strSerialNumber)
		{
			SerialNumber = strSerialNumber;
			Battery = 0;
		}
	}
}
