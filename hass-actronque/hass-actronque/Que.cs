using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HMX.HASSActronQue
{
	public class Que
	{
		[Flags]
		public enum UpdateItems
		{
			None = 0,
			Main = 1,
			Zone1 = 2,
			Zone2 = 4,
			Zone3 = 8,
			Zone4 = 16,
			Zone5 = 32,
			Zone6 = 64,
			Zone7 = 128,
			Zone8 = 256
		}

		[Flags]
		public enum TemperatureSetType
		{
			Default,
			High,
			Low
		}

		private static string _strBaseURLQue = "https://que.actronair.com.au/";
		private static string _strBaseURLNeo = "https://nimbus.actronair.com.au/";
		private static string _strSystemType;
		private static string _strDeviceName = "HASSActronQue";
		private static string _strAirConditionerName = "Air Conditioner";
		private static string _strDeviceIdFile = "/data/deviceid.json";
		private static string _strPairingTokenFile = "/data/pairingtoken.json";
		private static string _strBearerTokenFile = "/data/bearertoken.json";
		private static string _strDeviceUniqueIdentifier = "";
		private static string _strQueUser, _strQuePassword, _strSerialNumber;
		//private static string _strNextEventURL = "";
		private static bool _bPerZoneControls = false;
		private static bool _bPerZoneSensors = false;
		private static bool _bSeparateHeatCool = false;
		private static bool _bNeoNoEventMode = false;
		private static bool _bEventsReceived = false;
		private static Queue<QueueCommand> _queueCommands = new Queue<QueueCommand>();
		private static HttpClient _httpClient = null, _httpClientAuth = null, _httpClientCommands = null;
		private static int _iCancellationTime = 15; // Seconds
		private static int _iPollInterval = 15; // Seconds
		private static int _iPollIntervalNeoNoEventsMode = 30; // Seconds
		private static int _iPollIntervalUpdate = 5; // Seconds
		private static int _iAuthenticationInterval = 60; // Seconds
		private static int _iQueueInterval = 10; // Seconds
		private static int _iCommandExpiry = 10; // Seconds
		private static int _iPostCommandSleepTimer = 2; // Seconds
		private static int _iPostCommandSleepTimerNeoNoEventsMode = 10; // Seconds
		private static int _iCommandAckRetryCounter = 3;
		private static int _iFailedBearerRequests = 0;
		private static int _iFailedBearerRequestMaximum = 10; // Retries
		private static int _iZoneCount = 0;
		private static ManualResetEvent _eventStop;
		private static AutoResetEvent _eventAuthenticationFailure = new AutoResetEvent(false);
		private static AutoResetEvent _eventQueue = new AutoResetEvent(false);
		private static AutoResetEvent _eventUpdate = new AutoResetEvent(false);
		private static PairingToken _pairingToken;
		private static QueToken _queToken = null;
		//private static AirConditionerData _airConditionerData = new AirConditionerData();
		private static Dictionary<string, AirConditionerUnit> _airConditionerUnits = new Dictionary<string, AirConditionerUnit>();
		//private static Dictionary<int, AirConditionerZone> _airConditionerZones = new Dictionary<int, AirConditionerZone>();
		private static object _oLockData = new object(), _oLockQueue = new object();
		private static bool _bCommandAckPending = false;

		public static Dictionary<string, AirConditionerUnit> Units
		{
			get { return _airConditionerUnits; }
		}

		static Que()
		{
			HttpClientHandler httpClientHandler = new HttpClientHandler();

			Logging.WriteDebugLog("Que.Que()");

			if (httpClientHandler.SupportsAutomaticDecompression)
				httpClientHandler.AutomaticDecompression = System.Net.DecompressionMethods.All;

			if (Service.IsDevelopment)
			{
				_httpClientAuth = new HttpClient(new LoggingClientHandler(httpClientHandler));

				_httpClient = new HttpClient(new LoggingClientHandler(httpClientHandler));

				_httpClientCommands = new HttpClient(new LoggingClientHandler(httpClientHandler));
			}
			else
			{
				_httpClientAuth = new HttpClient(httpClientHandler);

				_httpClient = new HttpClient(httpClientHandler);

				_httpClientCommands = new HttpClient(httpClientHandler);
			}
		}

		public static async void Initialise(string strQueUser, string strQuePassword, string strSerialNumber, string strSystemType, int iPollInterval, bool bPerZoneControls, bool bPerZoneSensors, bool bSeparateHeatCool, ManualResetEvent eventStop)
		{
			Thread threadMonitor;
			string strDeviceUniqueIdentifierInput;
			string[] strTokens;

			Logging.WriteDebugLog("Que.Initialise()");

			_strQueUser = strQueUser;
			_strQuePassword = strQuePassword;
			_strSerialNumber = strSerialNumber;
			_strSystemType = strSystemType;
			_bPerZoneControls = bPerZoneControls;
			_bPerZoneSensors = bPerZoneSensors;
			_iPollInterval = iPollInterval;
			_bSeparateHeatCool = bSeparateHeatCool;
			_eventStop = eventStop;

			_httpClientAuth.BaseAddress = new Uri(GetBaseURL());
			_httpClient.BaseAddress = new Uri(GetBaseURL());
			_httpClientCommands.BaseAddress = new Uri(GetBaseURL());

			// Get Device Id
			try
			{
				if (File.Exists(_strDeviceIdFile))
				{
					strDeviceUniqueIdentifierInput = JsonConvert.DeserializeObject<string>(await File.ReadAllTextAsync(_strDeviceIdFile));

					if (strDeviceUniqueIdentifierInput.Contains(","))
					{
						strTokens = strDeviceUniqueIdentifierInput.Split(new char[] { ',' });
						if (strTokens.Length == 2)
						{
							if (strTokens[0].ToLower() == _strQueUser.ToLower())
							{
								_strDeviceUniqueIdentifier = strTokens[1];

								Logging.WriteDebugLog("Que.Initialise() Device Id: {0}", _strDeviceUniqueIdentifier);
							}
							else
							{
								Logging.WriteDebugLog("Que.Initialise() Device Id: will regenerate (Que User changed)");
							}
						}
					}
					else
					{
						_strDeviceUniqueIdentifier = strDeviceUniqueIdentifierInput;

						// Update Device Id File
						try
						{
							await File.WriteAllTextAsync(_strDeviceIdFile, JsonConvert.SerializeObject(_strQueUser + "," + _strDeviceUniqueIdentifier));
						}
						catch (Exception eException)
						{
							Logging.WriteDebugLogError("Que.Initialise()", eException, "Unable to update json file.");
						}
					}					
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Que.Initialise()", eException, "Unable to read device json file.");
			}

			// Get Pairing Token
			try
			{
				if (_strDeviceUniqueIdentifier != "" && File.Exists(_strPairingTokenFile))
				{
					_pairingToken = JsonConvert.DeserializeObject<PairingToken>(await File.ReadAllTextAsync(_strPairingTokenFile));

					Logging.WriteDebugLog("Que.Initialise() Restored Pairing Token");
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Que.Initialise()", eException, "Unable to read pairing token json file.");
			}

			threadMonitor = new Thread(new ThreadStart(TokenMonitor));
			threadMonitor.Start();

			threadMonitor = new Thread(new ThreadStart(AirConditionerMonitor));
			threadMonitor.Start();

			threadMonitor = new Thread(new ThreadStart(QueueMonitor));
			threadMonitor.Start();
		}

		private static async Task<bool> GeneratePairingToken()
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			Dictionary<string, string> dtFormContent = new Dictionary<string, string>();
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL = "api/v0/client/user-devices";
			string strResponse;
			dynamic jsonResponse;
			bool bRetVal = true;

			Logging.WriteDebugLog("Que.GeneratePairingToken() [0x{0}] Base: {1}{2}", lRequestId.ToString("X8"), GetBaseURL(), strPageURL);

			if (_strDeviceUniqueIdentifier == "")
			{
				_strDeviceUniqueIdentifier = GenerateDeviceId();

				Logging.WriteDebugLog("Que.GeneratePairingToken() Device Id: {0}", _strDeviceUniqueIdentifier);

				// Update Device Id File
				try
				{
					await File.WriteAllTextAsync(_strDeviceIdFile, JsonConvert.SerializeObject(_strQueUser + "," + _strDeviceUniqueIdentifier));
				}
				catch (Exception eException)
				{
					Logging.WriteDebugLogError("Que.GeneratePairingToken()", eException, "Unable to update json file.");
				}
			}

			dtFormContent.Add("username", _strQueUser);
			dtFormContent.Add("password", _strQuePassword);
			dtFormContent.Add("deviceName", Service.IsDevelopment ? _strDeviceName + "Dev" : _strDeviceName);
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

					Logging.WriteDebugLog("Que.GeneratePairingToken() [0x{0}] Responded (Encoding {1}, {2} bytes)", lRequestId.ToString("X8"), httpResponse.Content.Headers.ContentEncoding.ToString() == "" ? "N/A" : httpResponse.Content.Headers.ContentEncoding.ToString(), (httpResponse.Content.Headers.ContentLength ?? 0) == 0 ? "N/A" : httpResponse.Content.Headers.ContentLength.ToString());

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					_pairingToken = new PairingToken(jsonResponse.pairingToken.ToString());

					// Update Token File
					try
					{
						await File.WriteAllTextAsync(_strPairingTokenFile, JsonConvert.SerializeObject(_pairingToken));
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
			catch (OperationCanceledException eException)
			{
				Logging.WriteDebugLogError("Que.GeneratePairingToken()", lRequestId, eException, "Unable to process API HTTP response - operation timed out.");

				bRetVal = false;
				goto Cleanup;
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

		private static async Task<bool> GenerateBearerToken()
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			Dictionary<string, string> dtFormContent = new Dictionary<string, string>();
			QueToken queToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL = "api/v0/oauth/token";
			string strResponse;
			dynamic jsonResponse;
			bool bRetVal = true;

			Logging.WriteDebugLog("Que.GenerateBearerToken() [0x{0}] Base: {1}{2}", lRequestId.ToString("X8"), _httpClientAuth.BaseAddress, strPageURL);

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

					Logging.WriteDebugLog("Que.GenerateBearerToken() [0x{0}] Responded (Encoding {1}, {2} bytes)", lRequestId.ToString("X8"), httpResponse.Content.Headers.ContentEncoding.ToString() == "" ? "N/A" : httpResponse.Content.Headers.ContentEncoding.ToString(), (httpResponse.Content.Headers.ContentLength ?? 0) == 0 ? "N/A" : httpResponse.Content.Headers.ContentLength.ToString());

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					queToken = new QueToken();
					queToken.BearerToken = jsonResponse.access_token;
					queToken.TokenExpires = DateTime.Now.AddSeconds(int.Parse(jsonResponse.expires_in.ToString()));

					_queToken = queToken;

					_httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _queToken.BearerToken);
					_httpClientCommands.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _queToken.BearerToken);

					// Update Token File
					try
					{
						await File.WriteAllTextAsync(_strBearerTokenFile, JsonConvert.SerializeObject(_queToken));
					}
					catch (Exception eException)
					{
						Logging.WriteDebugLogError("Que.GenerateBearerToken()", eException, "Unable to update json file.");
					}
				}
				else
				{
					if (httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
					{
						Logging.WriteDebugLogError("Que.GenerateBearerToken()", lRequestId, "Unable to process API response: {0}/{1}. Refreshing pairing token.", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

						_pairingToken = null;
					}
					else if (httpResponse.StatusCode == System.Net.HttpStatusCode.BadRequest)
					{
						// Increment Failed Request Counter
						_iFailedBearerRequests++;

						// Reset Pairing Token when Failed Request Counter reaches maximum.
						if (_iFailedBearerRequests == _iFailedBearerRequestMaximum)
						{
							Logging.WriteDebugLogError("Que.GenerateBearerToken()", lRequestId, "Unable to process API response: {0}/{1}. Attempt: {2} of {3} - refreshing pairing token.", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase, _iFailedBearerRequests, _iFailedBearerRequestMaximum);

							_pairingToken = null;
						}
						else
							Logging.WriteDebugLogError("Que.GenerateBearerToken()", lRequestId, "Unable to process API response: {0}/{1}. Attempt: {2} of {3}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase, _iFailedBearerRequests, _iFailedBearerRequestMaximum);
					}
					else
						Logging.WriteDebugLogError("Que.GenerateBearerToken()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

					bRetVal = false;
					goto Cleanup;
				}
			}
			catch (OperationCanceledException eException)
			{
				Logging.WriteDebugLogError("Que.GenerateBearerToken()", lRequestId, eException, "Unable to process API HTTP response - operation timed out.");

				bRetVal = false;
				goto Cleanup;
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

			// Reset Failed Request Counter
			_iFailedBearerRequests = 0;

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();

			if (!bRetVal)
				_queToken = null;

			return bRetVal;
		}

		private async static void TokenMonitor()
		{
			WaitHandle[] waitHandles = new WaitHandle[] { _eventStop, _eventAuthenticationFailure };
			int iWaitHandle = 0;
			bool bExit = false;

			Logging.WriteDebugLog("Que.TokenMonitor()");

			if (_pairingToken == null)
			{
				if (await GeneratePairingToken())
					await GenerateBearerToken();
			}
			else
				await GenerateBearerToken();

			while (!bExit)
			{
				iWaitHandle = WaitHandle.WaitAny(waitHandles, TimeSpan.FromSeconds(_iAuthenticationInterval));

				switch (iWaitHandle)
				{
					case 0: // Stop
						bExit = true;

						break;

					case 1: // Authentication Failure
						if (_pairingToken == null)
						{
							if (await GeneratePairingToken())
								await GenerateBearerToken();
						}
						else
							await GenerateBearerToken();

						break;

					case WaitHandle.WaitTimeout: // Wait Timeout
						if (_pairingToken == null)
						{
							if (await GeneratePairingToken())
								await GenerateBearerToken();
						}
						else if (_queToken == null)
							await GenerateBearerToken();
						else if (_queToken != null && _queToken.TokenExpires <= DateTime.Now.Subtract(TimeSpan.FromMinutes(5)))
						{
							Logging.WriteDebugLog("Que.TokenMonitor() Refreshing expiring bearer token");
							await GenerateBearerToken();
						}

						break;
				}
			}

			Logging.WriteDebugLog("Que.TokenMonitor() Complete");
		}

		private async static Task<bool> GetAirConditionerSerial()
		{
			AirConditionerUnit unit;
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL = "api/v0/client/ac-systems?includeAcms=true&includeNeo=true";
			string strResponse;
			dynamic jsonResponse;
			bool bRetVal = true;
			string strSerial, strDescription, strType;

			Logging.WriteDebugLog("Que.GetAirConditionerSerial() [0x{0}] Base: {1}{2}", lRequestId.ToString("X8"), _httpClient.BaseAddress, strPageURL);

			if (!IsTokenValid())
			{
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

					Logging.WriteDebugLog("Que.GetAirConditionerSerial() [0x{0}] Responded (Encoding {1}, {2} bytes)", lRequestId.ToString("X8"), httpResponse.Content.Headers.ContentEncoding.ToString() == "" ? "N/A" : httpResponse.Content.Headers.ContentEncoding.ToString(), (httpResponse.Content.Headers.ContentLength ?? 0) == 0 ? "N/A" : httpResponse.Content.Headers.ContentLength.ToString());

					if (Service.IsDevelopment)
						Logging.WriteDebugLog("Que.GetAirConditionerSerial() [0x{0}] Response: {1}", lRequestId.ToString("X8"), strResponse);

					strResponse = strResponse.Replace("ac-system", "acsystem");

					if (!strResponse.Contains("acsystem"))
					{
						Logging.WriteDebugLog("Que.GetAirConditionerSerial() [0x{0}] No data returned from Que service - check Que user name and password.");
						
						bRetVal = false;
						goto Cleanup;
					}

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					for (int iIndex = 0; iIndex < jsonResponse._embedded.acsystem.Count; iIndex++)
					{
						strSerial = jsonResponse._embedded.acsystem[iIndex].serial.ToString();
						strDescription = jsonResponse._embedded.acsystem[iIndex].description.ToString();
						strType = jsonResponse._embedded.acsystem[iIndex].type.ToString();

						Logging.WriteDebugLog("Que.GetAirConditionerSerial() [0x{0}] Found AC: {1} - {2} ({3})", lRequestId.ToString("X8"), strSerial, strDescription, strType);

						if (_strSerialNumber == "" || _strSerialNumber == strSerial)
						{
							unit = new AirConditionerUnit(strDescription.Trim(), strSerial);
							_airConditionerUnits.Add(strSerial, unit);

							Logging.WriteDebugLog("Que.GetAirConditionerSerial() [0x{0}] Monitoring AC: {1}", lRequestId.ToString("X8"), strSerial);
						}
					}
				}
				else
				{
					if (httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
					{
						Logging.WriteDebugLogError("Que.GetAirConditionerSerial()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

						_eventAuthenticationFailure.Set();
					}
					else
						Logging.WriteDebugLogError("Que.GetAirConditionerSerial()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

					bRetVal = false;
					goto Cleanup;
				}
			}
			catch (OperationCanceledException eException)
			{
				Logging.WriteDebugLogError("Que.GetAirConditionerSerial()", lRequestId, eException, "Unable to process API HTTP response - operation timed out.");

				bRetVal = false;
				goto Cleanup;
			}
			catch (Exception eException)
			{
				if (eException.InnerException != null)
					Logging.WriteDebugLogError("Que.GetAirConditionerSerial()", lRequestId, eException.InnerException, "Unable to process API HTTP response.");
				else
					Logging.WriteDebugLogError("Que.GetAirConditionerSerial()", lRequestId, eException, "Unable to process API HTTP response.");

				bRetVal = false;
				goto Cleanup;
			}

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();

			return bRetVal;
		}

		private async static Task<bool> GetAirConditionerZones()
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL = "api/v0/client/ac-systems/status/latest?serial=";
			string strResponse;
			dynamic jsonResponse;
			bool bRetVal = true;
			//Dictionary<int, AirConditionerZone> dZones = new Dictionary<int, AirConditionerZone>();
			AirConditionerZone zone;
			AirConditionerSensor sensor;

			_iZoneCount = 0;

			foreach (AirConditionerUnit unit in _airConditionerUnits.Values)
			{
				Logging.WriteDebugLog("Que.GetAirConditionerZones() [0x{0}] Base: {1}{2}{3}", lRequestId.ToString("X8"), _httpClient.BaseAddress, strPageURL, unit.Serial);

				if (!IsTokenValid())
				{
					bRetVal = false;
					goto Cleanup;
				}

				try
				{
					cancellationToken = new CancellationTokenSource();
					cancellationToken.CancelAfter(TimeSpan.FromSeconds(_iCancellationTime));

					httpResponse = await _httpClient.GetAsync(strPageURL + unit.Serial, cancellationToken.Token);

					if (httpResponse.IsSuccessStatusCode)
					{
						strResponse = await httpResponse.Content.ReadAsStringAsync();

						Logging.WriteDebugLog("Que.GetAirConditionerZones() [0x{0}] Responded (Encoding {1}, {2} bytes)", lRequestId.ToString("X8"), httpResponse.Content.Headers.ContentEncoding.ToString() == "" ? "N/A" : httpResponse.Content.Headers.ContentEncoding.ToString(), (httpResponse.Content.Headers.ContentLength ?? 0) == 0 ? "N/A" : httpResponse.Content.Headers.ContentLength.ToString());

						jsonResponse = JsonConvert.DeserializeObject(strResponse);

						// Zones
						if (jsonResponse.ContainsKey("lastKnownState") && jsonResponse.lastKnownState.ContainsKey("RemoteZoneInfo"))
						{
							for (int iZoneIndex = 0; iZoneIndex < jsonResponse.lastKnownState.RemoteZoneInfo.Count; iZoneIndex++)
							{
								if (bool.Parse(jsonResponse.lastKnownState.RemoteZoneInfo[iZoneIndex].NV_Exists.ToString()))
								{
									zone = new AirConditionerZone();
									zone.Sensors = new Dictionary<string, AirConditionerSensor>();
									zone.Exists = true;

									zone.Name = jsonResponse.lastKnownState.RemoteZoneInfo[iZoneIndex].NV_Title;
									if (zone.Name == "")
										zone.Name = "Zone " + (iZoneIndex + 1);
									zone.Temperature = jsonResponse.lastKnownState.RemoteZoneInfo[iZoneIndex].LiveTemp_oC;

									Logging.WriteDebugLog("Que.GetAirConditionerZones() [0x{0}] Zone: {1} - {2}", lRequestId.ToString("X8"), iZoneIndex + 1, zone.Name);

									if (jsonResponse.lastKnownState.RemoteZoneInfo[iZoneIndex].ContainsKey("Sensors"))
									{
										foreach (JProperty sensorJson in jsonResponse.lastKnownState.RemoteZoneInfo[iZoneIndex].Sensors)
										{
											sensor = new AirConditionerSensor();
											sensor.Name = zone.Name + " Sensor " + sensorJson.Name;
											sensor.Serial = sensorJson.Name;

											Logging.WriteDebugLog("Que.GetAirConditionerZones() [0x{0}] Zone Sensor: {1}", lRequestId.ToString("X8"), sensorJson.Name);

											zone.Sensors.Add(sensorJson.Name, sensor);
										}
									}
								}
								else
								{
									zone = new AirConditionerZone();
									zone.Sensors = new Dictionary<string, AirConditionerSensor>();
									zone.Exists = false;

									Logging.WriteDebugLog("Que.GetAirConditionerZones() [0x{0}] Zone: {1} - Non Existent Zone", lRequestId.ToString("X8"), iZoneIndex + 1);
								}

								unit.Zones.Add(iZoneIndex + 1, zone);
								_iZoneCount++;
							}
						}
						else
							Logging.WriteDebugLog("Que.GetAirConditionerZones() [0x{0}] Responded - No Data. Retrying.", lRequestId.ToString("X8"));
					}
					else
					{
						if (httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
						{
							Logging.WriteDebugLogError("Que.GetAirConditionerZones()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

							_eventAuthenticationFailure.Set();
						}
						else
							Logging.WriteDebugLogError("Que.GetAirConditionerZones()", lRequestId, "Unable to process API response: {0}/{1}. Is the serial number correct?", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

						bRetVal = false;
						goto Cleanup;
					}
				}
				catch (OperationCanceledException eException)
				{
					Logging.WriteDebugLogError("Que.GetAirConditionerZones()", lRequestId, eException, "Unable to process API HTTP response - operation timed out.");

					bRetVal = false;
					goto Cleanup;
				}
				catch (Exception eException)
				{
					if (eException.InnerException != null)
						Logging.WriteDebugLogError("Que.GetAirConditionerZones()", lRequestId, eException.InnerException, "Unable to process API HTTP response. Is the serial number correct?");
					else
						Logging.WriteDebugLogError("Que.GetAirConditionerZones()", lRequestId, eException, "Unable to process API HTTP response. Is the serial number correct?");

					bRetVal = false;
					goto Cleanup;
				}

			Cleanup:
				cancellationToken?.Dispose();
				httpResponse?.Dispose();
			}

			return bRetVal;
		}

		private async static Task<UpdateItems> GetAirConditionerFullStatus(AirConditionerUnit unit)
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL = "api/v0/client/ac-systems/status/latest?serial=";
			string strResponse;
			dynamic jsonResponse;
			UpdateItems updateItems = UpdateItems.None;

			Logging.WriteDebugLog("Que.GetAirConditionerFullStatus() [0x{0}] Base: {1}{2}{3}", lRequestId.ToString("X8"), _httpClient.BaseAddress, strPageURL, unit.Serial);

			if (!IsTokenValid())
				goto Cleanup;

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(TimeSpan.FromSeconds(_iCancellationTime));

				httpResponse = await _httpClient.GetAsync(strPageURL + unit.Serial, cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					strResponse = await httpResponse.Content.ReadAsStringAsync();

					Logging.WriteDebugLog("Que.GetAirConditionerFullStatus() [0x{0}] Responded (Encoding {1}, {2} bytes)", lRequestId.ToString("X8"), httpResponse.Content.Headers.ContentEncoding.ToString() == "" ? "N/A" : httpResponse.Content.Headers.ContentEncoding.ToString(), (httpResponse.Content.Headers.ContentLength ?? 0) == 0 ? "N/A" : httpResponse.Content.Headers.ContentLength.ToString());

					lock (_oLockData)
					{
						unit.Data.LastUpdated = DateTime.Now;
					}

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					ProcessFullStatus(lRequestId, unit, jsonResponse.lastKnownState);

					updateItems = UpdateItems.Main | UpdateItems.Zone1 | UpdateItems.Zone2 | UpdateItems.Zone3 | UpdateItems.Zone4 | UpdateItems.Zone5 | UpdateItems.Zone6 | UpdateItems.Zone7 | UpdateItems.Zone8;
				}
				else
				{
					if (httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
					{
						Logging.WriteDebugLogError("Que.GetAirConditionerFullStatus()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

						_eventAuthenticationFailure.Set();
					}
					else
						Logging.WriteDebugLogError("Que.GetAirConditionerFullStatus()", lRequestId, "Unable to process API response: {0}/{1}. Is the serial number correct?", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

					goto Cleanup;
				}
			}
			catch (OperationCanceledException eException)
			{
				Logging.WriteDebugLogError("Que.GetAirConditionerFullStatus()", lRequestId, eException, "Unable to process API HTTP response - operation timed out.");

				goto Cleanup;
			}
			catch (Exception eException)
			{
				if (eException.InnerException != null)
					Logging.WriteDebugLogError("Que.GetAirConditionerFullStatus()", lRequestId, eException.InnerException, "Unable to process API HTTP response. Is the serial number correct?");
				else
					Logging.WriteDebugLogError("Que.GetAirConditionerFullStatus()", lRequestId, eException, "Unable to process API HTTP response. Is the serial number correct?");

				goto Cleanup;
			}

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();
			

			return updateItems;
		}

		private static void ProcessFullStatus(long lRequestId, AirConditionerUnit unit, dynamic jsonResponse)
		{
			JArray aEnabledZones;

			Logging.WriteDebugLog("Que.ProcessFullStatus() [0x{0}] Unit: {1}", lRequestId.ToString("X8"), unit.Serial);
		
			// Compressor Mode
			ProcessPartialStatus(lRequestId, "LiveAircon.CompressorMode", jsonResponse.LiveAircon.CompressorMode?.ToString(), ref unit.Data.CompressorState);

			// Compressor Capacity
			ProcessPartialStatus(lRequestId, "LiveAircon.CompressorCapacity", jsonResponse.LiveAircon.CompressorCapacity?.ToString(), ref unit.Data.CompressorCapacity);

			// Compressor Power
			if (jsonResponse.LiveAircon.ContainsKey("OutdoorUnit"))
				ProcessPartialStatus(lRequestId, "LiveAircon.OutdoorUnit.CompPower", jsonResponse.LiveAircon.OutdoorUnit.CompPower?.ToString(), ref unit.Data.CompressorPower);

			// On
			ProcessPartialStatus(lRequestId, "UserAirconSettings.isOn", jsonResponse.UserAirconSettings.isOn?.ToString(), ref unit.Data.On);

			// Mode
			ProcessPartialStatus(lRequestId, "UserAirconSettings.Mode", jsonResponse.UserAirconSettings.Mode?.ToString(), ref unit.Data.Mode);

			// Fan Mode
			ProcessPartialStatus(lRequestId, "UserAirconSettings.FanMode", jsonResponse.UserAirconSettings.FanMode?.ToString(), ref unit.Data.FanMode);

			// Set Cooling Temperature
			ProcessPartialStatus(lRequestId, "UserAirconSettings.TemperatureSetpoint_Cool_oC", jsonResponse.UserAirconSettings.TemperatureSetpoint_Cool_oC?.ToString(), ref unit.Data.SetTemperatureCooling);

			// Set Heating Temperature
			ProcessPartialStatus(lRequestId, "UserAirconSettings.TemperatureSetpoint_Heat_oC", jsonResponse.UserAirconSettings.TemperatureSetpoint_Heat_oC?.ToString(), ref unit.Data.SetTemperatureHeating);

			// Control All Zones
			ProcessPartialStatus(lRequestId, "MasterInfo.ControlAllZones", jsonResponse.MasterInfo.ControlAllZones?.ToString(), ref unit.Data.ControlAllZones);

			// Live Temperature
			ProcessPartialStatus(lRequestId, "MasterInfo.LiveTemp_oC", jsonResponse.MasterInfo.LiveTemp_oC?.ToString(), ref unit.Data.Temperature);

			// Live Temperature Outside
			ProcessPartialStatus(lRequestId, "MasterInfo.LiveOutdoorTemp_oC", jsonResponse.MasterInfo.LiveOutdoorTemp_oC?.ToString(), ref unit.Data.OutdoorTemperature);

			// Live Humidity
			ProcessPartialStatus(lRequestId, "MasterInfo.LiveHumidity_pc", jsonResponse.MasterInfo.LiveHumidity_pc?.ToString(), ref unit.Data.Humidity);

			// Coil Inlet Temperature
			ProcessPartialStatus(lRequestId, "LiveAircon.CoilInlet", jsonResponse.LiveAircon.CoilInlet?.ToString(), ref unit.Data.CoilInletTemperature);

			// Fan PWM
			ProcessPartialStatus(lRequestId, "LiveAircon.FanPWM", jsonResponse.LiveAircon.FanPWM?.ToString(), ref unit.Data.FanPWM);

			// Fan RPM
			ProcessPartialStatus(lRequestId, "LiveAircon.FanRPM", jsonResponse.LiveAircon.FanRPM?.ToString(), ref unit.Data.FanRPM);

			// Zones
			aEnabledZones = jsonResponse.UserAirconSettings.EnabledZones;
			if (aEnabledZones.Count != 8)
				Logging.WriteDebugLog("Que.GetAirConditionerFullStatus() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.EnabledZones");
			else
			{
				for (int iZoneIndex = 0; iZoneIndex < unit.Zones.Count; iZoneIndex++)
				{
					if (unit.Zones[iZoneIndex + 1].Exists)
					{
						// Enabled
						ProcessPartialStatus(lRequestId, "UserAirconSettings.EnabledZones", aEnabledZones[iZoneIndex].ToString(), ref unit.Zones[iZoneIndex + 1].State);

						// Temperature
						ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].LiveTemp_oC", iZoneIndex), jsonResponse.RemoteZoneInfo[iZoneIndex].LiveTemp_oC?.ToString(), ref unit.Zones[iZoneIndex + 1].Temperature);

						// Cooling Set Temperature
						ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Cool_oC", iZoneIndex), jsonResponse.RemoteZoneInfo[iZoneIndex].TemperatureSetpoint_Cool_oC?.ToString(), ref unit.Zones[iZoneIndex + 1].SetTemperatureCooling);

						// Heating Set Temperature
						ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Heat_oC", iZoneIndex), jsonResponse.RemoteZoneInfo[iZoneIndex].TemperatureSetpoint_Heat_oC?.ToString(), ref unit.Zones[iZoneIndex + 1].SetTemperatureHeating);

						// Position
						ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].ZonePosition", iZoneIndex), jsonResponse.RemoteZoneInfo[iZoneIndex].ZonePosition?.ToString(), ref unit.Zones[iZoneIndex + 1].Position);

						// Zone Sensors Temperature
						if (jsonResponse.RemoteZoneInfo[iZoneIndex].ContainsKey("RemoteTemperatures_oC") & _strSystemType == "que")
						{
							foreach (JProperty sensor in jsonResponse.RemoteZoneInfo[iZoneIndex].RemoteTemperatures_oC)
							{
								if (unit.Zones[iZoneIndex + 1].Sensors.ContainsKey(sensor.Name))
								{
									ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].RemoteTemperatures_oC.{1}", iZoneIndex, sensor.Name), jsonResponse.RemoteZoneInfo[iZoneIndex].RemoteTemperatures_oC[sensor.Name]?.ToString(), ref unit.Zones[iZoneIndex + 1].Sensors[sensor.Name].Temperature);
								}
							}
						}

						// Zone Sensors Battery
						if (jsonResponse.RemoteZoneInfo[iZoneIndex].ContainsKey("Sensors") & _strSystemType == "que")
						{
							foreach (JProperty sensor in jsonResponse.RemoteZoneInfo[iZoneIndex].Sensors)
							{
								if (unit.Zones[iZoneIndex + 1].Sensors.ContainsKey(sensor.Name))
								{
									ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].Sensors.{1}.Battery_pc", iZoneIndex, sensor.Name), jsonResponse.RemoteZoneInfo[iZoneIndex].Sensors[sensor.Name].Battery_pc?.ToString(), ref unit.Zones[iZoneIndex + 1].Sensors[sensor.Name].Battery);
								}
							}
						}
					}
				}
			}
		}

		private static void ProcessPartialStatus(long lRequestId, string strName, string strValue, ref double dblTarget)
		{
			double dblTemp = 0.0;

			Logging.WriteDebugLog("Que.ProcessPartialStatus() [0x{0}] Change: {1}", lRequestId.ToString("X8"), strName);

			if (!double.TryParse(strValue ?? "", out dblTemp))
				Logging.WriteDebugLog("Que.ProcessPartialStatus() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), strName);
			else
			{
				lock (_oLockData)
				{
					dblTarget = dblTemp;
				}
			}
		}

		private static void ProcessPartialStatus(long lRequestId, string strName, string strValue, ref string strTarget)
		{
			Logging.WriteDebugLog("Que.ProcessPartialStatus() [0x{0}] Change: {1}", lRequestId.ToString("X8"), strName);

			if ((strValue ?? "") == "")
				Logging.WriteDebugLog("Que.ProcessPartialStatus() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), strName);
			else
			{
				lock (_oLockData)
				{
					strTarget = strValue;
				}
			}
		}

		private static void ProcessPartialStatus(long lRequestId, string strName, string strValue, ref bool bTarget)
		{
			bool bTemp;

			Logging.WriteDebugLog("Que.ProcessPartialStatus() [0x{0}] Change: {1}", lRequestId.ToString("X8"), strName);

			if (!bool.TryParse(strValue ?? "", out bTemp))
				Logging.WriteDebugLog("Que.ProcessPartialStatus() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), strName);
			else
			{
				lock (_oLockData)
				{
					bTarget = bTemp;
				}
			}
		}

		private async static Task<UpdateItems> GetAirConditionerEvents(AirConditionerUnit unit)
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL, strPageURLFirstEvent = "api/v0/client/ac-systems/events/latest?serial=";
			string strResponse;
			dynamic jsonResponse;
			bool bRetVal = true;
			string strEventType;
			int iIndex;
			UpdateItems updateItems = UpdateItems.None;

			if (unit.NextEventURL == "")
				strPageURL = strPageURLFirstEvent + unit.Serial;
			else
				strPageURL = unit.NextEventURL;

			Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unit: {1}, Base: {2}{3}", lRequestId.ToString("X8"), unit.Serial, _httpClient.BaseAddress, strPageURL);

			if (!IsTokenValid())
			{
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

					Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Responded (Encoding {1}, {2} bytes)", lRequestId.ToString("X8"), httpResponse.Content.Headers.ContentEncoding.ToString() == "" ? "N/A" : httpResponse.Content.Headers.ContentEncoding.ToString(), (httpResponse.Content.Headers.ContentLength ?? 0) == 0 ? "N/A" : httpResponse.Content.Headers.ContentLength.ToString());

					if (Service.IsDevelopment)
						Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Response: {1}", lRequestId.ToString("X8"), strResponse);

					lock (_oLockData)
					{
						unit.Data.LastUpdated = DateTime.Now;
					}

					strResponse = strResponse.Replace("ac-newer-events", "acnewerevents");

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					unit.NextEventURL = jsonResponse._links.acnewerevents.href;
					if (unit.NextEventURL.StartsWith("/"))
						unit.NextEventURL = unit.NextEventURL.Substring(1);

					Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Next Event URL: {1}", lRequestId.ToString("X8"), unit.NextEventURL);

					Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Procesing {1} events", lRequestId.ToString("X8"), jsonResponse.events.Count);

					for (int iEvent = jsonResponse.events.Count - 1; iEvent >= 0; iEvent--)
					{
						// Events Received Flag
						_bEventsReceived = true;

						strEventType = jsonResponse.events[iEvent].type;

						Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Event Type: {1}", lRequestId.ToString("X8"), strEventType);

						switch (strEventType)
						{
							case "cmd-acked":
								// Clear Command Pending Flag
								if (_bCommandAckPending)
									_bCommandAckPending = false;

								break;

							case "status-change-broadcast":
								foreach (JProperty change in jsonResponse.events[iEvent].data)
								{
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Incremental Update: {1}", lRequestId.ToString("X8"), change.Name);

									// Compressor Mode
									if (change.Name == "LiveAircon.CompressorMode")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.CompressorState);
										updateItems |= UpdateItems.Main;
									}
									// Compressor Capacity
									else if (change.Name == "LiveAircon.CompressorCapacity")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.CompressorCapacity);
										updateItems |= UpdateItems.Main;
									}
									// Compressor Power
									else if (change.Name == "LiveAircon.OutdoorUnit.CompPower")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.CompressorPower);
										updateItems |= UpdateItems.Main;
									}
									// Mode
									else if (change.Name == "UserAirconSettings.Mode")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.Mode);
										updateItems |= UpdateItems.Main;
									}
									// Fan Mode
									else if (change.Name == "UserAirconSettings.FanMode")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.FanMode);
										updateItems |= UpdateItems.Main;
									}
									// On
									else if (change.Name == "UserAirconSettings.isOn")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.On);
										updateItems |= UpdateItems.Main;
									}
									// Control All Zones
									else if (change.Name == "MasterInfo.ControlAllZones")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.ControlAllZones);
										updateItems |= UpdateItems.Main;
									}
									// Live Temperature
									else if (change.Name == "MasterInfo.LiveTemp_oC")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.Temperature);
										updateItems |= UpdateItems.Main;
									}
									// Live Temperature Outside
									else if (change.Name == "MasterInfo.LiveOutdoorTemp_oC")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.OutdoorTemperature);
										updateItems |= UpdateItems.Main;
									}
									// Live Humidity
									else if (change.Name == "MasterInfo.LiveHumidity_pc")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.Humidity);
										updateItems |= UpdateItems.Main;
									}
									// Set Temperature Cooling
									else if (change.Name == "UserAirconSettings.TemperatureSetpoint_Cool_oC")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.SetTemperatureCooling);
										updateItems |= UpdateItems.Main;
									}
									// Set Temperature Heating
									else if (change.Name == "UserAirconSettings.TemperatureSetpoint_Heat_oC")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.SetTemperatureHeating);
										updateItems |= UpdateItems.Main;
									}
									// Coil Inlet Temperature
									else if (change.Name == "LiveAircon.CoilInlet")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.CoilInletTemperature);
										updateItems |= UpdateItems.Main;
									}
									// Fan PWM
									else if (change.Name == "LiveAircon.FanPWM")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.FanPWM);
										updateItems |= UpdateItems.Main;
									}
									// Fan RPM
									else if (change.Name == "LiveAircon.FanRPM")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Data.FanRPM);
										updateItems |= UpdateItems.Main;
									}
									// Remote Zone
									else if (change.Name.StartsWith("RemoteZoneInfo["))
									{
										iIndex = int.Parse(change.Name.Substring(change.Name.IndexOf("[") + 1, 1));

										if (unit.Zones.ContainsKey(iIndex + 1))
										{
											updateItems |= (UpdateItems)Math.Pow(2, iIndex + 1);

											// Live Temperature
											if (change.Name.EndsWith("].LiveTemp_oC"))
												ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Zones[iIndex + 1].Temperature);
											// Cooling Set Temperature
											else if (change.Name.EndsWith("].TemperatureSetpoint_Cool_oC"))
												ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Zones[iIndex + 1].SetTemperatureCooling);
											// Heating Set Temperature
											else if (change.Name.EndsWith("].TemperatureSetpoint_Heat_oC"))
												ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Zones[iIndex + 1].SetTemperatureHeating);
											// Zone Position
											else if (change.Name.EndsWith("].ZonePosition"))
												ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Zones[iIndex + 1].Position);
										}
									}
									// Enabled Zone
									else if (change.Name.StartsWith("UserAirconSettings.EnabledZones["))
									{
										iIndex = int.Parse(change.Name.Substring(change.Name.IndexOf("[") + 1, 1));

										if (unit.Zones.ContainsKey(iIndex + 1))
										{
											ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref unit.Zones[iIndex + 1].State);
											updateItems |= UpdateItems.Main;
											updateItems |= (UpdateItems)Math.Pow(2, iIndex + 1);
										}
									}
								}

								break;

							case "full-status-broadcast":
								ProcessFullStatus(lRequestId, unit, jsonResponse.events[iEvent].data);

								updateItems |= UpdateItems.Main | UpdateItems.Zone1 | UpdateItems.Zone2 | UpdateItems.Zone3 | UpdateItems.Zone4 | UpdateItems.Zone5 | UpdateItems.Zone6 | UpdateItems.Zone7 | UpdateItems.Zone8;

								break;
						}
					}
				}
				else
				{
					if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
						Logging.WriteDebugLogError("Que.GetAirConditionerEvents()", lRequestId, "Unable to process API response: {0}/{1} - check the Que Serial number.", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);
					else if (httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
					{
						Logging.WriteDebugLogError("Que.GetAirConditionerEvents()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

						_eventAuthenticationFailure.Set();
					}
					else
						Logging.WriteDebugLogError("Que.GetAirConditionerEvents()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

					bRetVal = false;
					goto Cleanup;
				}
			}
			catch (OperationCanceledException eException)
			{
				Logging.WriteDebugLogError("Que.GetAirConditionerEvents()", lRequestId, eException, "Unable to process API HTTP response - operation timed out.");

				bRetVal = false;
				goto Cleanup;
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
				unit.NextEventURL = "";			

			return updateItems;
		}

		private async static void AirConditionerMonitor()
		{
			WaitHandle[] waitHandles = new WaitHandle[] { _eventStop, _eventUpdate };
			int iWaitHandle = 0, iWaitInterval = 5, iCommandAckRetries = 0;
			bool bExit = false;
			UpdateItems updateItems = UpdateItems.None;

			Logging.WriteDebugLog("Que.AirConditionerMonitor()");

			while (!bExit)
			{
				updateItems = UpdateItems.None;

				iWaitHandle = WaitHandle.WaitAny(waitHandles, TimeSpan.FromSeconds(iWaitInterval));

				switch (iWaitHandle)
				{
					case 0: // Stop
						bExit = true;

						break;

					case 1: // Pull Update
						Logging.WriteDebugLog("Que.AirConditionerMonitor() Quick Update");

						// Normal Mode
						if (!_bNeoNoEventMode)
						{
							_bCommandAckPending = true;
							iCommandAckRetries = _iCommandAckRetryCounter;

							Thread.Sleep(_iPostCommandSleepTimer * 1000);

							foreach (AirConditionerUnit unit in _airConditionerUnits.Values)
							{
								updateItems = await GetAirConditionerEvents(unit);
								if (updateItems != UpdateItems.None)
								{
									MQTTUpdateData(unit, updateItems);
									MQTT.Update(null);
								}
							}
						}
						// Neo No Events Mode
						else
						{
							Thread.Sleep(_iPostCommandSleepTimerNeoNoEventsMode * 1000);

							foreach (AirConditionerUnit unit in _airConditionerUnits.Values)
							{
								updateItems = await GetAirConditionerFullStatus(unit);
								if (updateItems != UpdateItems.None)
								{
									MQTTUpdateData(unit, updateItems);
									MQTT.Update(null);
								}
							}
						}

						break;

					case WaitHandle.WaitTimeout: // Wait Timeout
						if (_airConditionerUnits.Count == 0)
							if (!await GetAirConditionerSerial())
								continue;

						if (_iZoneCount == 0)
						{
							if (!await GetAirConditionerZones())
								continue;
							else
								MQTTRegister();
						}

						// Normal Mode
						if (!_bNeoNoEventMode)
						{
							foreach (AirConditionerUnit unit in _airConditionerUnits.Values)
							{
								updateItems = await GetAirConditionerEvents(unit);
								if (_bEventsReceived)
								{
									if (updateItems != UpdateItems.None)
									{
										MQTTUpdateData(unit, updateItems);
										MQTT.Update(null);
									}
								}
								else if (_strSystemType == "neo")
								{
									Logging.WriteDebugLog("Que.AirConditionerMonitor() No Neo Events Received - Switching to Full Status Polling");
									_bNeoNoEventMode = true;
								}
							}
						}

						// Neo No Events Mode
						if (_bNeoNoEventMode)
						{
							foreach (AirConditionerUnit unit in _airConditionerUnits.Values)
							{
								updateItems = await GetAirConditionerFullStatus(unit);
								if (updateItems != UpdateItems.None)
								{
									MQTTUpdateData(unit, updateItems);
									MQTT.Update(null);
								}
							}
						}

						break;
				}

				if (_bCommandAckPending && iCommandAckRetries > 0)
				{
					iWaitInterval = _iPollIntervalUpdate;

					if (iCommandAckRetries-- == 0)
					{
						Logging.WriteDebugLog("Que.AirConditionerMonitor() Clearing Update Flag");
						_bCommandAckPending = false;
					}
				}
				else if (!_bCommandAckPending && iCommandAckRetries > 0)
				{
					Logging.WriteDebugLog("Que.AirConditionerMonitor() Post Command Update");
					iWaitInterval = _iPollIntervalUpdate;
					iCommandAckRetries = 0;
				}
				else
					iWaitInterval = (!_bNeoNoEventMode ? _iPollInterval : _iPollIntervalNeoNoEventsMode);
			}

			Logging.WriteDebugLog("Que.AirConditionerMonitor() Complete");
		}

		private async static void QueueMonitor()
		{
			WaitHandle[] waitHandles = new WaitHandle[] { _eventStop, _eventQueue };
			int iWaitHandle = 0;
			bool bExit = false;

			Logging.WriteDebugLog("Que.QueueMonitor()");

			while (!bExit)
			{
				iWaitHandle = WaitHandle.WaitAny(waitHandles, TimeSpan.FromSeconds(_iQueueInterval));

				switch (iWaitHandle)
				{
					case 0: // Stop
						bExit = true;

						break;

					case 1: // Queue Updated
						if (!IsTokenValid())
							continue;

						if (await ProcessQueue())
							_eventUpdate.Set();

						break;

					case WaitHandle.WaitTimeout: // Wait Timeout
						if (!IsTokenValid())
							continue;

						if (await ProcessQueue())
							_eventUpdate.Set();

						break;
				}
			}

			Logging.WriteDebugLog("Que.QueueMonitor() Complete");
		}

		private static async Task<bool> ProcessQueue()
		{
			QueueCommand command;
			bool bRetVal = false;

			Logging.WriteDebugLog("Que.ProcessQueue()");

			while (true)
			{
				lock (_oLockQueue)
				{
					if (_queueCommands.Count > 0)
					{
						command = _queueCommands.Peek();
						Logging.WriteDebugLog("Que.ProcessQueue() Attempting Command: 0x{0}", command.RequestId.ToString("X8"));

						if (command.Expires <= DateTime.Now)
						{
							Logging.WriteDebugLog("Que.ProcessQueue() Command Expired: 0x{0}", command.RequestId.ToString("X8"));
							_queueCommands.Dequeue();
							continue;
						}
					}
					else
						command = null;
				}

				if (command == null)
					break;

				if (await SendCommand(command))
				{
					lock (_oLockQueue)
					{
						Logging.WriteDebugLog("Que.ProcessQueue() Command Complete: 0x{0}", command.RequestId.ToString("X8"));
						_queueCommands.Dequeue();

						bRetVal = true;
					}
				}
			}

			Logging.WriteDebugLog("Que.ProcessQueue() Complete");

			return bRetVal;
		}

		private static bool IsTokenValid()
		{
			if (_queToken != null && _queToken.TokenExpires > DateTime.Now)
				return true;
			else
				return false;
		}

		private static void MQTTRegister()
		{
			AirConditionerZone zone;
			int iDeviceIndex = 0;
			string strHANameModifier = "", strDeviceNameModifier = "";
			string strAirConditionerName = "", strAirConditionerNameMQTT = "";

			Logging.WriteDebugLog("Que.MQTTRegister()");

			foreach (AirConditionerUnit unit in _airConditionerUnits.Values)
			{
				Logging.WriteDebugLog("Que.MQTTRegister() Registering Unit: {0}", unit.Serial);

				if (iDeviceIndex == 0)
				{
					strHANameModifier = "";
					strDeviceNameModifier = unit.Serial;
				}
				else
				{
					strHANameModifier = iDeviceIndex.ToString();
					strDeviceNameModifier = unit.Serial;
				}

				strAirConditionerName = string.Format("{0} ({1})", _strAirConditionerName, unit.Name);
				strAirConditionerNameMQTT = string.Format("{0} ({1})", Service.DeviceNameMQTT, unit.Name);

				if (!_bSeparateHeatCool) // Default
					MQTT.SendMessage(string.Format("homeassistant/climate/actronque{0}/config", strHANameModifier), "{{\"name\":\"{1}\",\"unique_id\":\"{0}-AC\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"modes\":[\"off\",\"auto\",\"cool\",\"fan_only\",\"heat\"],\"fan_modes\":[\"high\",\"medium\",\"low\",\"auto\"],\"mode_command_topic\":\"actronque{3}/mode/set\",\"temperature_command_topic\":\"actronque{3}/temperature/set\",\"fan_mode_command_topic\":\"actronque{3}/fan/set\",\"min_temp\":\"12\",\"max_temp\":\"30\",\"temp_step\":\"0.5\",\"fan_mode_state_topic\":\"actronque{3}/fanmode\",\"action_topic\":\"actronque{3}/compressor\",\"temperature_state_topic\":\"actronque{3}/settemperature\",\"mode_state_topic\":\"actronque{3}/mode\",\"current_temperature_topic\":\"actronque{3}/temperature\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial, unit.Name);
				else
					MQTT.SendMessage(string.Format("homeassistant/climate/actronque{0}/config", strHANameModifier), "{{\"name\":\"{1}\",\"unique_id\":\"{0}-AC\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"modes\":[\"off\",\"auto\",\"cool\",\"fan_only\",\"heat\"],\"fan_modes\":[\"high\",\"medium\",\"low\",\"auto\"],\"mode_command_topic\":\"actronque{3}/mode/set\",\"temperature_high_command_topic\":\"actronque{3}/temperature/high/set\",\"temperature_low_command_topic\":\"actronque{3}/temperature/low/set\",\"fan_mode_command_topic\":\"actronque{3}/fan/set\",\"min_temp\":\"12\",\"max_temp\":\"30\",\"temp_step\":\"0.5\",\"fan_mode_state_topic\":\"actronque{3}/fanmode\",\"action_topic\":\"actronque{3}/compressor\",\"temperature_high_state_topic\":\"actronque{3}/settemperature/high\",\"temperature_low_state_topic\":\"actronque{3}/settemperature/low\",\"mode_state_topic\":\"actronque{3}/mode\",\"current_temperature_topic\":\"actronque{3}/temperature\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial, unit.Name);

				MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}humidity/config", strHANameModifier), "{{\"name\":\"{1} Humidity\",\"unique_id\":\"{0}-Humidity\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{3}/humidity\",\"unit_of_measurement\":\"%\",\"device_class\":\"humidity\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial);

				if (_strSystemType == "que")
				{
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}compressorcapacity/config", strHANameModifier), "{{\"name\":\"{1} Compressor Capacity\",\"unique_id\":\"{0}-CompressorCapacity\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{3}/compressorcapacity\",\"unit_of_measurement\":\"%\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial);
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}compressorpower/config", strHANameModifier), "{{\"name\":\"{1} Compressor Power\",\"unique_id\":\"{0}-CompressorPower\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{3}/compressorpower\",\"unit_of_measurement\":\"W\",\"device_class\":\"power\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial);
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}outdoortemperature/config", strHANameModifier), "{{\"name\":\"{1} Outdoor Temperature\",\"unique_id\":\"{0}-OutdoorTemperature\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{3}/outdoortemperature\",\"unit_of_measurement\":\"\u00B0C\",\"device_class\":\"temperature\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial);
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}coilinlettemperature/config", strHANameModifier), "{{\"name\":\"{1} Coil Inlet Temperature\",\"unique_id\":\"{0}-CoilInletTemperature\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{3}/coilinlettemperature\",\"unit_of_measurement\":\"\u00B0C\",\"device_class\":\"temperature\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial);
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}fanpwm/config", strHANameModifier), "{{\"name\":\"{1} Fan PWM\",\"unique_id\":\"{0}-FanPWM\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{3}/fanpwm\",\"unit_of_measurement\":\"%\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial);
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}fanrpm/config", strHANameModifier), "{{\"name\":\"{1} Fan RPM\",\"unique_id\":\"{0}-FanRPM\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{3}/fanrpm\",\"unit_of_measurement\":\"RPM\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial);
					MQTT.SendMessage(string.Format("homeassistant/switch/actronque{0}/controlallzones/config", strHANameModifier), "{{\"name\":\"Control All Zones\",\"unique_id\":\"{0}-CAZ\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{3}/controlallzones\",\"command_topic\":\"actronque{3}/controlallzones/set\",\"payload_on\":\"ON\",\"payload_off\":\"OFF\",\"state_on\":\"ON\",\"state_off\":\"OFF\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial);

					MQTT.Subscribe("actronque{0}/controlallzones/set", unit.Serial);
				}
				else
				{
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}compressorcapacity/config", strHANameModifier), "");
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}compressorpower/config", strHANameModifier), "");
					MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}outdoortemperature/config", strHANameModifier), "");
				}

				foreach (int iZone in unit.Zones.Keys)
				{
					zone = unit.Zones[iZone];

					// Zone
					if (zone.Exists)
					{
						MQTT.SendMessage(string.Format("homeassistant/switch/actronque{0}/airconzone{1}/config", strHANameModifier, iZone), "{{\"name\":\"{0} Zone\",\"unique_id\":\"{2}-z{1}s\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{3}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{4}/zone{1}\",\"command_topic\":\"actronque{4}/zone{1}/set\",\"payload_on\":\"ON\",\"payload_off\":\"OFF\",\"state_on\":\"ON\",\"state_off\":\"OFF\",\"availability_topic\":\"{2}/status\"}}", zone.Name, iZone, Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerNameMQTT, unit.Serial);
						MQTT.Subscribe("actronque{0}/zone{1}/set", unit.Serial, iZone);

						MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}/airconzone{1}/config", strHANameModifier, iZone), "{{\"name\":\"{0}\",\"unique_id\":\"{2}-z{1}t\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{3}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{4}/zone{1}/temperature\",\"unit_of_measurement\":\"\u00B0C\",\"availability_topic\":\"{2}/status\"}}", zone.Name, iZone, Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerNameMQTT, unit.Serial);
						MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}/airconzone{1}position/config", strHANameModifier, iZone), "{{\"name\":\"{0} Position\",\"unique_id\":\"{2}-z{1}p\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{3}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{4}/zone{1}/position\",\"unit_of_measurement\":\"%\",\"availability_topic\":\"{2}/status\"}}", zone.Name, iZone, Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerNameMQTT, unit.Serial);

						// Per Zone Controls
						if (_bPerZoneControls)
						{
							if (!_bSeparateHeatCool) // Default
								MQTT.SendMessage(string.Format("homeassistant/climate/actronque{0}/zone{1}/config", strHANameModifier, iZone), "{{\"name\":\"{0} {3}\",\"unique_id\":\"{2}-z{1}ac\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{4}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"modes\":[\"off\",\"auto\",\"cool\",\"fan_only\",\"heat\"],\"mode_command_topic\":\"actronque{5}/zone{1}/mode/set\",\"temperature_command_topic\":\"actronque{5}/zone{1}/temperature/set\",\"min_temp\":\"12\",\"max_temp\":\"30\",\"temp_step\":\"0.5\",\"temperature_state_topic\":\"actronque{5}/zone{1}/settemperature\",\"mode_state_topic\":\"actronque{5}/zone{1}/mode\",\"current_temperature_topic\":\"actronque{5}/zone{1}/temperature\",\"availability_topic\":\"{2}/status\"}}", zone.Name, iZone, Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial);
							else
								MQTT.SendMessage(string.Format("homeassistant/climate/actronque{0}/zone{1}/config", strHANameModifier, iZone), "{{\"name\":\"{0} {3}\",\"unique_id\":\"{2}-z{1}ac\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{4}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"modes\":[\"off\",\"auto\",\"cool\",\"fan_only\",\"heat\"],\"mode_command_topic\":\"actronque{5}/zone{1}/mode/set\",\"temperature_high_command_topic\":\"actronque{5}/zone{1}/temperature/high/set\",\"temperature_low_command_topic\":\"actronque{5}/zone{1}/temperature/low/set\",\"min_temp\":\"12\",\"max_temp\":\"30\",\"temp_step\":\"0.5\",\"temperature_high_state_topic\":\"actronque{5}/zone{1}/settemperature/high\",\"temperature_low_state_topic\":\"actronque{5}/zone{1}/settemperature/low\",\"mode_state_topic\":\"actronque{5}/zone{1}/mode\",\"current_temperature_topic\":\"actronque{5}/zone{1}/temperature\",\"availability_topic\":\"{2}/status\"}}", zone.Name, iZone, Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, unit.Serial);

							MQTT.Subscribe("actronque{0}/zone{1}/temperature/set", unit.Serial, iZone);
							MQTT.Subscribe("actronque{0}/zone{1}/temperature/high/set", unit.Serial, iZone);
							MQTT.Subscribe("actronque{0}/zone{1}/temperature/low/set", unit.Serial, iZone);
							MQTT.Subscribe("actronque{0}/zone{1}/mode/set", unit.Serial, iZone);

							foreach (string sensor in zone.Sensors.Keys)
							{
								MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}/zone{1}sensor{2}battery/config", strHANameModifier, iZone, sensor), "{{\"name\":\"{0} Battery\",\"unique_id\":\"{2}-z{1}s{5}battery\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{4}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{6}/zone{1}sensor{5}/battery\",\"state_class\":\"measurement\",\"unit_of_measurement\":\"%\",\"device_class\":\"battery\",\"availability_topic\":\"{2}/status\"}}", zone.Sensors[sensor].Name, iZone, Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, sensor, unit.Serial);
							}
						}
						else
						{
							MQTT.SendMessage(string.Format("homeassistant/climate/actronque{0}/zone{1}/config", strHANameModifier, iZone), "");
						}

						// Per Zone Sensors
						if (_bPerZoneSensors && _strSystemType == "que")
						{
							foreach (string sensor in zone.Sensors.Keys)
							{
								MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}/zone{1}sensor{2}temperature/config", strHANameModifier, iZone, sensor), "{{\"name\":\"{0} Temperature\",\"unique_id\":\"{2}-z{1}s{5}temperature\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{4}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{6}/zone{1}sensor{5}/temperature\",\"device_class\":\"temperature\",\"unit_of_measurement\":\"\u00B0C\",\"availability_topic\":\"{2}/status\"}}", zone.Sensors[sensor].Name, iZone, Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, sensor, unit.Serial);
								MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}/zone{1}sensor{2}battery/config", strHANameModifier, iZone, sensor), "{{\"name\":\"{0} Battery\",\"unique_id\":\"{2}-z{1}s{5}battery\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{4}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque{6}/zone{1}sensor{5}/battery\",\"state_class\":\"measurement\",\"unit_of_measurement\":\"%\",\"device_class\":\"battery\",\"availability_topic\":\"{2}/status\"}}", zone.Sensors[sensor].Name, iZone, Service.ServiceName.ToLower() + strDeviceNameModifier, strAirConditionerName, strAirConditionerNameMQTT, sensor, unit.Serial);
							}
						}
						else if (_strSystemType == "que")
						{
							foreach (string sensor in zone.Sensors.Keys)
							{
								MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}/zone{1}sensor{2}temperature/config", strHANameModifier, iZone, sensor), "");
							}
						}

						// Clear Old Entities
						if (!_bPerZoneSensors && !_bPerZoneControls && _strSystemType == "que")
						{
							foreach (string sensor in zone.Sensors.Keys)
							{
								MQTT.SendMessage(string.Format("homeassistant/sensor/actronque{0}/zone{1}sensor{2}battery/config", strHANameModifier, iZone, sensor), "");
							}
						}
					}
				}

				MQTT.Subscribe("actronque{0}/mode/set", unit.Serial);
				MQTT.Subscribe("actronque{0}/fan/set", unit.Serial);
				MQTT.Subscribe("actronque{0}/temperature/set", unit.Serial);
				MQTT.Subscribe("actronque{0}/temperature/high/set", unit.Serial);
				MQTT.Subscribe("actronque{0}/temperature/low/set", unit.Serial);

				iDeviceIndex++;
			}		
		}

		private static void MQTTUpdateData(AirConditionerUnit unit, UpdateItems items)
		{
			Logging.WriteDebugLog("Que.MQTTUpdateData() Unit: {0}, Items: {1}", unit.Serial, items.ToString());

			if (unit.Data.LastUpdated == DateTime.MinValue)
			{
				Logging.WriteDebugLog("Que.MQTTUpdateData() Skipping update, No data received");
				return;
			}

			if (items.HasFlag(UpdateItems.Main))
			{
				// Fan Mode
				switch (unit.Data.FanMode)
				{
					case "AUTO":
						MQTT.SendMessage(string.Format("actronque{0}/fanmode", unit.Serial), "auto");
						break;

					case "AUTO+CONT":
						MQTT.SendMessage(string.Format("actronque{0}/fanmode", unit.Serial), "auto");
						break;

					case "LOW":
						MQTT.SendMessage(string.Format("actronque{0}/fanmode", unit.Serial), "low");
						break;

					case "LOW+CONT":
						MQTT.SendMessage(string.Format("actronque{0}/fanmode", unit.Serial), "low");
						break;

					case "MED":
						MQTT.SendMessage(string.Format("actronque{0}/fanmode", unit.Serial), "medium");
						break;

					case "MED+CONT":
						MQTT.SendMessage(string.Format("actronque{0}/fanmode", unit.Serial), "medium");
						break;

					case "HIGH":
						MQTT.SendMessage(string.Format("actronque{0}/fanmode", unit.Serial), "high");
						break;

					case "HIGH+CONT":
						MQTT.SendMessage(string.Format("actronque{0}/fanmode", unit.Serial), "high");
						break;

					default:
						Logging.WriteDebugLog("Que.MQTTUpdateData() Unexpected Fan Mode: {0}", unit.Data.FanMode);
						break;
				}

				// Temperature
				MQTT.SendMessage(string.Format("actronque{0}/temperature", unit.Serial), unit.Data.Temperature.ToString("N1"));

				if (_strSystemType == "que")
					MQTT.SendMessage(string.Format("actronque{0}/outdoortemperature", unit.Serial), unit.Data.OutdoorTemperature.ToString("N1"));

				// Humidity
				MQTT.SendMessage(string.Format("actronque{0}/humidity", unit.Serial), unit.Data.Humidity.ToString("N1"));

				// Power, Mode & Set Temperature
				if (!unit.Data.On)
				{
					MQTT.SendMessage(string.Format("actronque{0}/mode", unit.Serial), "off");
					MQTT.SendMessage(string.Format("actronque{0}/settemperature", unit.Serial), GetSetTemperature(unit.Data.SetTemperatureHeating, unit.Data.SetTemperatureCooling).ToString("N1"));
				}
				else
				{
					switch (unit.Data.Mode)
					{
						case "AUTO":
							MQTT.SendMessage(string.Format("actronque{0}/mode", unit.Serial), "auto");
							MQTT.SendMessage(string.Format("actronque{0}/settemperature", unit.Serial), GetSetTemperature(unit.Data.SetTemperatureHeating, unit.Data.SetTemperatureCooling).ToString("N1"));
							break;

						case "COOL":
							MQTT.SendMessage(string.Format("actronque{0}/mode", unit.Serial), "cool");
							MQTT.SendMessage(string.Format("actronque{0}/settemperature", unit.Serial), unit.Data.SetTemperatureCooling.ToString("N1"));
							break;

						case "HEAT":
							MQTT.SendMessage(string.Format("actronque{0}/mode", unit.Serial), "heat");
							MQTT.SendMessage(string.Format("actronque{0}/settemperature", unit.Serial), unit.Data.SetTemperatureHeating.ToString("N1"));
							break;

						case "FAN":
							MQTT.SendMessage(string.Format("actronque{0}/mode", unit.Serial), "fan_only");
							MQTT.SendMessage(string.Format("actronque{0}/settemperature", unit.Serial), "");
							break;

						default:
							Logging.WriteDebugLog("Que.MQTTUpdateData() Unexpected Mode: {0}", unit.Data.Mode);
							break;
					}
				}

				MQTT.SendMessage(string.Format("actronque{0}/settemperature/high", unit.Serial), unit.Data.SetTemperatureCooling.ToString("N1"));
				MQTT.SendMessage(string.Format("actronque{0}/settemperature/low", unit.Serial), unit.Data.SetTemperatureHeating.ToString("N1"));

				// Compressor
				switch (unit.Data.CompressorState)
				{
					case "HEAT":
						MQTT.SendMessage(string.Format("actronque{0}/compressor", unit.Serial), "heating");
						break;

					case "COOL":
						MQTT.SendMessage(string.Format("actronque{0}/compressor", unit.Serial), "cooling");
						break;

					case "OFF":
						MQTT.SendMessage(string.Format("actronque{0}/compressor", unit.Serial), "off");
						break;

					case "IDLE":
						if (unit.Data.On)
							MQTT.SendMessage(string.Format("actronque{0}/compressor", unit.Serial), "idle");
						else
							MQTT.SendMessage(string.Format("actronque{0}/compressor", unit.Serial), "off");

						break;

					default:
						Logging.WriteDebugLog("Que.MQTTUpdateData() Unexpected Compressor State: {0}", unit.Data.CompressorState);

						break;
				}

				if (_strSystemType == "que")
				{
					// Compressor Capacity
					MQTT.SendMessage(string.Format("actronque{0}/compressorcapacity", unit.Serial), unit.Data.CompressorCapacity.ToString("F1"));

					// Compressor Power
					MQTT.SendMessage(string.Format("actronque{0}/compressorpower", unit.Serial), unit.Data.CompressorPower.ToString("F2"));

					// Coil Inlet Temperature
					MQTT.SendMessage(string.Format("actronque{0}/coilinlettemperature", unit.Serial), unit.Data.CoilInletTemperature.ToString("F2"));

					// Fan PWM
					MQTT.SendMessage(string.Format("actronque{0}/fanpwm", unit.Serial), unit.Data.FanPWM.ToString("F0"));

					// Fan RPM
					MQTT.SendMessage(string.Format("actronque{0}/fanrpm", unit.Serial), unit.Data.FanRPM.ToString("F0"));

					// Control All Zones
					MQTT.SendMessage(string.Format("actronque{0}/controlallzones", unit.Serial), unit.Data.ControlAllZones ? "ON" : "OFF");
				}
			}

			// Zones
			foreach (int iIndex in unit.Zones.Keys)
			{
				if (unit.Zones[iIndex].Exists && items.HasFlag((UpdateItems)Math.Pow(2, iIndex)))
				{
					MQTT.SendMessage(string.Format("actronque{0}/zone{1}", unit.Serial, iIndex), unit.Zones[iIndex].State ? "ON" : "OFF");
					MQTT.SendMessage(string.Format("actronque{0}/zone{1}/temperature", unit.Serial, iIndex), unit.Zones[iIndex].Temperature.ToString("N1"));
					MQTT.SendMessage(string.Format("actronque{0}/zone{1}/position", unit.Serial, iIndex), (unit.Zones[iIndex].Position * 5).ToString()); // 0-20 numeric displayed as 0-100 percentage

					// Per Zone Controls
					if (_bPerZoneControls)
					{
						if (!unit.Data.On)
						{
							MQTT.SendMessage(string.Format("actronque{0}/zone{1}/mode", unit.Serial, iIndex), "off");
							MQTT.SendMessage(string.Format("actronque{0}/zone{1}/settemperature", unit.Serial, iIndex), GetSetTemperature(unit.Zones[iIndex].SetTemperatureHeating, unit.Zones[iIndex].SetTemperatureCooling).ToString("N1"));
						}
						else
						{
							switch (unit.Data.Mode)
							{
								case "AUTO":
									MQTT.SendMessage(string.Format("actronque{0}/zone{1}/mode", unit.Serial, iIndex), (unit.Zones[iIndex].State ? "auto" : "off"));
									MQTT.SendMessage(string.Format("actronque{0}/zone{1}/settemperature", unit.Serial, iIndex), GetSetTemperature(unit.Zones[iIndex].SetTemperatureHeating, unit.Zones[iIndex].SetTemperatureCooling).ToString("N1"));
									break;

								case "COOL":
									MQTT.SendMessage(string.Format("actronque{0}/zone{1}/mode", unit.Serial, iIndex), (unit.Zones[iIndex].State ? "cool" : "off"));
									MQTT.SendMessage(string.Format("actronque{0}/zone{1}/settemperature", unit.Serial, iIndex), unit.Zones[iIndex].SetTemperatureCooling.ToString("N1"));
									break;

								case "HEAT":
									MQTT.SendMessage(string.Format("actronque{0}/zone{1}/mode", unit.Serial, iIndex), (unit.Zones[iIndex].State ? "heat" : "off"));
									MQTT.SendMessage(string.Format("actronque{0}/zone{1}/settemperature", unit.Serial, iIndex), unit.Zones[iIndex].SetTemperatureHeating.ToString("N1"));
									break;

								case "FAN":
									MQTT.SendMessage(string.Format("actronque{0}/zone{1}/mode", unit.Serial, iIndex), (unit.Zones[iIndex].State ? "fan_only" : "off"));
									MQTT.SendMessage(string.Format("actronque{0}/zone{1}/settemperature", unit.Serial, iIndex), GetSetTemperature(unit.Zones[iIndex].SetTemperatureHeating, unit.Zones[iIndex].SetTemperatureCooling).ToString("N1"));
									break;

								default:
									Logging.WriteDebugLog("Que.MQTTUpdateData() Unexpected Mode: {0}", unit.Data.Mode);
									break;
							}
						}

						MQTT.SendMessage(string.Format("actronque{0}/zone{1}/settemperature/high", unit.Serial, iIndex), unit.Zones[iIndex].SetTemperatureCooling.ToString("N1"));
						MQTT.SendMessage(string.Format("actronque{0}/zone{1}/settemperature/low", unit.Serial, iIndex), unit.Zones[iIndex].SetTemperatureHeating.ToString("N1"));
					}

					// Per Zone Sensors
					if (_bPerZoneSensors && _strSystemType == "que")
					{
						foreach (AirConditionerSensor sensor in unit.Zones[iIndex].Sensors.Values)
						{
							MQTT.SendMessage(string.Format("actronque{0}/zone{1}sensor{2}/temperature", unit.Serial, iIndex, sensor.Serial), sensor.Temperature.ToString("N1"));
						}
					}

					// Per Zone Sensors/Controls
					if ((_bPerZoneSensors | _bPerZoneControls) && _strSystemType == "que")
					{
						foreach (AirConditionerSensor sensor in unit.Zones[iIndex].Sensors.Values)
						{
							MQTT.SendMessage(string.Format("actronque{0}/zone{1}sensor{2}/battery", unit.Serial, iIndex, sensor.Serial), sensor.Battery.ToString("N1"));
						}
					}
				}
			}
		}

		private static double GetSetTemperature(double dblHeating, double dblCooling)
		{
			double dblSetTemperature = 0.0;

			Logging.WriteDebugLog("Que.GetSetTemperature()");

			try
			{
				dblSetTemperature = dblHeating + ((dblCooling - dblHeating) / 2);

				dblSetTemperature = Math.Round(dblSetTemperature * 2, MidpointRounding.AwayFromZero) / 2;
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Que.GetSetTemperature()", eException, "Unable to determine set temperature mid-point.");
			}

			return dblSetTemperature;
		}

		private static void AddCommandToQueue(QueueCommand command)
		{
			Logging.WriteDebugLog("Que.AddCommandToQueue() [0x{0}]", command.RequestId.ToString("X8"));

			lock (_oLockQueue)
			{
				_queueCommands.Enqueue(command);

				_eventQueue.Set();
			}
		}

		public static void ChangeZone(long lRequestId, AirConditionerUnit unit, int iZone, bool bState)
		{
			bool[] bZones;
			QueueCommand command = new QueueCommand(lRequestId, unit, DateTime.Now.AddSeconds(_iCommandExpiry));

			Logging.WriteDebugLog("Que.ChangeZone() [0x{0}] Unit: {1}, Zone {2}: {3}", lRequestId.ToString("X8"), unit.Serial, iZone, bState ? "On" : "Off");

			command.Data.command.Add("type", "set-settings");

			switch (_strSystemType)
			{
				case "que":
					command.Data.command.Add(string.Format("UserAirconSettings.EnabledZones[{0}]", iZone - 1), bState);
					break;

				case "neo":
					bZones = new bool[] { false, false, false, false, false, false, false, false };

					if (_bNeoNoEventMode)
					{   // Temporarily set zone state to support subsequent zone changes before the next poll
						if (unit.Zones.ContainsKey(iZone))
							unit.Zones[iZone].State = bState;

						MQTT.SendMessage(string.Format("actronque{0}/zone{1}", unit.Serial, iZone), unit.Zones[iZone].State ? "ON" : "OFF");
					}

					for (int iIndex = 0; iIndex < bZones.Length; iIndex++)
					{
						if ((iIndex + 1) == iZone)
							bZones[iIndex] = bState;
						else
							bZones[iIndex] = (unit.Zones.ContainsKey(iIndex + 1) ? unit.Zones[iIndex + 1].State : false);
					}

					command.Data.command.Add("UserAirconSettings.EnabledZones", bZones);

					break;

				default:
					return;
			}

			AddCommandToQueue(command);
		}

		public static void ChangeControlAllZones(long lRequestId, AirConditionerUnit unit, bool bState)
		{
			QueueCommand command = new QueueCommand(lRequestId, unit, DateTime.Now.AddSeconds(_iCommandExpiry));

			Logging.WriteDebugLog("Que.ChangeControlAllZones() [0x{0}] Unit: {1}, Control All Zones: {2}", lRequestId.ToString("X8"), unit.Serial, bState ? "On" : "Off");

			command.Data.command.Add("type", "set-settings");

			switch (_strSystemType)
			{
				case "que":
					command.Data.command.Add(string.Format("MasterInfo.ControlAllZones"), bState);
					break;

				default:
					return;
			}

			AddCommandToQueue(command);
		}

		public static void ChangeMode(long lRequestId, AirConditionerUnit unit, AirConditionerMode mode)
		{
			QueueCommand command = new QueueCommand(lRequestId, unit, DateTime.Now.AddSeconds(_iCommandExpiry));

			Logging.WriteDebugLog("Que.ChangeMode() [0x{0}] Unit: {1}, Mode: {2}", lRequestId.ToString("X8"), unit.Serial, mode.ToString());

			switch (mode)
			{
				case AirConditionerMode.Off:
					command.Data.command.Add("UserAirconSettings.isOn", false);

					break;

				case AirConditionerMode.Automatic:
					command.Data.command.Add("UserAirconSettings.isOn", true);
					command.Data.command.Add("UserAirconSettings.Mode", "AUTO");

					break;

				case AirConditionerMode.Cool:
					command.Data.command.Add("UserAirconSettings.isOn", true);
					command.Data.command.Add("UserAirconSettings.Mode", "COOL");

					break;

				case AirConditionerMode.Fan_Only:
					command.Data.command.Add("UserAirconSettings.isOn", true);
					command.Data.command.Add("UserAirconSettings.Mode", "FAN");

					break;

				case AirConditionerMode.Heat:
					command.Data.command.Add("UserAirconSettings.isOn", true);
					command.Data.command.Add("UserAirconSettings.Mode", "HEAT");

					break;
			}

			command.Data.command.Add("type", "set-settings");

			AddCommandToQueue(command);
		}

		public static void ChangeFanMode(long lRequestId, AirConditionerUnit unit, FanMode fanMode)
		{
			QueueCommand command = new QueueCommand(lRequestId, unit,DateTime.Now.AddSeconds(_iCommandExpiry));

			Logging.WriteDebugLog("Que.ChangeFanMode() [0x{0}] Unit: {1}, Fan Mode: {2}", lRequestId.ToString("X8"), unit.Serial, fanMode.ToString());

			switch (fanMode)
			{
				case FanMode.Automatic:
					command.Data.command.Add("UserAirconSettings.FanMode", unit.Data.FanMode.EndsWith("CONT") ? "AUTO+CONT" : "AUTO");

					break;

				case FanMode.Low:
					command.Data.command.Add("UserAirconSettings.FanMode", unit.Data.FanMode.EndsWith("CONT") ? "LOW+CONT" : "LOW");

					break;

				case FanMode.Medium:
					command.Data.command.Add("UserAirconSettings.FanMode", unit.Data.FanMode.EndsWith("CONT") ? "MED+CONT" : "MED");

					break;

				case FanMode.High:
					command.Data.command.Add("UserAirconSettings.FanMode", unit.Data.FanMode.EndsWith("CONT") ? "HIGH+CONT" : "HIGH");

					break;
			}

			command.Data.command.Add("type", "set-settings");

			AddCommandToQueue(command);
		}

		public static void ChangeTemperature(long lRequestId, AirConditionerUnit unit, double dblTemperature, int iZone, TemperatureSetType setType)
		{
			string strCommandPrefix = "";
			QueueCommand command = new QueueCommand(lRequestId, unit, DateTime.Now.AddSeconds(_iCommandExpiry));

			Logging.WriteDebugLog("Que.ChangeTemperature() [0x{0}] Unit: {1}, Zone: {2}, Temperature ({4}): {3}", lRequestId.ToString("X8"), unit.Serial, iZone, dblTemperature, setType.ToString());

			if (iZone == 0)
				strCommandPrefix = "UserAirconSettings";
			else
				strCommandPrefix = string.Format("RemoteZoneInfo[{0}]", iZone - 1);

			switch (setType)
			{
				case TemperatureSetType.Default:
					switch (unit.Data.Mode)
					{
						case "OFF":
							return;

						case "FAN":
							return;

						case "COOL":
							command.Data.command.Add(string.Format("{0}.TemperatureSetpoint_Cool_oC", strCommandPrefix), dblTemperature);

							break;

						case "HEAT":
							command.Data.command.Add(string.Format("{0}.TemperatureSetpoint_Heat_oC", strCommandPrefix), dblTemperature);

							break;

						case "AUTO":
							command.Data.command.Add(string.Format("{0}.TemperatureSetpoint_Heat_oC", strCommandPrefix), dblTemperature);
							command.Data.command.Add(string.Format("{0}.TemperatureSetpoint_Cool_oC", strCommandPrefix), dblTemperature);

							break;
					}

					break;

				case TemperatureSetType.Low:
					command.Data.command.Add(string.Format("{0}.TemperatureSetpoint_Heat_oC", strCommandPrefix), dblTemperature);

					break;

				case TemperatureSetType.High:
					command.Data.command.Add(string.Format("{0}.TemperatureSetpoint_Cool_oC", strCommandPrefix), dblTemperature);

					break;							
			}	

			command.Data.command.Add("type", "set-settings");

			AddCommandToQueue(command);

			if (iZone == 0 && !unit.Data.ControlAllZones)
			{
				Logging.WriteDebugLog("Que.ChangeTemperature() [0x{0}] Unit: {1}, Setting Control All Zones to True due to Master temperature change", lRequestId.ToString("X8"), unit.Serial);

				ChangeControlAllZones(lRequestId, unit, true);
			}
		}

		private static async Task<bool> SendCommand(QueueCommand command)
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			StringContent content;
			long lRequestId = RequestManager.GetRequestId(command.RequestId);
			string strPageURL = "api/v0/client/ac-systems/cmds/send?serial=";
			bool bRetVal = true;

			Logging.WriteDebugLog("Que.SendCommand() [0x{0}] Base: {1}{2}{3}", lRequestId.ToString("X8"), _httpClient.BaseAddress, strPageURL, command.Unit.Serial);

			try
			{
				content = new StringContent(JsonConvert.SerializeObject(command.Data));

				content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

				Logging.WriteDebugLog("Que.SendCommand() [0x{0}] Content: {1}", lRequestId.ToString("X8"), await content.ReadAsStringAsync());
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Que.SendCommand()", lRequestId, eException, "Unable to serialize command.");

				bRetVal = false;
				goto Cleanup;
			}

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(TimeSpan.FromSeconds(_iCancellationTime));

				httpResponse = await _httpClientCommands.PostAsync(strPageURL + command.Unit.Serial, content, cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
					Logging.WriteDebugLog("Que.SendCommand() [0x{0}] Response {1}/{2}", lRequestId.ToString("X8"), httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);
				else
				{
					if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
						Logging.WriteDebugLogError("Que.SendCommand()", lRequestId, "Unable to process API response: {0}/{1} - check the Que Serial number.", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);
					else if (httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
					{
						Logging.WriteDebugLogError("Que.SendCommand()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

						_eventAuthenticationFailure.Set();
					}
					else
						Logging.WriteDebugLogError("Que.SendCommand()", lRequestId, "Unable to process API response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

					bRetVal = false;
					goto Cleanup;
				}
			}
			catch (OperationCanceledException eException)
			{
				Logging.WriteDebugLogError("Que.SendCommand()", lRequestId, eException, "Unable to process API HTTP response - operation timed out.");

				bRetVal = false;
				goto Cleanup;
			}
			catch (Exception eException)
			{
				if (eException.InnerException != null)
					Logging.WriteDebugLogError("Que.SendCommand()", lRequestId, eException.InnerException, "Unable to process API HTTP response.");
				else
					Logging.WriteDebugLogError("Que.SendCommand()", lRequestId, eException, "Unable to process API HTTP response.");

				bRetVal = false;
				goto Cleanup;
			}

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();

			return bRetVal;
		}

		private static string GenerateDeviceId()
		{
			Random random = new Random();
			int iLength = 25;

			StringBuilder sbDeviceId = new StringBuilder();

			for (int iIndex = 0; iIndex < iLength; iIndex++)
				sbDeviceId.Append(random.Next(0, 9));

			return sbDeviceId.ToString();
		}

		private static string GetBaseURL()
		{
			switch (_strSystemType)
			{
				case "que": return _strBaseURLQue;
				case "neo": return _strBaseURLNeo;
				default: return _strBaseURLQue;
			}
		}
	}
}
