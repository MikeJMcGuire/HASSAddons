using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;

namespace HMX.HASSBlueriiot
{
    public class ServiceCore
    {
		private static string _strServiceName = "hass-blueriiot";
		private static string _strServiceDescription = "Blueriiot Pool Sensor";
		private static string _strConfigFile = "/data/options.json";

		public static void Start()
        {
			IHost webHost;
			IConfigurationRoot configuration;
			string strMQTTUser, strMQTTPassword, strMQTTBroker;
			string strUser, strPassword;
			bool bMQTTTLS;

			Logging.WriteLog("ServiceCore.Start() Built: {0}", Properties.Resources.BuildDate);

			// Load Configuration
			try
			{
				configuration = new ConfigurationBuilder().AddJsonFile(_strConfigFile, false, true).Build();
			}
			catch (Exception eException)
			{
				Logging.WriteLogError("Service.Start()", eException, "Unable to build configuration instance.");
				return;
			}

			if (!Configuration.GetConfiguration(configuration, "BlueriiotUser", out strUser))
				return;

			if (!Configuration.GetPrivateConfiguration(configuration, "BlueriiotPassword", out strPassword))
				return;

			Configuration.GetOptionalConfiguration(configuration, "MQTTUser", out strMQTTUser);
			Configuration.GetPrivateOptionalConfiguration(configuration, "MQTTPassword", out strMQTTPassword);
			if (!Configuration.GetConfiguration(configuration, "MQTTBroker", out strMQTTBroker))
				return;
			Configuration.GetOptionalConfiguration(configuration, "MQTTTLS", out bMQTTTLS);

			MQTT.StartMQTT(strMQTTBroker, bMQTTTLS, _strServiceName, strMQTTUser, strMQTTPassword);
			BlueRiiot.Start(strUser, strPassword);

			MQTTRegister();

			try
			{
				webHost = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.UseStartup<ASPNETCoreStartup>();
				}).Build();
			}
			catch (Exception eException)
			{
				Logging.WriteLogError("ServiceCore.Start()", eException, "Unable to build Kestrel instance.");
				return;
			}

			webHost.Run();

			Logging.WriteLog("ServiceCore.Start() Started");
		}

		public static void MQTTRegister()
		{
			Logging.WriteLog("ServiceCore.MQTTRegister()");

			MQTT.SendMessage("homeassistant/sensor/blueriiot/sensor_pool_temperature_c/config",
				"{{\"name\":\"Pool Temperature C\",\"unique_id\":\"{1}-0c\",\"device\":{{\"identifiers\":[\"{1}\"],\"name\":\"{2}\",\"model\":\"Container\",\"manufacturer\":\"Blueriiot\"}},\"state_topic\":\"sarah/sensor_pool/temperature_c\",\"device_class\":\"temperature\",\"unit_of_measurement\":\"°C\",\"availability_topic\":\"{0}/status\"}}", _strServiceName.ToLower(), _strServiceName, _strServiceDescription);

			MQTT.SendMessage("homeassistant/sensor/blueriiot/sensor_pool_temperature_f/config",
				"{{\"name\":\"Pool Temperature F\",\"unique_id\":\"{1}-0f\",\"device\":{{\"identifiers\":[\"{1}\"],\"name\":\"{2}\",\"model\":\"Container\",\"manufacturer\":\"Blueriiot\"}},\"state_topic\":\"sarah/sensor_pool/temperature_f\",\"device_class\":\"temperature\",\"unit_of_measurement\":\"°F\",\"availability_topic\":\"{0}/status\"}}", _strServiceName.ToLower(), _strServiceName, _strServiceDescription);

			MQTT.SendMessage("homeassistant/sensor/blueriiot/sensor_pool_ph/config",
				"{{\"name\":\"Pool pH\",\"unique_id\":\"{1}-1\",\"device\":{{\"identifiers\":[\"{1}\"],\"name\":\"{2}\",\"model\":\"Container\",\"manufacturer\":\"Blueriiot\"}},\"state_topic\":\"sarah/sensor_pool/ph\",\"availability_topic\":\"{0}/status\"}}", _strServiceName.ToLower(), _strServiceName, _strServiceDescription);

			MQTT.SendMessage("homeassistant/sensor/blueriiot/sensor_pool_orp/config",
				 "{{\"name\":\"Pool Orp\",\"unique_id\":\"{1}-2\",\"device\":{{\"identifiers\":[\"{1}\"],\"name\":\"{2}\",\"model\":\"Container\",\"manufacturer\":\"Blueriiot\"}},\"state_topic\":\"sarah/sensor_pool/orp\",\"unit_of_measurement\":\"mV\",\"availability_topic\":\"{0}/status\"}}", _strServiceName.ToLower(), _strServiceName, _strServiceDescription);

			MQTT.SendMessage("homeassistant/sensor/blueriiot/sensor_pool_salinity/config",
				"{{\"name\":\"Pool Salinity\",\"unique_id\":\"{1}-3\",\"device\":{{\"identifiers\":[\"{1}\"],\"name\":\"{2}\",\"model\":\"Container\",\"manufacturer\":\"Blueriiot\"}},\"state_topic\":\"sarah/sensor_pool/salinity\",\"unit_of_measurement\":\"ppm\",\"availability_topic\":\"{0}/status\"}}", _strServiceName.ToLower(), _strServiceName, _strServiceDescription);
		}

		public static void Stop()
        {
            Logging.WriteLog("ServiceCore.Stop()");

			MQTT.StopMQTT();

			Logging.WriteLog("ServiceCore.Stop() Stopped");
		}
	}
}
