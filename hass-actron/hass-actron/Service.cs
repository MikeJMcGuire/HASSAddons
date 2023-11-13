using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace HMX.HASSActron
{
    internal class Service
    {
		private static string _strServiceName = "hass-actron";		
		private static string _strConfigFile = "/data/options.json";
		private static bool _bRegisterZoneTemperatures = false;
		private static bool _bForwardToOriginalWebService = false;

		public static bool ForwardToOriginalWebService
		{
			get { return _bForwardToOriginalWebService; }
		}

		public static bool RegisterZoneTemperatures
		{
			get { return _bRegisterZoneTemperatures; }
		}

		public static string ServiceName
		{
			get { return _strServiceName; }
		}

		public static void Start()
        {
			IConfigurationRoot configuration;
			IHost webHost;
			bool bMQTTTLS, bMQTTLogging;

			Logging.WriteDebugLog("Service.Start() Build Date: {0}", Properties.Resources.BuildDate);

			try
			{
				configuration = new ConfigurationBuilder().AddJsonFile(_strConfigFile, false, true).Build();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Service.Start()", eException, "Unable to build configuration instance.");
				return;
			}

			bool.TryParse(configuration["RegisterZoneTemperatures"] ?? "false", out _bRegisterZoneTemperatures);
			bool.TryParse(configuration["ForwardToOriginalWebService"] ?? "false", out _bForwardToOriginalWebService);
			bool.TryParse(configuration["MQTTLogs"] ?? "true", out bMQTTLogging);
			bool.TryParse(configuration["MQTTTLS"] ?? "false", out bMQTTTLS);

			Logging.WriteDebugLog("Service.Start() RegisterZoneTemperatures: {0}", _bRegisterZoneTemperatures);
			Logging.WriteDebugLog("Service.Start() ForwardToOriginalWebService: {0}", _bForwardToOriginalWebService);
			Logging.WriteDebugLog("Service.Start() MQTT Logging: {0}", bMQTTLogging);
			Logging.WriteDebugLog("Service.Start() MQTT TLS: {0}", bMQTTTLS);

			MQTT.StartMQTT(configuration["MQTTBroker"] ?? "core-mosquitto", bMQTTTLS, bMQTTLogging, _strServiceName, configuration["MQTTUser"] ?? "", configuration["MQTTPassword"] ?? "", MQTTProcessor);

			AirConditioner.Configure(configuration);

			try
			{
				webHost = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.UseStartup<ASPNETCoreStartup>().UseConfiguration(configuration).UseUrls($"http://*:80/");
				}).Build();
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

			MQTT.StopMQTT();
		}

		private static void MQTTProcessor(string strTopic, string strPayload)
		{
			long lRequestId = 0;
			double dblTemperature = 0;
			string[] strTokens;
			string strUnit, strNewTopic;

			Logging.WriteDebugLog("Service.MQTTProcessor() {0}", strTopic);

			try
			{
				strTokens = strTopic.Split(new char[] { '/' });

				if (strTokens.Length == 5)
				{
					strUnit = strTokens[2];

					strNewTopic = strTopic.Replace(strTokens[2] + "/", "");
				}
				else
					return;
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Service.MQTTProcessor()", eException, "Unable to determine unit.");
				return;
			}

			switch (strNewTopic)
			{
				case "actron/aircon/zone1/set":
					AirConditioner.ChangeZone(strUnit, lRequestId, 1, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone2/set":
					AirConditioner.ChangeZone(strUnit, lRequestId, 2, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone3/set":
					AirConditioner.ChangeZone(strUnit, lRequestId, 3, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone4/set":
					AirConditioner.ChangeZone(strUnit, lRequestId, 4, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone5/set":
					AirConditioner.ChangeZone(strUnit, lRequestId, 5, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone6/set":
					AirConditioner.ChangeZone(strUnit, lRequestId, 6, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone7/set":
					AirConditioner.ChangeZone(strUnit, lRequestId, 7, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/zone8/set":
					AirConditioner.ChangeZone(strUnit, lRequestId, 8, strPayload == "ON" ? true : false);
					break;

				case "actron/aircon/mode/set":
					Logging.WriteDebugLog("ServiceCore.MQTTProcessor() {0}: {1}", strTopic, strPayload);

					switch (strPayload)
					{
						case "off":
							AirConditioner.ChangeMode(strUnit, lRequestId, AirConditionerMode.None);

							break;

						case "auto":
							AirConditioner.ChangeMode(strUnit, lRequestId, AirConditionerMode.Automatic);

							break;

						case "cool":
							AirConditioner.ChangeMode(strUnit, lRequestId, AirConditionerMode.Cooling);

							break;

						case "heat":
							AirConditioner.ChangeMode(strUnit, lRequestId, AirConditionerMode.Heating);

							break;

						case "fan_only":
							AirConditioner.ChangeMode(strUnit, lRequestId, AirConditionerMode.FanOnly);

							break;
					}

					break;

				case "actron/aircon/fan/set":
					Logging.WriteDebugLog("Service.MQTTProcessor() {0}: {1}", strTopic, strPayload);

					switch (strPayload)
					{
						case "low":
							AirConditioner.ChangeFanSpeed(strUnit, lRequestId, FanSpeed.Low);

							break;

						case "medium":
							AirConditioner.ChangeFanSpeed(strUnit, lRequestId, FanSpeed.Medium);

							break;

						case "high":
							AirConditioner.ChangeFanSpeed(strUnit, lRequestId, FanSpeed.High);

							break;
					}

					break;

				case "actron/aircon/temperature/set":
					Logging.WriteDebugLog("Service.MQTTProcessor() {0}: {1}", strTopic, strPayload);

					if (double.TryParse(strPayload, out dblTemperature))
						AirConditioner.ChangeTemperature(strUnit, lRequestId, dblTemperature);

					break;
			}
		}		
	}
}
