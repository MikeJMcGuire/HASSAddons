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
		public HttpClient HttpClientCommands, HttpClientStatus;

		public AirConditionerUnit(string strName, string strSerial, string strModelType, string strBearerToken, Uri uriBaseAddress)
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
			{
				HttpClientStatus = new HttpClient(new LoggingClientHandler(httpClientHandler));
				HttpClientCommands = new HttpClient(new LoggingClientHandler(httpClientHandler));
			}
			else
			{
				HttpClientStatus = new HttpClient(httpClientHandler); 
				HttpClientCommands = new HttpClient(httpClientHandler);
			}

			HttpClientStatus.BaseAddress = uriBaseAddress;
			HttpClientCommands.BaseAddress = uriBaseAddress;

			UpdateBearerToken(strBearerToken);
		}

		public void UpdateBearerToken(string strBearerToken)
		{
			Logging.WriteDebugLog("AirConditionerUnit.UpdateBearerToken()");

			HttpClientStatus.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", strBearerToken);
			HttpClientCommands.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", strBearerToken);
		}
	}
}
