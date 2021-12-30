using System;
using System.Collections.Generic;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HMX.HASSBlueriiot
{
	public class HomeAssistant
	{
		public delegate void HAEventHandler(dynamic haEvent);

		private static int _iRepeatDelay = 10000; // Milliseconds
		private static int _iCancellationTime = 10000; // Milliseconds
		private static int _iInactivityTime = 300000; // Milliseconds
		private static HttpClient _httpClient = null;
		private static string _strAPIKey = "";
		private static string _strHAServer = "";

		public enum Method
		{
			GET,
			POST
		}

		static HomeAssistant()
		{
			HttpClientHandler httpClientHandler = null;

			Logging.WriteLog("HomeAssistant.HomeAssistant()");

			httpClientHandler = new HttpClientHandler();
			httpClientHandler.Proxy = null;
			httpClientHandler.UseProxy = false;

			_httpClient = new HttpClient(httpClientHandler);

			_httpClient.DefaultRequestHeaders.Connection.Add("close");
		}

		public static void Initialise(string strHAServer, string strAPIKey)
		{
			Logging.WriteLog("HomeAssistant.Initialise()");

			_strAPIKey = strAPIKey;
			if (strHAServer != "")
				_strHAServer = strHAServer;
			else
				_strHAServer = "http://supervisor/core";

			_httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + _strAPIKey);
		}

		/*public async static Task<bool> SetObjectState(long lRequestId, string strObject, string strState)
		{
			JObject jRequest = new JObject();
			string strPageURI;
			GenericResponse response;

			Logging.WriteLog("HomeAssistant.SetObjectState() [0x{0}]", lRequestId.ToString("X8"));

			jRequest.Add("entity_id", strObject);
			jRequest.Add("value", strState);

			strPageURI = string.Format("/api/services/{0}/set_value", strObject.Substring(0, strObject.IndexOf(".")));

			response = await SendAPIRequest(lRequestId, strPageURI, jRequest.ToString());

			return response.Successful;
		}*/

		public async static Task<bool> SetObjectState(long lRequestId, string strObject, string strState, string strFriendlyName, string? strIcon, string? strUnitOfMeasurement)
		{
			JObject jRequest = new JObject();
			JObject jAttributes = new JObject();
			string strPageURI;
			GenericResponse response;

			Logging.WriteLog("HomeAssistant.CreateObject() [0x{0}]", lRequestId.ToString("X8"));

			jAttributes.Add("friendly_name", strFriendlyName);
			if (strUnitOfMeasurement != null)
				jAttributes.Add("unit_of_measurement", strUnitOfMeasurement);
			if (strIcon != null)
				jAttributes.Add("icon", strIcon);

			jRequest.Add("state", strState);
			jRequest.Add("attributes", jAttributes);

			strPageURI = string.Format("/api/states/{0}", strObject);

			response = await SendAPIRequest(lRequestId, strPageURI, jRequest.ToString());

			return response.Successful;
		}


		public async static Task<GenericResponse> SendAPIRequest(long lRequestId, string strPageURI)
		{
			return await SendAPIRequest(lRequestId, Method.GET, strPageURI, null);
		}

		public async static Task<GenericResponse> SendAPIRequest(long lRequestId, string strPageURI, string strContent)
		{
			return await SendAPIRequest(lRequestId, Method.POST, strPageURI, strContent);
		}

		private async static Task<GenericResponse> SendAPIRequest(long lRequestId, Method method, string strPageURI, string strContent)
		{
			GenericResponse response = new GenericResponse(true, "");
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;

			Logging.WriteLog("HomeAssistant.SendAPIRequest() [0x{0}] Base: {1}{2}", lRequestId.ToString("X8"), _strHAServer, strPageURI);

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(_iCancellationTime);

				switch (method)
				{
					case Method.GET:
						httpResponse = await _httpClient.GetAsync(_strHAServer + strPageURI, cancellationToken.Token);
						break;

					case Method.POST:
						httpResponse = await _httpClient.PostAsync(_strHAServer + strPageURI, new StringContent(strContent), cancellationToken.Token);
						break;

					default:
						response = new GenericResponse(false, "Invalid Method Specified.");
						return response;
				}


				if (httpResponse.IsSuccessStatusCode)
				{
					response = new GenericResponse(true, await httpResponse.Content.ReadAsStringAsync());
				}
				else
				{
					response = new GenericResponse(false, string.Format("Unable to process API HTTP response. Response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase));
					Logging.WriteLogError("HomeAssistant.SendAPIRequest()", lRequestId, response.Reason);
					goto Cleanup;
				}
			}
			catch (Exception eException)
			{
				if (eException.InnerException != null)
					response = new GenericResponse(false, string.Format("Unable to process API HTTP response. Error: {0}. {1}", eException.Message, eException.InnerException.Message));
				else
					response = new GenericResponse(false, string.Format("Unable to process API HTTP response. Error: {0}", eException.Message));

				Logging.WriteLogError("HomeAssistant.SendAPIRequest()", lRequestId, eException, "Unable to process API HTTP response.");
				goto Cleanup;
			}

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();

			return response;
		}
	}
}
