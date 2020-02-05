using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace HMX.HASSActronQue
{
	public class ASPNETCoreStartup
	{
		public void Configure(IApplicationBuilder applicationBuilder, IApplicationLifetime applicationLifetime)
		{
			Logging.WriteDebugLog("ASPNETCoreStartup.Configure()");

			applicationLifetime.ApplicationStarted.Register(OnStarted);
			applicationLifetime.ApplicationStopping.Register(OnStopping);
			applicationLifetime.ApplicationStopped.Register(OnStopped);

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

		private void OnStarted()
		{
			Logging.WriteDebugLog("ASPNETCoreStartup.OnStarted()");
		}

		private void OnStopping()
		{
			Logging.WriteDebugLog("ASPNETCoreStartup.OnStopping()");

			Service.Stop();
		}

		private void OnStopped()
		{
			Logging.WriteDebugLog("ASPNETCoreStartup.OnStopped()");
		}
	}
}
