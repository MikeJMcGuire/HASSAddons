using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HMX.HASSActronQue
{
	public class Que
	{
		private static string _strQueBaseURL = "https://que.actronair.com.au";
		private static string _strBaseUserAgent = "nxgen-ios/1214 CFNetwork/976 Darwin/18.2.0";
		private static string _strDeviceName = "TestClient";
		private static string _strAirConditionerName = "Que Air Conditioner";
		private static string _strDeviceUniqueIdentifier = "1111";
		private static string _strQueUser, _strQuePassword, _strSerialNumber;
		private static HttpClient _httpClient = null, _httpClientAuth = null;
		private static int _iCancellationTime = 10; // Seconds
		private static int _iStateRetrievalInterval = 30; // Seconds
		private static int _iAuthenticationInterval = 60; // Seconds
		private static ManualResetEvent _eventStop;
		private static ManualResetEvent _eventAuthenticationFailure = new ManualResetEvent(false);
		private static PairingToken _pairingToken;
		private static QueToken _queToken = null;
		private static AirConditionerData _airConditionerData = null;
		private static object _oLockData = new object();
		private static int _iZoneCount;

		public static DateTime LastUpdate
		{
			get { return _airConditionerData.LastUpdated; }
		}

		static Que()
		{
			HttpClientHandler httpClientHandler = null;

			Logging.WriteDebugLog("Que.Que()");

			httpClientHandler = new HttpClientHandler();
			httpClientHandler.Proxy = null;
			httpClientHandler.UseProxy = false;

			_httpClientAuth = new HttpClient(httpClientHandler);

			_httpClientAuth.DefaultRequestHeaders.UserAgent.ParseAdd(_strBaseUserAgent);

			httpClientHandler = new HttpClientHandler();
			httpClientHandler.Proxy = null;
			httpClientHandler.UseProxy = false;

			_httpClient = new HttpClient(httpClientHandler);

			_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_strBaseUserAgent);
		}

		public static void Initialise(string strQueUser, string strQuePassword, string strSerialNumber, int iZoneCount, ManualResetEvent eventStop)
		{
			Thread threadMonitor;

			Logging.WriteDebugLog("Que.Initialise()");

			_strQueUser = strQueUser;
			_strQuePassword = strQuePassword;
			_strSerialNumber = strSerialNumber;
			_iZoneCount = iZoneCount;
			_eventStop = eventStop;

			_airConditionerData = new AirConditionerData();
			_airConditionerData.Zones = new Dictionary<int, AirConditionerZone>();
			for (int iIndex = 1; iIndex <= iZoneCount; iIndex++)
				_airConditionerData.Zones.Add(iIndex, new AirConditionerZone());

			threadMonitor = new Thread(new ThreadStart(TokenMonitor));
			threadMonitor.Start();

			threadMonitor = new Thread(new ThreadStart(AirConditionerMonitor));
			threadMonitor.Start();
		}

		public static async Task<bool> GeneratePairingToken()
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			Dictionary<string, string> dtFormContent = new Dictionary<string, string>();
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL = "/api/v0/client/user-devices";
			string strResponse;
			dynamic jsonResponse;
			bool bRetVal = true;

			Logging.WriteDebugLog("Que.GeneratePairingToken() [0x{0}] Base: {1}{2}", lRequestId.ToString("X8"), _strQueBaseURL, strPageURL);

			dtFormContent.Add("username", _strQueUser);
			dtFormContent.Add("password", _strQuePassword);
			dtFormContent.Add("deviceName", _strDeviceName);
			dtFormContent.Add("client", "ios");
			dtFormContent.Add("deviceUniqueIdentifier", _strDeviceUniqueIdentifier);

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(TimeSpan.FromSeconds(_iCancellationTime));

				httpResponse = await _httpClientAuth.PostAsync(_strQueBaseURL + strPageURL, new FormUrlEncodedContent(dtFormContent), cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					strResponse = await httpResponse.Content.ReadAsStringAsync();

					Logging.WriteDebugLog("Que.GeneratePairingToken() [0x{0}] Responded", lRequestId.ToString("X8"));

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					_pairingToken = new PairingToken(jsonResponse.pairingToken.ToString());
				}
				else
				{
					Logging.WriteDebugLogError("Que.GeneratePairingToken()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);
					bRetVal = false;
					goto Cleanup;
				}
			}
			catch (Exception eException)
			{
				if (eException.InnerException != null)
					Logging.WriteDebugLogError("Que.GeneratePairingToken()", lRequestId, eException.InnerException, "Unable to process API HTTP response.");
				else
					Logging.WriteDebugLogError("Que.GeneratePairingToken()", lRequestId, eException, "Unable to process API HTTP response.");

				bRetVal = false;
				goto Cleanup;
			}

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();

			if (!bRetVal)
				_pairingToken = null;

			return bRetVal;
		}

		
		public static async Task<bool> GenerateBearerToken()
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			Dictionary<string, string> dtFormContent = new Dictionary<string, string>();
			QueToken queToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL = "/api/v0/oauth/token";
			string strResponse;
			dynamic jsonResponse;
			bool bRetVal = true;

			Logging.WriteDebugLog("Que.GenerateBearerToken() [0x{0}] Base: {1}{2}", lRequestId.ToString("X8"), _strQueBaseURL, strPageURL);

			dtFormContent.Add("grant_type", "refresh_token");
			dtFormContent.Add("refresh_token", _pairingToken.Token);
			dtFormContent.Add("client_id", "app");

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(TimeSpan.FromSeconds(_iCancellationTime));

				httpResponse = await _httpClientAuth.PostAsync(_strQueBaseURL + strPageURL, new FormUrlEncodedContent(dtFormContent), cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					strResponse = await httpResponse.Content.ReadAsStringAsync();

					Logging.WriteDebugLog("Que.GenerateBearerToken() [0x{0}] Responded", lRequestId.ToString("X8"));

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					queToken = new QueToken();
					queToken.BearerToken = jsonResponse.access_token;
					queToken.TokenExpires = DateTime.Now.AddSeconds(int.Parse(jsonResponse.expires_in.ToString()));

					_queToken = queToken;
				}
				else
				{
					Logging.WriteDebugLogError("Que.GenerateBearerToken()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);
					bRetVal = false;
					goto Cleanup;
				}
			}
			catch (Exception eException)
			{
				if (eException.InnerException != null)
					Logging.WriteDebugLogError("Que.GenerateBearerToken()", lRequestId, eException.InnerException, "Unable to process API HTTP response.");
				else
					Logging.WriteDebugLogError("Que.GenerateBearerToken()", lRequestId, eException, "Unable to process API HTTP response.");

				bRetVal = false;
				goto Cleanup;
			}

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();

			if (!bRetVal)
				_queToken = null;

			return bRetVal;
		}

		public async static void TokenMonitor()
		{
			WaitHandle[] waitHandles = new WaitHandle[] { _eventStop, _eventAuthenticationFailure };
			int iWaitHandle = 0;
			bool bExit = false;

			Logging.WriteDebugLog("Que.TokenMonitor()");

			if (await GeneratePairingToken())
				if (await GenerateBearerToken())
					_httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _queToken.BearerToken);

			while (!bExit)
			{
				iWaitHandle = WaitHandle.WaitAny(waitHandles, TimeSpan.FromSeconds(_iAuthenticationInterval));

				switch (iWaitHandle)
				{
					case 0: // Stop
						bExit = true;

						break;

					case 1: // Authentication Failure
						if (await GeneratePairingToken())
							await GenerateBearerToken();

						break;

					case WaitHandle.WaitTimeout: // Wait Timeout
						if (_pairingToken == null || _queToken == null)
						{
							if (await GeneratePairingToken())
								await GenerateBearerToken();
						}
						else if (_queToken != null && _queToken.TokenExpires <= DateTime.Now.Subtract(TimeSpan.FromMinutes(5)))
						{
							Logging.WriteDebugLog("Que.TokenMonitor() Refreshing expiring bearer token");
							if (await GenerateBearerToken())
								_httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _queToken.BearerToken);
						}

						break;
				}
			}

			Logging.WriteDebugLog("Que.TokenMonitor() Complete");
		}

		public async static Task<bool> GetAirConditionerState()
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL = "/api/v0/client/ac-systems/status/latest?serial=";
			string strResponse;
			dynamic jsonResponse;
			bool bRetVal = true;
			bool bValid = true;
			JArray aEnabledZones;
			AirConditionerData airConditionerData = new AirConditionerData();

			Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Base: {1}{2}{3}", lRequestId.ToString("X8"), _strQueBaseURL, strPageURL, _strSerialNumber);

			if (!IsTokenValid())
			{
				Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Aborting - No Bearer Token", lRequestId.ToString("X8"));
				bRetVal = false;
				goto Cleanup;
			}

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(TimeSpan.FromSeconds(_iCancellationTime));

				httpResponse = await _httpClient.GetAsync(_strQueBaseURL + strPageURL + _strSerialNumber, cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					strResponse = await httpResponse.Content.ReadAsStringAsync();

					Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Responded", lRequestId.ToString("X8"));

					airConditionerData.LastUpdated = DateTime.Now;

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					// Compressor Mode
					airConditionerData.CompressorState = jsonResponse.lastKnownState.LiveAircon.CompressorMode;
					if (airConditionerData.CompressorState == "")
					{
						Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "lastKnownState.LiveAircon.CompressorMode");
						bValid = false;
					}
									
					// On
					if (!bool.TryParse(jsonResponse.lastKnownState.UserAirconSettings.isOn.ToString(), out airConditionerData.On))
					{
						Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "lastKnownState.UserAirconSettings.isOn");
						bValid = false;
					}

					// Mode
					airConditionerData.Mode = jsonResponse.lastKnownState.UserAirconSettings.Mode;
					if (airConditionerData.Mode == "")
					{
						Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "lastKnownState.UserAirconSettings.Mode");
						bValid = false;
					}

					// Fan Mode
					airConditionerData.FanMode = jsonResponse.lastKnownState.UserAirconSettings.FanMode;
					if (airConditionerData.FanMode == "")
					{
						Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "lastKnownState.UserAirconSettings.FanMode");
						bValid = false;
					}

					// Cooling Temperature
					if (!double.TryParse(jsonResponse.lastKnownState.UserAirconSettings.TemperatureSetpoint_Cool_oC.ToString(), out airConditionerData.SetTemperatureCooling))
					{
						Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "lastKnownState.UserAirconSettings.TemperatureSetpoint_Cool_oC");
						bValid = false;
					}

					// Heating Temperature
					if (!double.TryParse(jsonResponse.lastKnownState.UserAirconSettings.TemperatureSetpoint_Heat_oC.ToString(), out airConditionerData.SetTemperatureHeating))
					{
						Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "lastKnownState.UserAirconSettings.TemperatureSetpoint_Heat_oC");
						bValid = false;
					}

					// Zones
					aEnabledZones = jsonResponse.lastKnownState.UserAirconSettings.EnabledZones;
					if (aEnabledZones.Count != 8)
					{
						Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "lastKnownState.UserAirconSettings.TemperatureSetpoint_Heat_oC");
						bValid = false;
					}

					airConditionerData.Zones = new Dictionary<int, AirConditionerZone>();

					for (int iIndex = 0; iIndex < _iZoneCount; iIndex++)
					{
						airConditionerData.Zones.Add(iIndex + 1, new AirConditionerZone());

						if (!bool.TryParse(aEnabledZones[iIndex].ToString(), out airConditionerData.Zones[iIndex + 1].State))
						{
							Logging.WriteDebugLog("Que.GetAirConditionerState() [0x{0}] Unable to read zone state information: {1}", lRequestId.ToString("X8"), "lastKnownState.UserAirconSettings.EnabledZones");
							bValid = false;
						}

						airConditionerData.Zones[iIndex + 1].Name = jsonResponse.lastKnownState.RemoteZoneInfo[iIndex].NV_Title;
						if (airConditionerData.Zones[iIndex + 1].Name == "")
							airConditionerData.Zones[iIndex + 1].Name = "Zone " + (iIndex + 1);
						airConditionerData.Zones[iIndex + 1].Temperature = jsonResponse.lastKnownState.RemoteZoneInfo[iIndex].LiveTemp_oC;
					}

					// Update Air Conditioner Data
					if (bValid)
					{
						lock (_oLockData)
						{
							_airConditionerData = airConditionerData;
						}
					}
				}
				else
				{
					Logging.WriteDebugLogError("Que.GetAirConditionerState()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);
					bRetVal = false;
					goto Cleanup;
				}
			}
			catch (Exception eException)
			{
				if (eException.InnerException != null)
					Logging.WriteDebugLogError("Que.GetAirConditionerState()", lRequestId, eException.InnerException, "Unable to process API HTTP response.");
				else
					Logging.WriteDebugLogError("Que.GetAirConditionerState()", lRequestId, eException, "Unable to process API HTTP response.");

				bRetVal = false;
				goto Cleanup;
			}

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();

			return bRetVal;
		}

		public async static void AirConditionerMonitor()
		{
			WaitHandle[] waitHandles = new WaitHandle[] { _eventStop };
			int iWaitHandle = 0, iWaitInterval = 5;
			bool bExit = false;

			Logging.WriteDebugLog("Que.AirConditionerMonitor()");

			while (!bExit)
			{
				iWaitHandle = WaitHandle.WaitAny(waitHandles, TimeSpan.FromSeconds(iWaitInterval));

				switch (iWaitHandle)
				{
					case 0: // Stop
						bExit = true;

						break;

					case WaitHandle.WaitTimeout: // Wait Timeout
						if (await GetAirConditionerState())
							MQTTRegister();

						break;
				}

				iWaitInterval = _iStateRetrievalInterval;
			}

			Logging.WriteDebugLog("Que.AirConditionerMonitor() Complete");
		}

		public static bool IsTokenValid()
		{
			if (_queToken != null && _queToken.TokenExpires > DateTime.Now)
				return true;
			else
				return false;
		}

		private static void MQTTRegister()
		{
			Logging.WriteDebugLog("Que.MQTTRegister()");

			MQTT.SendMessage("homeassistant/climate/actronque/config", "{{\"name\":\"{1}\",\"modes\":[\"off\",\"auto\",\"cool\",\"fan_only\",\"heat\"],\"fan_modes\":[\"high\",\"medium\",\"low\"],\"mode_command_topic\":\"actronque/mode/set\",\"temperature_command_topic\":\"actronque/temperature/set\",\"fan_mode_command_topic\":\"actronque/fan/set\",\"min_temp\":\"12\",\"max_temp\":\"30\",\"temp_step\":\"0.5\",\"fan_mode_state_topic\":\"actronque/fanmode\",\"action_topic\":\"actronque/compressor\",\"temperature_state_topic\":\"actronque/settemperature\",\"mode_state_topic\":\"actronque/mode\",\"current_temperature_topic\":\"actronque/temperature\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower(), _strAirConditionerName);

			foreach (int iZone in _airConditionerData.Zones.Keys)
			{
				MQTT.SendMessage(string.Format("homeassistant/switch/actronque/airconzone{0}/config", iZone), "{{\"name\":\"{0} Zone\",\"state_topic\":\"actronque/zone{1}\",\"command_topic\":\"actronque/zone{1}/set\",\"payload_on\":\"ON\",\"payload_off\":\"OFF\",\"state_on\":\"ON\",\"state_off\":\"OFF\",\"availability_topic\":\"{2}/status\"}}", _airConditionerData.Zones[iZone].Name, iZone, Service.ServiceName.ToLower());
				MQTT.Subscribe("actronque/zone{0}/set", iZone);

				/*if (_bRegisterZoneTemperatures)
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/airconzone{0}/config", iZone), "{{\"name\":\"{0}\",\"state_topic\":\"actron/aircon/zone{1}/temperature\",\"unit_of_measurement\":\"\u00B0C\",\"availability_topic\":\"{2}/status\"}}", AirConditioner.Zones[iZone].Name, iZone, _strServiceName.ToLower());
				else
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/airconzone{0}/config", iZone), "{{}}"); // Clear existing devices*/
			}

			MQTT.Subscribe("actronque/mode/set");
			MQTT.Subscribe("actronque/fan/set");
			MQTT.Subscribe("actronque/temperature/set");
		}
	}
}
