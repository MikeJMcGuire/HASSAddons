using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActronQue
{
	public class AirConditionerData
	{
		public string FanMode; // FanMode
		public string Mode; // Mode
		public bool On; // isOn
		public bool Continuous;
		public double SetTemperatureCooling; // TemperatureSetpoint_Cool_oC
		public double SetTemperatureHeating; // TemperatureSetpoint_Heat_oC
		public double Temperature; // LiveTemp_oC
		public string CompressorState; // CompressorMode
		public DateTime LastUpdated;
	}
}
