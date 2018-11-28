using Microsoft.AspNetCore.Mvc;
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

			result.ContentType = "text/html";
			result.StatusCode = 200;

			result.Content = "OK";

			return result;
		}
	}
}
