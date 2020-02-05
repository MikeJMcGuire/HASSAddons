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
		private static string _strConfigFile = "/data/options.json";
		private static ManualResetEvent _eventStop = new ManualResetEvent(false);

		public static string ServiceName
		{
			get { return _strServiceName; }
		}

		public static void Start()
        {
			IConfigurationRoot configuration;
			IWebHost webHost;
			string strMQTTUser, strMQTTPassword, strMQTTBroker;
			string strQueUser, strQuePassword, strQueSerial;
			int iZoneCount;

			Logging.WriteDebugLog("Service.Start()");

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

			Configuration.GetOptionalConfiguration(configuration["MQTTUser"] ?? "", out strMQTTUser);
			Configuration.GetOptionalConfiguration(configuration["MQTTPassword"] ?? "", out strMQTTPassword);
			if (!Configuration.GetConfiguration(configuration["MQTTBroker"] ?? "", out strMQTTBroker))
				return;

			if (!Configuration.GetConfiguration(configuration["QueUser"] ?? "", out strQueUser))
				return;
			if (!Configuration.GetConfiguration(configuration["QuePassword"] ?? "", out strQuePassword))
				return;
			if (!Configuration.GetConfiguration(configuration["QueSerial"] ?? "", out strQueSerial))
				return;

			if (!Configuration.GetConfiguration(configuration["ZoneCount"] ?? "", out iZoneCount) || iZoneCount < 0 || iZoneCount > 8)
			{
				Logging.WriteDebugLog("Service.Start() Zone Count must be between 0 and 8 (inclusive)");
				return;
			}
	
			MQTT.StartMQTT(strMQTTBroker, _strServiceName, strMQTTUser, strMQTTPassword, MQTTProcessor);

			Que.Initialise(strQueUser, strQuePassword, strQueSerial, iZoneCount, _eventStop);

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
			long lRequestId = 0;
			double dblTemperature = 0;

			Logging.WriteDebugLog("Service.MQTTProcessor() {0}", strTopic);

			switch (strTopic)
			{
				case "actron/aircon/zone1/set":
					AirConditioner.ChangeZone(lRequestId, 1, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone2/set":
					AirConditioner.ChangeZone(lRequestId, 2, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone3/set":
					AirConditioner.ChangeZone(lRequestId, 3, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone4/set":
					AirConditioner.ChangeZone(lRequestId, 4, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone5/set":
					AirConditioner.ChangeZone(lRequestId, 5, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone6/set":
					AirConditioner.ChangeZone(lRequestId, 6, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone7/set":
					AirConditioner.ChangeZone(lRequestId, 7, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone8/set":
					AirConditioner.ChangeZone(lRequestId, 8, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/mode/set":
					Logging.WriteDebugLog("ServiceCore.MQTTProcessor() {0}: {1}", strTopic, strPayload);

					switch (strPayload)
					{
						case "off":
							AirConditioner.ChangeMode(lRequestId, AirConditionerMode.None);

							break;

						case "auto":
							AirConditioner.ChangeMode(lRequestId, AirConditionerMode.Automatic);

							break;

						case "cool":
							AirConditioner.ChangeMode(lRequestId, AirConditionerMode.Cooling);

							break;

						case "heat":
							AirConditioner.ChangeMode(lRequestId, AirConditionerMode.Heating);

							break;

						case "fan_only":
							AirConditioner.ChangeMode(lRequestId, AirConditionerMode.FanOnly);

							break;
					}

					break;

				case "actron/aircon/fan/set":
					Logging.WriteDebugLog("Service.MQTTProcessor() {0}: {1}", strTopic, strPayload);

					switch (strPayload)
					{
						case "low":
							AirConditioner.ChangeFanSpeed(lRequestId, FanSpeed.Low);

							break;

						case "medium":
							AirConditioner.ChangeFanSpeed(lRequestId, FanSpeed.Medium);

							break;

						case "high":
							AirConditioner.ChangeFanSpeed(lRequestId, FanSpeed.High);

							break;
					}

					break;

				case "actron/aircon/temperature/set":
					Logging.WriteDebugLog("Service.MQTTProcessor() {0}: {1}", strTopic, strPayload);

					if (double.TryParse(strPayload, out dblTemperature))
						AirConditioner.ChangeTemperature(lRequestId, dblTemperature);

					break;
			}
		}
    }

}
