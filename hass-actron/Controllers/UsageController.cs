using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;

namespace HMX.HASSActron.Controllers
{
	[Route("usage")]
	public class UsageController : Controller
	{
		[Route("log")]
		public IActionResult Log()
		{
			UsageResponse response = new UsageResponse();
			string[] strElements, strSubElements;
			string strData, strEvent;
			bool bMode = false, bWall = false;

			Logging.WriteDebugLog("UsageController.Log() Client: {0}:{1}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString());

			HttpContext.Response.Headers.Add("Access-Control-Allow-Headers", new Microsoft.Extensions.Primitives.StringValues("X-Requested-With"));
			HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", new Microsoft.Extensions.Primitives.StringValues("*"));

			response.status = 200;
			response.message = "Usage tracked";
			response.value = null;

			try
			{
				strData = new StreamReader(Request.Body).ReadToEnd();
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("UsageController.Log()", eException, "Unable to capture post data.");
				goto Cleanup;
			}

			try
			{
				strElements = strData.Substring(strData.IndexOf("{")).Replace("{", "").Replace("}", "").Trim().Replace("\"", "").Split(new char[] { ',' });

				foreach (string strElement in strElements)
				{
					strSubElements = strElement.Split(new char[] { ':' });

					if (strSubElements.Length == 2)
					{
						if (strSubElements[0] == "mode")
							bMode = (strSubElements[1] == "on" ? true : false);
						else if (strSubElements[0] == "method")
							bWall = (strSubElements[1] == "wall" ? true : false);
					}
				}

				strEvent = string.Format("The air conditioner was turned {0} locally.", bMode ? "on" : "off");
				Logging.WriteDebugLog("UsageController.Log() [0x{0}] Log Entry: {1}", 0.ToString("X8"), strEvent);
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("UsageController.Log()", eException, "Unable to send log data.");
			}

		Cleanup:
			return new ObjectResult(response);
		}
	}
}
