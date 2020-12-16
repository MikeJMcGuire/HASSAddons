using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActron.Controllers
{
	public class InstrumentationController : Controller
	{
		[Route("/")]
		public IActionResult Status()
		{
			Logging.WriteDebugLog("Instrumentation.Status() Client: {0}:{1}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString());

			return new ObjectResult("OK");
		}

		[Route("/status")]
		public IActionResult DetailedStatus()
		{
			ContentResult result = new ContentResult();

			Logging.WriteDebugLog("Instrumentation.Status() Client: {0}:{1}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString());

			result.ContentType = "text/html";
			result.StatusCode = 200;

			if (AirConditioner.DataReceived)
			{
				result.Content = "Last Post from Air Conditioner: " + AirConditioner.LastUpdate + "<br/>";
				result.Content += "Last Request from Air Conditioner: " + AirConditioner.LastRequest;
			}
			else
				result.Content = "Last Update from Air Conditioner: Never";

			return result;
		}
	}
}
