using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;

namespace HMX.HASSActron.Controllers
{
	[Route("v0")]
	public class UpdateController : Controller
	{
		[Route("AConnect")]
		public IActionResult Log(string serial, string mac, string reboots, string uptime_mins, string bootloader, string wifi, string flash_fam, string version, string msg)
		{
			ContentResult content = new ContentResult();
			string strResponse;
			
			Logging.WriteDebugLog("UpdateController.Log() Client: {0}:{1} Message: {2}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString(), msg);

			HttpContext.Response.Headers.Append("Access-Control-Allow-Headers", new Microsoft.Extensions.Primitives.StringValues("X-Requested-With"));
			HttpContext.Response.Headers.Append("Access-Control-Allow-Origin", new Microsoft.Extensions.Primitives.StringValues("*"));

			strResponse = string.Format("download=\nMessageLogged={0}:{1}", msg, version);

			content.Content = strResponse;

			return content;
		}
	}
}
