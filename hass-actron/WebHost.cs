using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Net;

namespace HMX.HASSActron
{
	public class WebHost<StartupClass> where StartupClass : class
	{
		private IWebHost _webHost = null;

		public bool Start(IConfigurationRoot configuration)
		{
			Logging.WriteDebugLog("WebHost.Start()");
			
			try
			{
				_webHost = new WebHostBuilder().UseKestrel().UseStartup<StartupClass>().UseConfiguration(configuration).UseUrls($"http://*:80/").Build();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("WebHost.Start()", eException, "Unable to build Kestrel instance.");
				return false;
			}


			/*_webHost.Services.GetRequiredService<IApplicationLifetime>().ApplicationStopped.Register(() =>
			{
				if (!_bStoppedByService)
					_eventStop.Set();
			});*/

			try
			{
				_webHost.Start();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("WebHost.Start()", eException, "Unable to start Kestrel.");
				return false;
			}

			return true;
		}

		public void Stop()
		{
			Logging.WriteDebugLog("WebHost.Stop()");

			_webHost?.Dispose();
		}
	}
}
