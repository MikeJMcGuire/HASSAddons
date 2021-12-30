using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using Microsoft.Extensions.Hosting;

namespace HMX.HASSBlueriiot
{
	public class ASPNETCoreStartup
	{
		public void Configure(IApplicationBuilder applicationBuilder, IWebHostEnvironment environment, IHostApplicationLifetime applicationLifetime)
		{
			Logging.WriteLog("ASPNETCoreStartup.Configure()");

			applicationLifetime.ApplicationStarted.Register(OnStarted);
			applicationLifetime.ApplicationStopping.Register(OnStopping);
			applicationLifetime.ApplicationStopped.Register(OnStopped);

			try
			{
				applicationBuilder.UseRouting();
				applicationBuilder.UseEndpoints(endpoints => {
					endpoints.MapControllers();
				});
			}
			catch (Exception eException)
			{
				Logging.WriteLogError("ASPNETCoreStartup.Configure()", eException, "Unable to configure application.");
			}
		}

		public void ConfigureServices(IServiceCollection services)
		{
			Logging.WriteLog("ASPNETCoreStartup.ConfigureServices()");

			try
			{
				services.AddControllers();
				services.AddHttpContextAccessor();
				services.TryAddSingleton<IActionContextAccessor, ActionContextAccessor>();
			}
			catch (Exception eException)
			{
				Logging.WriteLogError("ASPNETCoreStartup.ConfigureServices()", eException, "Unable to configure services.");
			}
		}

		private void OnStarted()
		{
			Logging.WriteLog("ASPNETCoreStartup.OnStarted()");
		}

		private void OnStopping()
		{
			Logging.WriteLog("ASPNETCoreStartup.OnStopping()");

			ServiceCore.Stop();
		}

		private void OnStopped()
		{
			Logging.WriteLog("ASPNETCoreStartup.OnStopped()");
		}
	}
}
