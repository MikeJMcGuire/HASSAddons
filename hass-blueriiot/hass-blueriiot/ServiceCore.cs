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
        public static void Start()
        {
			IHost webHost;
			string strHAServer, strHAKey, strUser, strPassword;

			Logging.WriteLog("ServiceCore.Start() Built: {0}", Properties.Resources.BuildDate);

			if (!Configuration.GetOptionalConfiguration("HAServer", out strHAServer))
				return;

			if (!Configuration.GetConfiguration("BlueRiiotUser", out strUser))
				return;

			if (!Configuration.GetPrivateConfiguration("BlueRiiotPassword", out strPassword))
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
