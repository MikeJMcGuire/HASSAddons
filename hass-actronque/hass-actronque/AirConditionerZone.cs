using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActronQue
{
	public class AirConditionerZone
	{
		public string Name; // NV_Title
		public double Temperature; // LiveTemp_oC
		public double SetTemperatureCooling; // TemperatureSetpoint_Cool_oC
		public double SetTemperatureHeating; // TemperatureSetpoint_Heat_oC
		public bool State;
		public double Position; // ZonePosition
		public Dictionary<string, AirConditionerSensor> Sensors;
		public Dictionary<string, AirConditionerPeripheral> Peripherals;
		public bool Exists;
	}
}
