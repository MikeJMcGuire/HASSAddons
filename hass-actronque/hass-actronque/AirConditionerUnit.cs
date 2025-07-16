using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace HMX.HASSActronQue
{
	public class AirConditionerUnit
	{
		public string Name;
		public string NextEventURL;
		public string Serial;
		public bool Online;
		public string ModelType;
		public AirConditionerData Data;
		public Dictionary<int, AirConditionerZone> Zones;	
		public Dictionary<int, AirConditionerPeripheral> Peripherals;
		public HttpClient HttpClientCommands;

		public AirConditionerUnit(string strName, string strSerial, string strModelType)
		{
			HttpClientHandler httpClientHandler = new HttpClientHandler();

			if (httpClientHandler.SupportsAutomaticDecompression)
				httpClientHandler.AutomaticDecompression = System.Net.DecompressionMethods.All;

			Name = strName;
			Serial = strSerial;
			ModelType = strModelType;
			Online = true;
			NextEventURL = "";
			Data = new AirConditionerData();
			Zones = new Dictionary<int, AirConditionerZone>();
			Peripherals = new Dictionary<int, AirConditionerPeripheral>();

			if (Service.IsDevelopment)
				HttpClientCommands = new HttpClient(new LoggingClientHandler(httpClientHandler));
			else
				HttpClientCommands = new HttpClient(httpClientHandler);
		}
	}
}
