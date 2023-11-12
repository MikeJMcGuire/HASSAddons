using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading;

namespace HMX.HASSActronQue
{
	internal class Service
	{
		private static string _strServiceName = "hass-actronque";
		private static string _strDeviceNameMQTT = "Actron Que Air Conditioner";
		private static string _strConfigFile = "/data/options.json";
		private static ManualResetEvent _eventStop = new ManualResetEvent(false);
		private static bool _bDevelopment = false;

		public static bool IsDevelopment
		{
			get { return _bDevelopment; }
		}

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
			IHost webHost;
			string strMQTTUser, strMQTTPassword, strMQTTBroker;
			string strQueUser, strQuePassword, strQueSerial, strSystemType;
			int iPollInterval;
			bool bPerZoneControls, bPerZoneSensors, bMQTTTLS, bSeparateHeatCool;

			Logging.WriteDebugLog("Service.Start() Build Date: {0}", Properties.Resources.BuildDate);

			// Environment Check
			if ((Environment.GetEnvironmentVariable("Development") ?? "").ToLower() == "true")
			{
				_bDevelopment = true;
				Logging.WriteDebugLog("Service.Start() Development Mode");
			}

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
			Configuration.GetOptionalConfiguration(configuration, "MQTTTLS", out bMQTTTLS);

			if (!Configuration.GetConfiguration(configuration, "PerZoneControls", out bPerZoneControls))
				return;

			Configuration.GetOptionalConfiguration(configuration, "PerZoneSensors", out bPerZoneSensors);		

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

			Configuration.GetOptionalConfiguration(configuration, "SeparateHeatCoolTargets", out bSeparateHeatCool);

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

