using HMX.HASSActron;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using Microsoft.Extensions.Hosting;

namespace HMX.HASSActron
{
	public class ASPNETCoreStartup
	{
		public void Configure(IApplicationBuilder applicationBuilder, IWebHostEnvironment environment, IHostApplicationLifetime applicationLifetime)
		{
			Logging.WriteDebugLog("ASPNETCoreStartup.Configure()");

			applicationLifetime.ApplicationStarted.Register(OnStarted);
			applicationLifetime.ApplicationStopping.Register(OnStopping);
			applicationLifetime.ApplicationStopped.Register(OnStopped);

			try
			{
				applicationBuilder.UseResponseBuffering();
				applicationBuilder.UseRouting();
				applicationBuilder.UseEndpoints(endpoints => {
					endpoints.MapControllers();
				});
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
				services.AddControllers().AddNewtonsoftJson();
				services.AddHttpContextAccessor();
				services.TryAddSingleton<IActionContextAccessor, ActionContextAccessor>();
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
