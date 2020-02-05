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
		private static string _strPairingTokenFile = "/data/pairingtoken.json";
		private static string _strBearerTokenFile = "/data/bearertoken.json";
		private static string _strDeviceUniqueIdentifier = "1111";
		private static string _strQueUser, _strQuePassword, _strSerialNumber;
		private static string _strNextEventURL = "";
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
			HttpClientHandler httpClientHandler = new HttpClientHandler();

			Logging.WriteDebugLog("Que.Que()");

			if (httpClientHandler.SupportsAutomaticDecompression)
				httpClientHandler.AutomaticDecompression = System.Net.DecompressionMethods.All;

			_httpClientAuth = new HttpClient(httpClientHandler);

			_httpClientAuth.DefaultRequestHeaders.UserAgent.ParseAdd(_strBaseUserAgent);
			_httpClientAuth.BaseAddress = new Uri(_strQueBaseURL);

			_httpClient = new HttpClient(httpClientHandler);

			_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_strBaseUserAgent);
			_httpClient.BaseAddress = new Uri(_strQueBaseURL);
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

				httpResponse = await _httpClientAuth.PostAsync(strPageURL, new FormUrlEncodedContent(dtFormContent), cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					strResponse = await httpResponse.Content.ReadAsStringAsync();

					Logging.WriteDebugLog("Que.GeneratePairingToken() [0x{0}] Responded (Encoding {1}, {2} bytes)", lRequestId.ToString("X8"), httpResponse.Content.Headers.ContentEncoding.ToString() == "" ? "N/A" : httpResponse.Content.Headers.ContentEncoding.ToString(), httpResponse.Content.Headers.ContentLength);

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					_pairingToken = new PairingToken(jsonResponse.pairingToken.ToString());

					// Update Token File
					try
					{
						File.WriteAllText(_strPairingTokenFile, JsonConvert.SerializeObject(_pairingToken));
					}
					catch (Exception eException)
					{
						Logging.WriteDebugLogError("Que.GeneratePairingToken()", eException, "Unable to update json file.");
					}
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

				httpResponse = await _httpClientAuth.PostAsync(strPageURL, new FormUrlEncodedContent(dtFormContent), cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					strResponse = await httpResponse.Content.ReadAsStringAsync();

					Logging.WriteDebugLog("Que.GenerateBearerToken() [0x{0}] Responded (Encoding {1}, {2} bytes)", lRequestId.ToString("X8"), httpResponse.Content.Headers.ContentEncoding.ToString() == "" ? "N/A" : httpResponse.Content.Headers.ContentEncoding.ToString(), httpResponse.Content.Headers.ContentLength);

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					queToken = new QueToken();
					queToken.BearerToken = jsonResponse.access_token;
					queToken.TokenExpires = DateTime.Now.AddSeconds(int.Parse(jsonResponse.expires_in.ToString()));

					_queToken = queToken;

					// Update Token File
					try
					{
						File.WriteAllText(_strBearerTokenFile, JsonConvert.SerializeObject(_queToken));
					}
					catch (Exception eException)
					{
						Logging.WriteDebugLogError("Que.GenerateBearerToken()", eException, "Unable to update json file.");
					}
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

		public async static Task<bool> GetAirConditionerEvents()
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL, strPageURLFirstEvent = "/api/v0/client/ac-systems/events/latest?serial=";
			string strResponse;
			dynamic jsonResponse;
			bool bRetVal = true;
			bool bValid = true;
			string strEventType;
			JArray aEnabledZones;
			int iIndex;
			AirConditionerData airConditionerData = new AirConditionerData();

			if (_strNextEventURL == "")
				strPageURL = strPageURLFirstEvent + _strSerialNumber;
			else
				strPageURL = _strNextEventURL;

			Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Base: {1}{2}", lRequestId.ToString("X8"), _strQueBaseURL, strPageURL);

			if (!IsTokenValid())
			{
				Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Aborting - No Bearer Token", lRequestId.ToString("X8"));
				bRetVal = false;
				goto Cleanup;
			}

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(TimeSpan.FromSeconds(_iCancellationTime));

				httpResponse = await _httpClient.GetAsync(strPageURL, cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					strResponse = await httpResponse.Content.ReadAsStringAsync();

					Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Responded (Encoding {1}, {2} bytes)", lRequestId.ToString("X8"), httpResponse.Content.Headers.ContentEncoding.ToString() == "" ? "N/A" : httpResponse.Content.Headers.ContentEncoding.ToString(), httpResponse.Content.Headers.ContentLength);

					//Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Response: {1}", lRequestId.ToString("X8"), strResponse);

					airConditionerData.LastUpdated = DateTime.Now;

					strResponse = strResponse.Replace("ac-newer-events", "acnewerevents");

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					_strNextEventURL = jsonResponse._links.acnewerevents.href;

					Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Next Event URL: {1}", lRequestId.ToString("X8"), _strNextEventURL);

					for (int iEvent = jsonResponse.events.Count - 1; iEvent >= 0; iEvent--)
					{
						strEventType = jsonResponse.events[iEvent].type;

						Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Event Type: {1}", lRequestId.ToString("X8"), strEventType);

						switch (strEventType)
						{
							case "status-change-broadcast":
								foreach (JProperty change in jsonResponse.events[iEvent].data)
								{
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Incremental Update: {1}", lRequestId.ToString("X8"), change.Name);

									// Compressor Mode
									if (change.Name == "LiveAircon.CompressorMode")
									{
										airConditionerData.CompressorState = change.Value.ToString();
										if (airConditionerData.CompressorState == "")
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "LiveAircon.CompressorMode");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.CompressorState = airConditionerData.CompressorState;
											}
										}
									}
									// Mode
									else if (change.Name == "UserAirconSettings.Mode")
									{
										airConditionerData.Mode = change.Value.ToString();
										if (airConditionerData.Mode == "")

											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.Mode");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.Mode = airConditionerData.Mode;
											}
										}
									}
									// Fan Mode
									else if (change.Name == "UserAirconSettings.FanMode")
									{
										airConditionerData.FanMode = change.Value.ToString();
										if (airConditionerData.FanMode == "")

											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.FanMode");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.FanMode = airConditionerData.FanMode;
											}
										}
									}
									// On
									else if (change.Name == "UserAirconSettings.isOn")
									{
										if (!bool.TryParse(change.Value.ToString(), out airConditionerData.On))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.isOn");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.On = airConditionerData.On;
											}
										}
									}
									// Live Temperature
									else if (change.Name == "MasterInfo.LiveTemp_oC")
									{
										if (!double.TryParse(change.Value.ToString(), out airConditionerData.Temperature))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "MasterInfo.LiveTemp_oC");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.Temperature = airConditionerData.Temperature;
											}
										}
									}
									// Remote Zone
									else if (change.Name.StartsWith("RemoteZoneInfo["))
									{
										iIndex = int.Parse(change.Name.Substring(change.Name.IndexOf("[") + 1, 1));

										// Live Temperature
										if (change.Name.EndsWith("].LiveTemp_oC"))
										{
											lock (_oLockData)
											{
												_airConditionerData.Zones[iIndex + 1].Temperature = double.Parse(change.Value.ToString());
											}
										}
										// Cooling Set Temperature
										else if (change.Name.EndsWith("].TemperatureSetpoint_Cool_oC"))
										{
											lock (_oLockData)
											{
												_airConditionerData.Zones[iIndex + 1].SetTemperatureCooling = double.Parse(change.Value.ToString());
											}
										}
										// Heating Set Temperature
										else if (change.Name.EndsWith("].TemperatureSetpoint_Heat_oC"))
										{
											lock (_oLockData)
											{
												_airConditionerData.Zones[iIndex + 1].SetTemperatureHeating = double.Parse(change.Value.ToString());
											}
										}
									}
									// Enabled Zone
									else if (change.Name.StartsWith("UserAirconSettings.EnabledZones["))
									{
										iIndex = int.Parse(change.Name.Substring(change.Name.IndexOf("[") + 1, 1));

										lock (_oLockData)
										{
											_airConditionerData.Zones[iIndex + 1].State = bool.Parse(change.Value.ToString());
										}
									}
								}

								break;

							case "full-status-broadcast":
								// Compressor Mode
								airConditionerData.CompressorState = jsonResponse.events[iEvent].data.LiveAircon.CompressorMode;
								if (airConditionerData.CompressorState == "")
								{
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "LiveAircon.CompressorMode");
									bValid = false;
								}

								// On
								if (!bool.TryParse(jsonResponse.events[iEvent].data.UserAirconSettings.isOn.ToString(), out airConditionerData.On))
								{
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.isOn");
									bValid = false;
								}

								// Mode
								airConditionerData.Mode = jsonResponse.events[iEvent].data.UserAirconSettings.Mode;
								if (airConditionerData.Mode == "")
								{
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.Mode");
									bValid = false;
								}

								// Fan Mode
								airConditionerData.FanMode = jsonResponse.events[iEvent].data.UserAirconSettings.FanMode;
								if (airConditionerData.FanMode == "")
								{
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.FanMode");
									bValid = false;
								}

								// Set Cooling Temperature
								if (!double.TryParse(jsonResponse.events[iEvent].data.UserAirconSettings.TemperatureSetpoint_Cool_oC.ToString(), out airConditionerData.SetTemperatureCooling))
								{
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.TemperatureSetpoint_Cool_oC");
									bValid = false;
								}

								// Set Heating Temperature
								if (!double.TryParse(jsonResponse.events[iEvent].data.UserAirconSettings.TemperatureSetpoint_Heat_oC.ToString(), out airConditionerData.SetTemperatureHeating))
								{
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.TemperatureSetpoint_Heat_oC");
									bValid = false;
								}

								// Live Temperature
								if (!double.TryParse(jsonResponse.events[iEvent].data.MasterInfo.LiveTemp_oC.ToString(), out airConditionerData.Temperature))
								{
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "MasterInfo.LiveTemp_oC");
									bValid = false;
								}

								// Zones
								aEnabledZones = jsonResponse.events[iEvent].data.UserAirconSettings.EnabledZones;
								if (aEnabledZones.Count != 8)
								{
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.TemperatureSetpoint_Heat_oC");
									bValid = false;
								}

								airConditionerData.Zones = new Dictionary<int, AirConditionerZone>();

								for (int iZoneIndex = 0; iZoneIndex < _iZoneCount; iZoneIndex++)
								{
									airConditionerData.Zones.Add(iZoneIndex + 1, new AirConditionerZone());

									if (!bool.TryParse(aEnabledZones[iZoneIndex].ToString(), out airConditionerData.Zones[iZoneIndex + 1].State))
									{
										Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read zone state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.EnabledZones");
										bValid = false;
									}

									airConditionerData.Zones[iZoneIndex + 1].Name = jsonResponse.events[iEvent].data.RemoteZoneInfo[iZoneIndex].NV_Title;
									if (airConditionerData.Zones[iZoneIndex + 1].Name == "")
										airConditionerData.Zones[iZoneIndex + 1].Name = "Zone " + (iZoneIndex + 1);
									airConditionerData.Zones[iZoneIndex + 1].Temperature = jsonResponse.events[iEvent].data.RemoteZoneInfo[iZoneIndex].LiveTemp_oC;
								}

								// Update Air Conditioner Data
								if (bValid)
								{
									lock (_oLockData)
									{
										_airConditionerData = airConditionerData;
									}
								}

								break;
						}
					}
				}
				else
				{
					Logging.WriteDebugLogError("Que.GetAirConditionerEvents()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);
					bRetVal = false;
					goto Cleanup;
				}
			}
			catch (Exception eException)
			{
				if (eException.InnerException != null)
					Logging.WriteDebugLogError("Que.GetAirConditionerEvents()", lRequestId, eException.InnerException, "Unable to process API HTTP response.");
				else
					Logging.WriteDebugLogError("Que.GetAirConditionerEvents()", lRequestId, eException, "Unable to process API HTTP response.");

				bRetVal = false;
				goto Cleanup;
			}

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();

			if (!bRetVal)
				_strNextEventURL = "";

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
						if (await GetAirConditionerEvents())
						{
							MQTTRegister();
							MQTTUpdateData();
						}

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

			MQTT.SendMessage("homeassistant/climate/actronque/config", "{{\"name\":\"{1}\",\"modes\":[\"off\",\"auto\",\"cool\",\"fan_only\",\"heat\"],\"fan_modes\":[\"high\",\"medium\",\"low\",\"auto\"],\"mode_command_topic\":\"actronque/mode/set\",\"temperature_command_topic\":\"actronque/temperature/set\",\"fan_mode_command_topic\":\"actronque/fan/set\",\"min_temp\":\"12\",\"max_temp\":\"30\",\"temp_step\":\"0.5\",\"fan_mode_state_topic\":\"actronque/fanmode\",\"action_topic\":\"actronque/compressor\",\"temperature_state_topic\":\"actronque/settemperature\",\"mode_state_topic\":\"actronque/mode\",\"current_temperature_topic\":\"actronque/temperature\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower(), _strAirConditionerName);

			foreach (int iZone in _airConditionerData.Zones.Keys)
			{
				MQTT.SendMessage(string.Format("homeassistant/switch/actronque/airconzone{0}/config", iZone), "{{\"name\":\"{0} Zone\",\"state_topic\":\"actronque/zone{1}\",\"command_topic\":\"actronque/zone{1}/set\",\"payload_on\":\"ON\",\"payload_off\":\"OFF\",\"state_on\":\"ON\",\"state_off\":\"OFF\",\"availability_topic\":\"{2}/status\"}}", _airConditionerData.Zones[iZone].Name, iZone, Service.ServiceName.ToLower());
				MQTT.Subscribe("actronque/zone{0}/set", iZone);

				MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/airconzone{0}/config", iZone), "{{\"name\":\"{0}\",\"state_topic\":\"actronque/zone{1}/temperature\",\"unit_of_measurement\":\"\u00B0C\",\"availability_topic\":\"{2}/status\"}}", _airConditionerData.Zones[iZone].Name, iZone, Service.ServiceName.ToLower());
			}

			MQTT.Subscribe("actronque/mode/set");
			MQTT.Subscribe("actronque/fan/set");
			MQTT.Subscribe("actronque/temperature/set");
		}

		private static void MQTTUpdateData()
		{
			Logging.WriteDebugLog("Que.MQTTUpdateData()");

			// Fan Mode
			switch (_airConditionerData.FanMode)
			{
				case "AUTO":
					MQTT.SendMessage("actronque/fanmode", "auto");
					break;

				case "LOW":
					MQTT.SendMessage("actronque/fanmode", "low");
					break;

				case "MED":
					MQTT.SendMessage("actronque/fanmode", "medium");
					break;

				case "HIGH":
					MQTT.SendMessage("actronque/fanmode", "high");
					break;

				default:
					Logging.WriteDebugLog("Que.MQTTUpdateData() Unexpected Fan Mode: {0}", _airConditionerData.FanMode);
					break;
			}

			// Temperature
			MQTT.SendMessage("actronque/temperature", _airConditionerData.Temperature.ToString("N1"));

			// Power, Mode & Set Temperature
			if (!_airConditionerData.On)
				MQTT.SendMessage("actronque/mode", "off");
			else
			{
				switch (_airConditionerData.Mode)
				{
					case "AUTO":
						MQTT.SendMessage("actronque/mode", "auto");
						MQTT.SendMessage("actronque/settemperature", GetSetTemperature().ToString("N1"));
						break;

					case "COOL":
						MQTT.SendMessage("actronque/mode", "cool");
						MQTT.SendMessage("actronque/settemperature", _airConditionerData.SetTemperatureCooling.ToString("N1"));
						break;

					case "HEAT":
						MQTT.SendMessage("actronque/mode", "heat");
						MQTT.SendMessage("actronque/settemperature", _airConditionerData.SetTemperatureHeating.ToString("N1"));
						break;

					case "FAN":
						MQTT.SendMessage("actronque/mode", "fan_only");
						break;

					default:
						Logging.WriteDebugLog("Que.MQTTUpdateData() Unexpected Mode: {0}", _airConditionerData.Mode);
						break;
				}
			}

			// Zones
			foreach (int iIndex in _airConditionerData.Zones.Keys)
			{
				MQTT.SendMessage(string.Format("actronque/zone{0}", iIndex), _airConditionerData.Zones[iIndex].State ? "ON" : "OFF");
				MQTT.SendMessage(string.Format("actronque/zone{0}/temperature", iIndex), _airConditionerData.Zones[iIndex].Temperature.ToString());
			}

			// Compressor
			switch (_airConditionerData.CompressorState)
			{
				case "HEAT":
					MQTT.SendMessage("actronque/compressor", "heating");
					break;

				case "COOL":
					MQTT.SendMessage("actronque/compressor", "cooling");
					break;

				case "OFF":
					MQTT.SendMessage("actronque/compressor", "off");
					break;

				case "IDLE":
					if (_airConditionerData.On)
						MQTT.SendMessage("actronque/compressor", "idle");
					else
						MQTT.SendMessage("actronque/compressor", "off");

					break;

				default:
					Logging.WriteDebugLog("Que.MQTTUpdateData() Unexpected Compressor State: {0}", _airConditionerData.CompressorState);
					break;
			}
		}


		private static double GetSetTemperature()
		{
			double dblSetTemperature = 0.0;
			
			Logging.WriteDebugLog("Que.GetSetTemperature()");

			try
			{
				dblSetTemperature = _airConditionerData.SetTemperatureHeating + ((_airConditionerData.SetTemperatureCooling - _airConditionerData.SetTemperatureHeating) / 2);

				dblSetTemperature = Math.Round(dblSetTemperature * 2, MidpointRounding.AwayFromZero) / 2;
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Que.GetSetTemperature()", eException, "Unable to determine set temperature mid-point.");
			}

			return dblSetTemperature;
		}		
	}
}
