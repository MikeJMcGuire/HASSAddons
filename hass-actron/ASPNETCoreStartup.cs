using HMX.HASSActron;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace HMX.HASSActron
{
	public class ASPNETCoreStartup
	{
		public void Configure(IApplicationBuilder applicationBuilder)
		{
			Logging.WriteDebugLog("ASPNETCoreStartup.Configure()");

			try
			{
				applicationBuilder.UseResponseBuffering();
				applicationBuilder.UseMvc();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("ASPNETCoreStartup.Configure()", eException, "Unable to configure application.");
			}
		}

		public void ConfigureServices(IServiceCollection services)
		{
			Logging.WriteDebugLog("ASPNETCoreStartup.ConfigureServices()");

			try
			{
				services.AddMvc();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("ASPNETCoreStartup.ConfigureServices()", eException, "Unable to configure services.");
			}
		}
	}
}
