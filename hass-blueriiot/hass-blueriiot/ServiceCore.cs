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
		private static string _strConfigFile = "/data/options.json";

		public static void Start()
        {
			IHost webHost;
			IConfigurationRoot configuration;
			string strHAServer, strHAKey, strUser, strPassword;

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

			if (!Configuration.GetOptionalConfiguration("HAServer", out strHAServer))
				return;

			if (!Configuration.GetConfiguration(configuration, "BlueriiotUser", out strUser))
				return;

			if (!Configuration.GetPrivateConfiguration(configuration, "BlueriiotPassword", out strPassword))
				return;

			if (!Configuration.GetPrivateConfiguration("SUPERVISOR_TOKEN", out strHAKey))
				return;

			HomeAssistant.Initialise(strHAServer, strHAKey); 
			BlueRiiot.Start(strUser, strPassword);			
					
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

		public static void Stop()
        {
            Logging.WriteLog("ServiceCore.Stop()");		

			Logging.WriteLog("ServiceCore.Stop() Stopped");
		}
	}
}
