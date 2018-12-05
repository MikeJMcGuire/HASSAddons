﻿using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActron.Controllers
{
	public class Instrumentation : Controller
	{
		[Route("/")]
		public IActionResult Test()
		{
			ContentResult result = new ContentResult();

			Logging.WriteDebugLog("Instrumentation.Test() Client: {0}:{1}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString());

			result.ContentType = "text/html";
			result.StatusCode = 200;

			result.Content = "OK";

			return result;
		}
	}
}