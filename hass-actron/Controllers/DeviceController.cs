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
		[Route("commands")]
		public async Task<IActionResult> Command(string version, string device)
		{
			AirConditionerCommand command;
			ContentResult result;
			string strCommandType;

			Logging.WriteDebugLog("DeviceController.Command() Client: {0}:{1}", HttpContext.Connection.RemoteIpAddress.ToString(), HttpContext.Connection.RemotePort.ToString());

			HttpContext.Response.Headers.Add("Access-Control-Allow-Headers", new Microsoft.Extensions.Primitives.StringValues("Accept, Content-Type, Authorization, Content-Length, X-Requested-With, X-Ninja-Token"));
			HttpContext.Response.Headers.Add("Access-Control-Allow-Methods", new Microsoft.Extensions.Primitives.StringValues("GET,PUT,POST,DELETE,OPTIONS"));
			HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", new Microsoft.Extensions.Primitives.StringValues("*"));

			command = AirConditioner.GetCommand(out strCommandType);

			if (strCommandType != "4" & strCommandType != "5")
			{
				if (Service.ForwardToInternalWebService == "")
					return new EmptyResult();
				else
					return await ForwardCommandToOriginalWebService();
			}
			else
			{
				result = new ContentResult();

				result.ContentType = "application/json";
				result.StatusCode = 200;

				if (strCommandType == "4")
				{
					result.Content = string.Format("{{\"DEVICE\":[{{\"G\":\"0\",\"V\":2,\"D\":4,\"DA\":{{\"amOn\":{0},\"tempTarget\":{1},\"fanSpeed\":{2},\"mode\":{3}}}}}]}}",
						command.amOn ? "true" : "false",
						command.tempTarget.ToString("F1"),
						command.fanSpeed.ToString(),
						command.mode.ToString()
					);

					Logging.WriteDebugLog("DeviceController.Command() Command: {0}", result.Content);

				}
				else if (strCommandType == "5")
				{
					result.Content = string.Format("{{\"DEVICE\":[{{\"G\":\"0\",\"V\":2,\"D\":5,\"DA\":{{\"enabledZones\":[{0}]}}}}]}}",
						command.enabledZones
					);

					Logging.WriteDebugLog("DeviceController.Command() Command: {0}", result.Content);
				}

				return result;
			}
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

						aZones = (Newtonsoft.Json.Linq.JArray) dDataField["enabledZones"];
						data.bZone1 = (aZones[0].ToString() == "1");
						data.bZone2 = (aZones[1].ToString() == "1");
						data.bZone3 = (aZones[2].ToString() == "1");
						data.bZone4 = (aZones[3].ToString() == "1");
						data.bZone5 = (aZones[4].ToString() == "1");
						data.bZone6 = (aZones[5].ToString() == "1");
						data.bZone7 = (aZones[6].ToString() == "1");
						data.bZone8 = (aZones[7].ToString() == "1");

						aZoneTemperatures = (Newtonsoft.Json.Linq.JArray)dDataField["individualZoneTemperatures_oC"];
						if (aZones[0].ToString() != "null") double.TryParse(aZones[0].ToString(), out data.dblZone1Temperature);
						if (aZones[1].ToString() != "null") double.TryParse(aZones[1].ToString(), out data.dblZone2Temperature);
						if (aZones[2].ToString() != "null") double.TryParse(aZones[2].ToString(), out data.dblZone3Temperature);
						if (aZones[3].ToString() != "null") double.TryParse(aZones[3].ToString(), out data.dblZone4Temperature);
						if (aZones[4].ToString() != "null") double.TryParse(aZones[4].ToString(), out data.dblZone5Temperature);
						if (aZones[5].ToString() != "null") double.TryParse(aZones[5].ToString(), out data.dblZone6Temperature);
						if (aZones[6].ToString() != "null") double.TryParse(aZones[6].ToString(), out data.dblZone7Temperature);
						if (aZones[7].ToString() != "null") double.TryParse(aZones[7].ToString(), out data.dblZone8Temperature);							

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

			if (Service.ForwardToInternalWebService != "")
				ForwardDataToOriginalWebService(strData);

			return new ObjectResult(response);
		}

		private async void ForwardDataToOriginalWebService(string strData)
		{
			HttpClient httpClient = null;
			HttpClientHandler httpClientHandler;
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			StringContent stringContent;
			string strContent;
			string strURL = "http://" + Service.ForwardToInternalWebService + HttpContext.Request.Path;

			Logging.WriteDebugLog("DeviceController.ForwardDataToOriginalWebService() URL: " + strURL);

			httpClientHandler = new HttpClientHandler();
			httpClientHandler.Proxy = null;
			httpClientHandler.UseProxy = false;

			httpClient = new HttpClient(httpClientHandler);

			httpClient.DefaultRequestHeaders.Connection.Add("close");

			stringContent = new StringContent(strData);

			foreach (string strHeader in HttpContext.Request.Headers.Keys)
			{
				try
				{
					switch (strHeader)
					{
						case "User-Agent":
							httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(HttpContext.Request.Headers[strHeader].ToString());
							break;

						case "Content-Type":
							stringContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(HttpContext.Request.Headers[strHeader].ToString());
							break;

						case "X-Ninja-Token":
							httpClient.DefaultRequestHeaders.Add(strHeader, HttpContext.Request.Headers[strHeader].ToString());
							break;
					}
				}
				catch (Exception eException)
				{
					Logging.WriteDebugLogError("DeviceController.ForwardDataToOriginalWebService()", eException, "Unable to add request header ({0}).", strHeader);
				}
			}

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(5000);

				httpResponse = await httpClient.PostAsync(strURL, stringContent, cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					strContent = await httpResponse.Content.ReadAsStringAsync();
					Logging.WriteDebugLog("Response: " + strContent);
				}
				else
				{
					Logging.WriteDebugLog("Response: " + httpResponse.StatusCode);
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("DeviceController.ForwardDataToOriginalWebService()", eException, "Unable to process API HTTP response.");
			}

			cancellationToken?.Dispose();
			httpResponse?.Dispose();
			httpClient?.Dispose();
		}

		private async Task<IActionResult> ForwardCommandToOriginalWebService()
		{
			IActionResult result;
			ContentResult contentResult;
			HttpClient httpClient = null;
			HttpClientHandler httpClientHandler;
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			string strURL = "http://" + Service.ForwardToInternalWebService + HttpContext.Request.Path;
			string strContent;

			Logging.WriteDebugLog("DeviceController.ForwardCommandToOriginalWebService() URL: " + strURL);

			httpClientHandler = new HttpClientHandler();
			httpClientHandler.Proxy = null;
			httpClientHandler.UseProxy = false;

			httpClient = new HttpClient(httpClientHandler);

			httpClient.DefaultRequestHeaders.Connection.Add("close");

			result = new EmptyResult();

			foreach (string strHeader in HttpContext.Request.Headers.Keys)
			{
				try
				{
					switch (strHeader)
					{
						case "User-Agent":
							httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(HttpContext.Request.Headers[strHeader].ToString());
							break;

						case "X-Ninja-Token":
							httpClient.DefaultRequestHeaders.Add(strHeader, HttpContext.Request.Headers[strHeader].ToString());
							break;
					}
				}
				catch (Exception eException)
				{
					Logging.WriteDebugLogError("DeviceController.ForwardCommandToOriginalWebService()", eException, "Unable to add request header ({0}).", strHeader);
				}
			}

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(5000);

				httpResponse = await httpClient.GetAsync(strURL, cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					strContent = await httpResponse.Content.ReadAsStringAsync();

					if (strContent.Length > 0)
					{
						Logging.WriteDebugLog("Response: " + strContent);

						contentResult = new ContentResult();
						contentResult.ContentType = httpResponse.Content.Headers.ContentType.MediaType;
						contentResult.Content = strContent;

						result = contentResult;
					}
				}
				else
				{
					Logging.WriteDebugLog("Response: " + httpResponse.StatusCode);
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("DeviceController.ForwardCommandToOriginalWebService()", eException, "Unable to process API HTTP response.");
			}

			cancellationToken?.Dispose();
			httpResponse?.Dispose();
			httpClient?.Dispose();

			return result;
		}
	}
}