			try
			{
				webHost = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.UseStartup<ASPNETCoreStartup>().UseConfiguration(configuration).UseUrls($"http://*:80/");
				}).Build();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Service.Start()", eException, "Unable to build web server instance.");
				return;
			}

			MQTT.StartMQTT(strMQTTBroker, bMQTTTLS, _strServiceName, strMQTTUser, strMQTTPassword, MQTTProcessor);

			Que.Initialise(strQueUser, strQuePassword, strQueSerial, strSystemType, iPollInterval, bPerZoneControls, bPerZoneSensors, bSeparateHeatCool, _eventStop);

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
			string strUnit, strUnitHeader;

			Logging.WriteDebugLog("Service.MQTTProcessor() [0x{0}] {1}", lRequestId.ToString("X8"), strTopic);

			// Determine Unit
			strUnit = strTopic.Substring(9, strTopic.IndexOf("/") - 9);
			strUnitHeader = strTopic.Substring(0, strTopic.IndexOf("/"));

			if (!Que.Units.ContainsKey(strUnit))
			{
				Logging.WriteDebugLog("Service.MQTTProcessor() [0x{0}] Can not locate unit: {1}", lRequestId.ToString("X8"), strTopic, strUnit);
				return;
			}

			// Per Zone Temperature
			if (strTopic.StartsWith(strUnitHeader + "/zone") && strTopic.Contains("/temperature/"))
			{
				iZone = int.Parse(strTopic.Substring(strUnitHeader.Length + 5, 1));

				// Temperature
				if (strTopic.EndsWith("/temperature/set"))
				{
					if (double.TryParse(strPayload, out dblTemperature))
						Que.ChangeTemperature(lRequestId, Que.Units[strUnit], dblTemperature, iZone, Que.TemperatureSetType.Default);
				}
				// Temperature High
				else if (strTopic.EndsWith("/high/set"))
				{
					if (double.TryParse(strPayload, out dblTemperature))
						Que.ChangeTemperature(lRequestId, Que.Units[strUnit], dblTemperature, iZone, Que.TemperatureSetType.High);
				}
				// Temperature Low
				else if (strTopic.EndsWith("/low/set"))
				{
					if (double.TryParse(strPayload, out dblTemperature))
						Que.ChangeTemperature(lRequestId, Que.Units[strUnit], dblTemperature, iZone, Que.TemperatureSetType.Low);
				}
			}
			// Per Zone Mode
			else if (strTopic.StartsWith(strUnitHeader + "/zone") && strTopic.EndsWith("/mode/set"))
			{
				iZone = int.Parse(strTopic.Substring(strUnitHeader.Length + 5, 1));

				switch (strPayload)
				{
					case "off":
						Que.ChangeZone(lRequestId, Que.Units[strUnit], iZone, false);

						break;

					case "auto":
						Que.ChangeZone(lRequestId, Que.Units[strUnit], iZone, true);
						Que.ChangeMode(lRequestId, Que.Units[strUnit], AirConditionerMode.Automatic);

						break;

					case "cool":
						Que.ChangeZone(lRequestId, Que.Units[strUnit], iZone, true);
						Que.ChangeMode(lRequestId, Que.Units[strUnit], AirConditionerMode.Cool);

						break;

					case "heat":
						Que.ChangeZone(lRequestId, Que.Units[strUnit], iZone, true);
						Que.ChangeMode(lRequestId, Que.Units[strUnit], AirConditionerMode.Heat);

						break;

					case "fan_only":
						Que.ChangeZone(lRequestId, Que.Units[strUnit], iZone, true);
						Que.ChangeMode(lRequestId, Que.Units[strUnit], AirConditionerMode.Fan_Only);

						break;
				}
			}
			// Zone
			else if (strTopic.StartsWith(strUnitHeader + "/zone") && strTopic.EndsWith("/set"))
			{
				iZone = int.Parse(strTopic.Substring(strUnitHeader.Length + 5, 1));

				Que.ChangeZone(lRequestId, Que.Units[strUnit], iZone, strPayload == "ON" ? true : false);
			}
			// Master
			else if (strTopic.StartsWith(strUnitHeader + "/mode/set"))
			{
				switch (strPayload)
				{
					case "off":
						Que.ChangeMode(lRequestId, Que.Units[strUnit], AirConditionerMode.Off);

						break;

					case "auto":
						Que.ChangeMode(lRequestId, Que.Units[strUnit], AirConditionerMode.Automatic);

						break;

					case "cool":
						Que.ChangeMode(lRequestId, Que.Units[strUnit], AirConditionerMode.Cool);

						break;

					case "heat":
						Que.ChangeMode(lRequestId, Que.Units[strUnit], AirConditionerMode.Heat);

						break;

					case "fan_only":
						Que.ChangeMode(lRequestId, Que.Units[strUnit], AirConditionerMode.Fan_Only);

						break;
				}
			}
			// Control All Zones
			else if (strTopic.StartsWith(strUnitHeader + "/controlallzones/set"))
			{
				Que.ChangeControlAllZones(lRequestId, Que.Units[strUnit], strPayload == "ON" ? true : false);
			}
			// Fan Speed
			else if (strTopic.StartsWith(strUnitHeader + "/fan/set"))
			{
				switch (strPayload)
				{
					case "auto":
						Que.ChangeFanMode(lRequestId, Que.Units[strUnit], FanMode.Automatic);

						break;

					case "low":
						Que.ChangeFanMode(lRequestId, Que.Units[strUnit], FanMode.Low);

						break;

					case "medium":
						Que.ChangeFanMode(lRequestId, Que.Units[strUnit], FanMode.Medium);

						break;

					case "high":
						Que.ChangeFanMode(lRequestId, Que.Units[strUnit], FanMode.High);

						break;
				}
			}
			// Temperature
			else if (strTopic.StartsWith(strUnitHeader + "/temperature"))
			{
				// Temperature
				if (strTopic.EndsWith("/temperature/set"))
				{
					if (double.TryParse(strPayload, out dblTemperature))
						Que.ChangeTemperature(lRequestId, Que.Units[strUnit], dblTemperature, 0, Que.TemperatureSetType.Default);
				}
				// Temperature High
				else if (strTopic.EndsWith("/high/set"))
				{
					if (double.TryParse(strPayload, out dblTemperature))
						Que.ChangeTemperature(lRequestId, Que.Units[strUnit], dblTemperature, 0, Que.TemperatureSetType.High);
				}
				// Temperature Low
				else if (strTopic.EndsWith("/low/set"))
				{
					if (double.TryParse(strPayload, out dblTemperature))
						Que.ChangeTemperature(lRequestId, Que.Units[strUnit], dblTemperature, 0, Que.TemperatureSetType.Low);
				}
			}
		}
    }
}
