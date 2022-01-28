using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActronQue
{
	public class AirConditionerZone
	{
		public string Name; // NV_Title
		public double Temperature; // LiveTemp_oC
		public double Humidity; // LiveHumidity_pc
		public double SetTemperatureCooling; // TemperatureSetpoint_Cool_oC
		public double SetTemperatureHeating; // TemperatureSetpoint_Heat_oC
		public double Battery; // Battery_pc
		public bool State;
		public double Position; // ZonePosition

	}
}
