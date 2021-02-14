using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading;

namespace HMX.HASSActronQue
{
    internal class Service
    {
		private static string _strServiceName = "hass-actronque";
		private static string _strDeviceNameMQTT = "Actron Que Air Conditioner";
		private static string _strConfigFile = "/data/options.json";
		private static ManualResetEvent _eventStop = new ManualResetEvent(false);

		public static string ServiceName
		{
			get { return _strServiceName; }
		}

		public static string DeviceNameMQTT
		{
			get { return _strDeviceNameMQTT; }
		}


		public static void Start()
        {
			IConfigurationRoot configuration;
			IWebHost webHost;
			string strMQTTUser, strMQTTPassword, strMQTTBroker;
			string strQueUser, strQuePassword, strQueSerial, strSystemType;
			int iPollInterval;
			bool bPerZoneControls;

			Logging.WriteDebugLog("Service.Start() Build Date: {0}", Properties.Resources.BuildDate);

			// Load Configuration
			try
			{
				configuration = new ConfigurationBuilder().AddJsonFile(_strConfigFile, false, true).Build();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Service.Start()", eException, "Unable to build configuration instance.");
				return;
			}

			Configuration.GetOptionalConfiguration(configuration, "MQTTUser", out strMQTTUser);
			Configuration.GetPrivateOptionalConfiguration(configuration, "MQTTPassword", out strMQTTPassword);
			if (!Configuration.GetConfiguration(configuration, "MQTTBroker", out strMQTTBroker))
				return;

			if (!Configuration.GetConfiguration(configuration, "PerZoneControls", out bPerZoneControls))
				return;

			if (!Configuration.GetConfiguration(configuration, "PollInterval", out iPollInterval) || iPollInterval < 10 || iPollInterval > 300)
			{
				Logging.WriteDebugLog("Service.Start() Poll interval must be between 10 and 300 (inclusive)");
				return;
			}

			if (!Configuration.GetConfiguration(configuration, "QueUser", out strQueUser))
				return;
			if (!Configuration.GetPrivateConfiguration(configuration, "QuePassword", out strQuePassword))
				return;
			Configuration.GetOptionalConfiguration(configuration, "QueSerial", out strQueSerial);
			
			Configuration.GetOptionalConfiguration(configuration, "SystemType", out strSystemType);
			if (strSystemType == "")
			{
				Logging.WriteDebugLog("Service.Start() System Type not specified, defaulting to que.");
				strSystemType = "que";
			}
			else
			{
				strSystemType = strSystemType.ToLower().Trim();
				if (strSystemType != "que" && strSystemType != "neo")
				{
					Logging.WriteDebugLog("Service.Start() System Type must be que or neo.");
					return;
				}
			}

			MQTT.StartMQTT(strMQTTBroker, _strServiceName, strMQTTUser, strMQTTPassword, MQTTProcessor);

			Que.Initialise(strQueUser, strQuePassword, strQueSerial, strSystemType, iPollInterval, bPerZoneControls, _eventStop);

			try
			{
				webHost = new WebHostBuilder().UseKestrel().UseStartup<ASPNETCoreStartup>().UseConfiguration(configuration).UseUrls($"http://*:80/").Build();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Service.Start()", eException, "Unable to build Kestrel instance.");
				return;
			}

			webHost.Run();
		}

		public static void Stop()
		{
			Logging.WriteDebugLog("Service.Stop()");

			_eventStop.Set();

			MQTT.StopMQTT();
		}

		private static void MQTTProcessor(string strTopic, string strPayload)
		{
			long lRequestId = RequestManager.GetRequestId();
			int iZone = 0;
			double dblTemperature = 0;

			Logging.WriteDebugLog("Service.MQTTProcessor() [0x{0}] {1}", lRequestId.ToString("X8"), strTopic);

			// Per Zone Temperature
			if (strTopic.StartsWith("actronque/zone") && strTopic.EndsWith("/temperature/set"))
			{
				iZone = int.Parse(strTopic.Substring(14, 1));

				if (double.TryParse(strPayload, out dblTemperature))
					Que.ChangeTemperature(lRequestId, dblTemperature, iZone);
			}
			// Per Zone Mode
			else if (strTopic.StartsWith("actronque/zone") && strTopic.EndsWith("/mode/set"))
			{
				iZone = int.Parse(strTopic.Substring(14, 1));

				switch (strPayload)
				{
					case "off":
						Que.ChangeZone(lRequestId, iZone, false);

						break;

					case "auto":
						Que.ChangeZone(lRequestId, iZone, true);
						Que.ChangeMode(lRequestId, AirConditionerMode.Automatic);

						break;

					case "cool":
						Que.ChangeZone(lRequestId, iZone, true); 
						Que.ChangeMode(lRequestId, AirConditionerMode.Cool);

						break;

					case "heat":
						Que.ChangeZone(lRequestId, iZone, true); 
						Que.ChangeMode(lRequestId, AirConditionerMode.Heat);

						break;

					case "fan_only":
						Que.ChangeZone(lRequestId, iZone, true); 
						Que.ChangeMode(lRequestId, AirConditionerMode.Fan_Only);

						break;
				}
			}
			// Zone
			else if (strTopic.StartsWith("actronque/zone") && strTopic.EndsWith("/set"))
			{
				iZone = int.Parse(strTopic.Substring(14, 1));

				Que.ChangeZone(lRequestId, iZone, strPayload == "ON" ? true : false);
			}
			// Master
			else
			{
				switch (strTopic)
				{
					// Mode
					case "actronque/mode/set":
						switch (strPayload)
						{
							case "off":
								Que.ChangeMode(lRequestId, AirConditionerMode.Off);

								break;

							case "auto":
								Que.ChangeMode(lRequestId, AirConditionerMode.Automatic);

								break;

							case "cool":
								Que.ChangeMode(lRequestId, AirConditionerMode.Cool);

								break;

							case "heat":
								Que.ChangeMode(lRequestId, AirConditionerMode.Heat);

								break;

							case "fan_only":
								Que.ChangeMode(lRequestId, AirConditionerMode.Fan_Only);

								break;
						}

						break;

					// Fan Speed
					case "actronque/fan/set":
						switch (strPayload)
						{
							case "auto":
								Que.ChangeFanMode(lRequestId, FanMode.Automatic);

								break;

							case "low":
								Que.ChangeFanMode(lRequestId, FanMode.Low);

								break;

							case "medium":
								Que.ChangeFanMode(lRequestId, FanMode.Medium);

								break;

							case "high":
								Que.ChangeFanMode(lRequestId, FanMode.High);

								break;
						}

						break;

					// Temperature
					case "actronque/temperature/set":
						if (double.TryParse(strPayload, out dblTemperature))
							Que.ChangeTemperature(lRequestId, dblTemperature, 0);

						break;
				}
			}
		}
    }
}
