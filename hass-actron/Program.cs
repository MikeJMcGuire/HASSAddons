using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace HMX.HASSActron
{
    class Program
    {
		static void Main(string[] args)
        {
			IConfigurationRoot configuration;
			IWebHost webHost;

			try
			{
				configuration = new ConfigurationBuilder().AddJsonFile("config.json", false, true).Build();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Program.Main()", eException, "Unable to build configuration instance.");
				return;
			}

			try
			{
				webHost = new WebHostBuilder().UseKestrel().UseStartup<ASPNETCoreStartup>().UseConfiguration(configuration).UseUrls($"http://*:80/").Build();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Program.Main()", eException, "Unable to build Kestrel instance.");
				return;
			}

			webHost.Run();
		}
    }
}
