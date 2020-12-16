using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HMX.HASSActron.Controllers
{
	[Route("rest/{version:required}/block/{device:required}")]
	public class DeviceController : Controller
	{
		private static int _iTimeout = 10000;

		[Route("commands")]
		public async Task<IActionResult> Command(string version, string device)
		{
			AirConditionerCommand command;
			CancellationToken cancellationToken = new CancellationToken();
			ActionResult result;
			ContentResult contentResult;
			string strCommandType;

			Logging.WriteDebugLog("DeviceController.Command() Client Start: {0}:{1}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString());

			HttpContext.Response.Headers.Add("Access-Control-Allow-Headers", new Microsoft.Extensions.Primitives.StringValues("Accept, Content-Type, Authorization, Content-Length, X-Requested-With, X-Ninja-Token"));
			HttpContext.Response.Headers.Add("Access-Control-Allow-Methods", new Microsoft.Extensions.Primitives.StringValues("GET,PUT,POST,DELETE,OPTIONS"));
			HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", new Microsoft.Extensions.Primitives.StringValues("*"));

			AirConditioner.UpdateRequestTime();

			if (!await AirConditioner.EventCommand.WaitOneAsync(_iTimeout, cancellationToken))
				return new EmptyResult();

			command = AirConditioner.GetCommand(out strCommandType);

			if (strCommandType != "4" & strCommandType != "5")
				result = new EmptyResult();
			else
			{
				contentResult = new ContentResult();

				contentResult.ContentType = "application/json";
				contentResult.StatusCode = 200;

				if (strCommandType == "4")
				{
					contentResult.Content = string.Format("{{\"DEVICE\":[{{\"G\":\"0\",\"V\":2,\"D\":4,\"DA\":{{\"amOn\":{0},\"tempTarget\":{1},\"fanSpeed\":{2},\"mode\":{3}}}}}]}}",
						command.amOn ? "true" : "false",
						command.tempTarget.ToString("F1"),
						command.fanSpeed.ToString(),
						command.mode.ToString()
					);

					Logging.WriteDebugLog("DeviceController.Command() Command: {0}", contentResult.Content);

				}
				else if (strCommandType == "5")
				{
					contentResult.Content = string.Format("{{\"DEVICE\":[{{\"G\":\"0\",\"V\":2,\"D\":5,\"DA\":{{\"enabledZones\":[{0}]}}}}]}}",
						command.enabledZones
					);

					Logging.WriteDebugLog("DeviceController.Command() Command: {0}", contentResult.Content);
				}

				result = contentResult;
			}

			Logging.WriteDebugLog("DeviceController.Command() Client End: {0}:{1}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString());

			return result;
		}

		[Route("data")]
		public IActionResult Data(string version, string device)
		{
			AirConditionerData data = new AirConditionerData();
			AirConditionerDataHeader header;
			AirConditionerDataHeader6 header6;
			Dictionary<string, object> dDataField;
			DataResponse response = new DataResponse();
			StreamReader reader;
			string strData;
			Newtonsoft.Json.Linq.JArray aZones, aZoneTemperatures;

			Logging.WriteDebugLog("DeviceController.Data() Client: {0}:{1}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString());

			HttpContext.Response.Headers.Add("Access-Control-Allow-Headers", new Microsoft.Extensions.Primitives.StringValues("Accept, Content-Type, Authorization, Content-Length, X-Requested-With, X-Ninja-Token"));
			HttpContext.Response.Headers.Add("Access-Control-Allow-Methods", new Microsoft.Extensions.Primitives.StringValues("GET,PUT,POST,DELETE,OPTIONS"));
			HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", new Microsoft.Extensions.Primitives.StringValues("*"));

			reader = new StreamReader(HttpContext.Request.Body);
			strData = reader.ReadToEnd();
			reader.Dispose();

			try
			{
				header = JsonConvert.DeserializeObject<AirConditionerDataHeader>(strData);
				switch (header.D)
				{
					case 6:
						Logging.WriteDebugLog("DeviceController.Data() Data: {0}", strData);

						header6 = JsonConvert.DeserializeObject<AirConditionerDataHeader6>(strData);

						dDataField = header6.DA;

						data.iCompressorActivity = int.Parse(dDataField["compressorActivity"].ToString());
						data.strErrorCode = dDataField["errorCode"].ToString();
						data.iFanContinuous = int.Parse(dDataField["fanIsCont"].ToString());
						data.iFanSpeed = int.Parse(dDataField["fanSpeed"].ToString());
						data.bOn = bool.Parse(dDataField["isOn"].ToString());
						data.bESPOn = bool.Parse(dDataField["isInESP_Mode"].ToString());
						data.iMode = int.Parse(dDataField["mode"].ToString());
						data.dblRoomTemperature = double.Parse(dDataField["roomTemp_oC"].ToString());
						data.dblSetTemperature = double.Parse(dDataField["setPoint"].ToString());

						aZones = (Newtonsoft.Json.Linq.JArray)dDataField["enabledZones"];
						data.bZone1 = (aZones[0].ToString() == "1");
						data.bZone2 = (aZones[1].ToString() == "1");
						data.bZone3 = (aZones[2].ToString() == "1");
						data.bZone4 = (aZones[3].ToString() == "1");
						data.bZone5 = (aZones[4].ToString() == "1");
						data.bZone6 = (aZones[5].ToString() == "1");
						data.bZone7 = (aZones[6].ToString() == "1");
						data.bZone8 = (aZones[7].ToString() == "1");

						aZoneTemperatures = (Newtonsoft.Json.Linq.JArray)dDataField["individualZoneTemperatures_oC"];
						if (aZones[0].ToString() != "null") double.TryParse(aZoneTemperatures[0].ToString(), out data.dblZone1Temperature);
						if (aZones[1].ToString() != "null") double.TryParse(aZoneTemperatures[1].ToString(), out data.dblZone2Temperature);
						if (aZones[2].ToString() != "null") double.TryParse(aZoneTemperatures[2].ToString(), out data.dblZone3Temperature);
						if (aZones[3].ToString() != "null") double.TryParse(aZoneTemperatures[3].ToString(), out data.dblZone4Temperature);
						if (aZones[4].ToString() != "null") double.TryParse(aZoneTemperatures[4].ToString(), out data.dblZone5Temperature);
						if (aZones[5].ToString() != "null") double.TryParse(aZoneTemperatures[5].ToString(), out data.dblZone6Temperature);
						if (aZones[6].ToString() != "null") double.TryParse(aZoneTemperatures[6].ToString(), out data.dblZone7Temperature);
						if (aZones[7].ToString() != "null") double.TryParse(aZoneTemperatures[7].ToString(), out data.dblZone8Temperature);

						AirConditioner.PostData(data);

						break;
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("DeviceController.Data()", eException, "Unable to parse air conditioner data.");
			}

			response.result = 1;
			response.error = null;
			response.id = 0;

			if (Service.ForwardToOriginalWebService)
				ForwardDataToOriginalWebService(strData);

			return new ObjectResult(response);
		}

		[Route("activate")]
		public async Task<IActionResult> Activate(string version, string device, string user_access_token)
		{
			ContentResult contentResult;
			string strUserAgent, strHost, strPage;
			ProxyResponse response;

			Logging.WriteDebugLog("DeviceController.Activate() Client: {0}:{1}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString());

			strUserAgent = HttpContext.Request.Headers["User-Agent"];
			strHost = Request.Host.Host;
			strPage = string.Format("/rest/{0}/block/{1}/activate?user_access_token={2}", version, device, user_access_token);

			Logging.WriteDebugLog("DeviceController.Activate() Client: {0}:{1} GET http://{2}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString(), strHost + strPage);

			response = await Proxy.ForwardRequestToOriginalWebService("GET", strUserAgent, strHost, strPage);
			if (response.ProxySuccessful)
			{
				contentResult = new ContentResult();
				contentResult.ContentType = "application/json";
				contentResult.StatusCode = (int)response.ResponseCode;
				contentResult.Content = response.Response;
			}
			else
			{
				contentResult = new ContentResult();
				contentResult.StatusCode = 500;
			}

			return contentResult;
		}

		[Route("activate")]
		[HttpDelete]
		public async Task<IActionResult> ActivateDelete(string version, string device, string user_access_token)
		{
			ContentResult contentResult;
			string strUserAgent, strHost, strPage;
			ProxyResponse response;

			Logging.WriteDebugLog("DeviceController.ActivateDelete() Client: {0}:{1}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString());

			strUserAgent = HttpContext.Request.Headers["User-Agent"];
			strHost = Request.Host.Host;
			strPage = string.Format("/rest/{0}/block/{1}/activate?user_access_token={2}", version, device, user_access_token);

			Logging.WriteDebugLog("DeviceController.ActivateDelete() Client: {0}:{1} DELETE http://{2}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString(), strHost + strPage);

			response = await Proxy.ForwardRequestToOriginalWebService("DELETE", strUserAgent, strHost, strPage);
			if (response.ProxySuccessful)
			{
				contentResult = new ContentResult();
				contentResult.ContentType = "application/json";
				contentResult.StatusCode = (int)response.ResponseCode;
				contentResult.Content = response.Response;
			}
			else
			{
				contentResult = new ContentResult();
				contentResult.StatusCode = 500;
			}

			return contentResult;
		}

		private void ForwardDataToOriginalWebService(string strData)
		{
			string strUserAgent = "", strCnntentType = "", strNinjaToken = "";

			Logging.WriteDebugLog("DeviceController.ForwardDataToOriginalWebService()");

			foreach (string strHeader in HttpContext.Request.Headers.Keys)
			{
				try
				{
					switch (strHeader)
					{
						case "User-Agent":
							strUserAgent = HttpContext.Request.Headers[strHeader].ToString();
							break;

						case "Content-Type":
							strCnntentType = HttpContext.Request.Headers[strHeader].ToString();
							break;

						case "X-Ninja-Token":
							strNinjaToken = HttpContext.Request.Headers[strHeader].ToString();
							break;
					}
				}
				catch (Exception eException)
				{
					Logging.WriteDebugLogError("DeviceController.ForwardDataToOriginalWebService()", eException, "Unable to add request header ({0}).", strHeader);
				}
			}

			Proxy.ForwardDataToOriginalWebService(strUserAgent, strCnntentType, strNinjaToken, HttpContext.Request.Host.ToString(), HttpContext.Request.Path, strData);
		}
	}
}
