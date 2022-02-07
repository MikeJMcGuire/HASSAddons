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
		private static string _strNextEventURL = "";
		private static bool _bPerZoneControls = false;
		private static bool _bPerZoneSensors = false;
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
		private static ManualResetEvent _eventStop;
		private static AutoResetEvent _eventAuthenticationFailure = new AutoResetEvent(false);
		private static AutoResetEvent _eventQueue = new AutoResetEvent(false);
		private static AutoResetEvent _eventUpdate = new AutoResetEvent(false);
		private static PairingToken _pairingToken;
		private static QueToken _queToken = null;
		private static AirConditionerData _airConditionerData = new AirConditionerData();
		private static Dictionary<int, AirConditionerZone> _airConditionerZones = new Dictionary<int, AirConditionerZone>();
		private static object _oLockData = new object(), _oLockQueue = new object();
		private static bool _bCommandAckPending = false;

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

		public static async void Initialise(string strQueUser, string strQuePassword, string strSerialNumber, string strSystemType, int iPollInterval, bool bPerZoneControls, bool bPerZoneSensors, ManualResetEvent eventStop)
		{
			Thread threadMonitor;

			Logging.WriteDebugLog("Que.Initialise()");

			_strQueUser = strQueUser;
			_strQuePassword = strQuePassword;
			_strSerialNumber = strSerialNumber;
			_strSystemType = strSystemType;
			_bPerZoneControls = bPerZoneControls;
			_bPerZoneSensors = bPerZoneSensors;
			_iPollInterval = iPollInterval;
			_eventStop = eventStop;

			_httpClientAuth.BaseAddress = new Uri(GetBaseURL());
			_httpClient.BaseAddress = new Uri(GetBaseURL());
			_httpClientCommands.BaseAddress = new Uri(GetBaseURL());

			_airConditionerData.LastUpdated = DateTime.MinValue;

			// Get Device Id
			try
			{
				if (File.Exists(_strDeviceIdFile))
				{
					_strDeviceUniqueIdentifier = JsonConvert.DeserializeObject<string>(await File.ReadAllTextAsync(_strDeviceIdFile));

					Logging.WriteDebugLog("Que.Initialise() Device Id: {0}", _strDeviceUniqueIdentifier);
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Que.Initialise()", eException, "Unable to read device json file.");
			}

			// Get Pairing Token
			try
			{
				if (File.Exists(_strPairingTokenFile))
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
					await File.WriteAllTextAsync(_strDeviceIdFile, JsonConvert.SerializeObject(_strDeviceUniqueIdentifier));
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
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL = "api/v0/client/ac-systems?includeAcms=true&includeNeo=true"; // "/api/v0/client/ac-systems";
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

					strResponse = strResponse.Replace("ac-system", "acsystem");

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					for (int iIndex = 0; iIndex < jsonResponse._embedded.acsystem.Count; iIndex++)
					{
						strSerial = jsonResponse._embedded.acsystem[iIndex].serial.ToString();
						strDescription = jsonResponse._embedded.acsystem[iIndex].description.ToString();
						strType = jsonResponse._embedded.acsystem[iIndex].type.ToString();

						Logging.WriteDebugLog("Que.GetAirConditionerSerial() [0x{0}] Found AC: {1} - {2} ({3})", lRequestId.ToString("X8"), strSerial, strDescription, strType);

						if (_strSerialNumber == "")
						{
							Logging.WriteDebugLog("Que.GetAirConditionerSerial() [0x{0}] Defaulting to AC: {1} - {2} ({3})", lRequestId.ToString("X8"), strSerial, strDescription, strType);
							_strSerialNumber = strSerial;
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

			if (!bRetVal)
				_strSerialNumber = "";

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
			Dictionary<int, AirConditionerZone> dZones = new Dictionary<int, AirConditionerZone>();
			AirConditionerZone zone;
			AirConditionerSensor sensor;

			Logging.WriteDebugLog("Que.GetAirConditionerZones() [0x{0}] Base: {1}{2}{3}", lRequestId.ToString("X8"), _httpClient.BaseAddress, strPageURL, _strSerialNumber);

			if (!IsTokenValid())
			{
				bRetVal = false;
				goto Cleanup;
			}

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(TimeSpan.FromSeconds(_iCancellationTime));

				httpResponse = await _httpClient.GetAsync(strPageURL + _strSerialNumber, cancellationToken.Token);

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
									
								dZones.Add(iZoneIndex + 1, zone);
							}
						}

						// Update Air Conditioner Data
						lock (_oLockData)
						{
							_airConditionerZones = dZones;
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

			return bRetVal;
		}

		private async static Task<UpdateItems> GetAirConditionerFullStatus()
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL = "api/v0/client/ac-systems/status/latest?serial=";
			string strResponse;
			dynamic jsonResponse;
			UpdateItems updateItems = UpdateItems.None;

			Logging.WriteDebugLog("Que.GetAirConditionerFullStatus() [0x{0}] Base: {1}{2}{3}", lRequestId.ToString("X8"), _httpClient.BaseAddress, strPageURL, _strSerialNumber);

			if (!IsTokenValid())
				goto Cleanup;

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(TimeSpan.FromSeconds(_iCancellationTime));

				httpResponse = await _httpClient.GetAsync(strPageURL + _strSerialNumber, cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					strResponse = await httpResponse.Content.ReadAsStringAsync();

					Logging.WriteDebugLog("Que.GetAirConditionerFullStatus() [0x{0}] Responded (Encoding {1}, {2} bytes)", lRequestId.ToString("X8"), httpResponse.Content.Headers.ContentEncoding.ToString() == "" ? "N/A" : httpResponse.Content.Headers.ContentEncoding.ToString(), (httpResponse.Content.Headers.ContentLength ?? 0) == 0 ? "N/A" : httpResponse.Content.Headers.ContentLength.ToString());

					lock (_oLockData)
					{
						_airConditionerData.LastUpdated = DateTime.Now;
					}

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					ProcessFullStatus(lRequestId, jsonResponse.lastKnownState);

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

		private static void ProcessFullStatus(long lRequestId, dynamic jsonResponse)
		{
			JArray aEnabledZones;

			Logging.WriteDebugLog("Que.ProcessFullStatus() [0x{0}]", lRequestId.ToString("X8"));

			// Compressor Mode
			ProcessPartialStatus(lRequestId, "LiveAircon.CompressorMode", jsonResponse.LiveAircon.CompressorMode?.ToString(), ref _airConditionerData.CompressorState);

			// Compressor Capacity
			ProcessPartialStatus(lRequestId, "LiveAircon.CompressorCapacity", jsonResponse.LiveAircon.CompressorCapacity?.ToString(), ref _airConditionerData.CompressorCapacity);

			// Compressor Power
			if (jsonResponse.LiveAircon.ContainsKey("OutdoorUnit"))
				ProcessPartialStatus(lRequestId, "LiveAircon.OutdoorUnit.CompPower", jsonResponse.LiveAircon.OutdoorUnit.CompPower?.ToString(), ref _airConditionerData.CompressorPower);

			// On
			ProcessPartialStatus(lRequestId, "UserAirconSettings.isOn", jsonResponse.UserAirconSettings.isOn?.ToString(), ref _airConditionerData.On);

			// Mode
			ProcessPartialStatus(lRequestId, "UserAirconSettings.Mode", jsonResponse.UserAirconSettings.Mode?.ToString(), ref _airConditionerData.Mode);

			// Fan Mode
			ProcessPartialStatus(lRequestId, "UserAirconSettings.FanMode", jsonResponse.UserAirconSettings.FanMode?.ToString(), ref _airConditionerData.FanMode);

			// Set Cooling Temperature
			ProcessPartialStatus(lRequestId, "UserAirconSettings.TemperatureSetpoint_Cool_oC", jsonResponse.UserAirconSettings.TemperatureSetpoint_Cool_oC?.ToString(), ref _airConditionerData.SetTemperatureCooling);

			// Set Heating Temperature
			ProcessPartialStatus(lRequestId, "UserAirconSettings.TemperatureSetpoint_Heat_oC", jsonResponse.UserAirconSettings.TemperatureSetpoint_Heat_oC?.ToString(), ref _airConditionerData.SetTemperatureHeating);

			// Live Temperature
			ProcessPartialStatus(lRequestId, "MasterInfo.LiveTemp_oC", jsonResponse.MasterInfo.LiveTemp_oC?.ToString(), ref _airConditionerData.Temperature);

			// Live Temperature Outside
			ProcessPartialStatus(lRequestId, "MasterInfo.LiveOutdoorTemp_oC", jsonResponse.MasterInfo.LiveOutdoorTemp_oC?.ToString(), ref _airConditionerData.OutdoorTemperature);

			// Live Humidity
			ProcessPartialStatus(lRequestId, "MasterInfo.LiveHumidity_pc", jsonResponse.MasterInfo.LiveHumidity_pc?.ToString(), ref _airConditionerData.Humidity);

			// Coil Inlet Temperature
			ProcessPartialStatus(lRequestId, "LiveAircon.CoilInlet", jsonResponse.LiveAircon.CoilInlet?.ToString(), ref _airConditionerData.CoilInletTemperature);

			// Fan PWM
			ProcessPartialStatus(lRequestId, "LiveAircon.FanPWM", jsonResponse.LiveAircon.FanPWM?.ToString(), ref _airConditionerData.FanPWM);

			// Fan RPM
			ProcessPartialStatus(lRequestId, "LiveAircon.FanRPM", jsonResponse.LiveAircon.FanRPM?.ToString(), ref _airConditionerData.FanRPM);

			// Zones
			aEnabledZones = jsonResponse.UserAirconSettings.EnabledZones;
			if (aEnabledZones.Count != 8)
				Logging.WriteDebugLog("Que.GetAirConditionerFullStatus() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.EnabledZones");
			else
			{
				for (int iZoneIndex = 0; iZoneIndex < _airConditionerZones.Count; iZoneIndex++)
				{
					// Enabled
					ProcessPartialStatus(lRequestId, "UserAirconSettings.EnabledZones", aEnabledZones[iZoneIndex].ToString(), ref _airConditionerZones[iZoneIndex + 1].State);

					// Temperature
					ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].LiveTemp_oC", iZoneIndex), jsonResponse.RemoteZoneInfo[iZoneIndex].LiveTemp_oC?.ToString(), ref _airConditionerZones[iZoneIndex + 1].Temperature);

					// Cooling Set Temperature
					ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Cool_oC", iZoneIndex), jsonResponse.RemoteZoneInfo[iZoneIndex].TemperatureSetpoint_Cool_oC?.ToString(), ref _airConditionerZones[iZoneIndex + 1].SetTemperatureCooling);

					// Heating Set Temperature
					ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Heat_oC", iZoneIndex), jsonResponse.RemoteZoneInfo[iZoneIndex].TemperatureSetpoint_Heat_oC?.ToString(), ref _airConditionerZones[iZoneIndex + 1].SetTemperatureHeating);

					// Position
					ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].ZonePosition", iZoneIndex), jsonResponse.RemoteZoneInfo[iZoneIndex].ZonePosition?.ToString(), ref _airConditionerZones[iZoneIndex + 1].Position);

					// Zone Sensors Temperature
					if (jsonResponse.RemoteZoneInfo[iZoneIndex].ContainsKey("RemoteTemperatures_oC") & _strSystemType == "que")
					{
						foreach (JProperty sensor in jsonResponse.RemoteZoneInfo[iZoneIndex].RemoteTemperatures_oC)
						{
							if (_airConditionerZones[iZoneIndex + 1].Sensors.ContainsKey(sensor.Name))
							{
								ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].RemoteTemperatures_oC.{1}", iZoneIndex, sensor.Name), jsonResponse.RemoteZoneInfo[iZoneIndex].RemoteTemperatures_oC[sensor.Name]?.ToString(), ref _airConditionerZones[iZoneIndex + 1].Sensors[sensor.Name].Temperature);
							}
						}
					}

					// Zone Sensors Battery
					if (jsonResponse.RemoteZoneInfo[iZoneIndex].ContainsKey("Sensors") & _strSystemType == "que")
					{
						foreach (JProperty sensor in jsonResponse.RemoteZoneInfo[iZoneIndex].Sensors)
						{
							if (_airConditionerZones[iZoneIndex + 1].Sensors.ContainsKey(sensor.Name))
							{
								ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].Sensors.{1}.Battery_pc", iZoneIndex, sensor.Name), jsonResponse.RemoteZoneInfo[iZoneIndex].Sensors[sensor.Name].Battery_pc?.ToString(), ref _airConditionerZones[iZoneIndex + 1].Sensors[sensor.Name].Battery);
							}
						}
					}

					// Humidity
					// ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].LiveHumidity_pc", iZoneIndex), jsonResponse.RemoteZoneInfo[iZoneIndex].LiveHumidity_pc?.ToString(), ref _airConditionerZones[iZoneIndex + 1].Humidity);

					// Battery
					//if (jsonResponse.RemoteZoneInfo[iZoneIndex].ContainsKey("Sensors") & _strSystemType == "que")
					//{
					//	jObject = jsonResponse.RemoteZoneInfo[iZoneIndex].Sensors;

					//if (jObject.HasValues && jObject.First.HasValues)
					//	ProcessPartialStatus(lRequestId, string.Format("RemoteZoneInfo[{0}].Sensors[0].Battery_pc", iZoneIndex), jObject.First.First["Battery_pc"]?.ToString(), ref _airConditionerZones[iZoneIndex + 1].Battery);
					//}
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

		private async static Task<UpdateItems> GetAirConditionerEvents()
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

			if (_strNextEventURL == "")
				strPageURL = strPageURLFirstEvent + _strSerialNumber;
			else
				strPageURL = _strNextEventURL;

			Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Base: {1}{2}", lRequestId.ToString("X8"), _httpClient.BaseAddress, strPageURL);

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
						_airConditionerData.LastUpdated = DateTime.Now;
					}

					strResponse = strResponse.Replace("ac-newer-events", "acnewerevents");

					jsonResponse = JsonConvert.DeserializeObject(strResponse);

					_strNextEventURL = jsonResponse._links.acnewerevents.href;
					if (_strNextEventURL.StartsWith("/"))
						_strNextEventURL = _strNextEventURL.Substring(1);

					Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Next Event URL: {1}", lRequestId.ToString("X8"), _strNextEventURL);

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
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.CompressorState);
										updateItems |= UpdateItems.Main;
									}
									// Compressor Capacity
									else if (change.Name == "LiveAircon.CompressorCapacity")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.CompressorCapacity);
										updateItems |= UpdateItems.Main;
									}
									// Compressor Power
									else if (change.Name == "LiveAircon.OutdoorUnit.CompPower")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.CompressorPower);
										updateItems |= UpdateItems.Main;
									}
									// Mode
									else if (change.Name == "UserAirconSettings.Mode")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.Mode);
										updateItems |= UpdateItems.Main;
									}
									// Fan Mode
									else if (change.Name == "UserAirconSettings.FanMode")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.FanMode);
										updateItems |= UpdateItems.Main;
									}
									// On
									else if (change.Name == "UserAirconSettings.isOn")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.On);
										updateItems |= UpdateItems.Main;
									}
									// Live Temperature
									else if (change.Name == "MasterInfo.LiveTemp_oC")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.Temperature);
										updateItems |= UpdateItems.Main;
									}
									// Live Temperature Outside
									else if (change.Name == "MasterInfo.LiveOutdoorTemp_oC")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.OutdoorTemperature);
										updateItems |= UpdateItems.Main;
									}
									// Live Humidity
									else if (change.Name == "MasterInfo.LiveHumidity_pc")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.Humidity);
										updateItems |= UpdateItems.Main;
									}
									// Set Temperature Cooling
									else if (change.Name == "UserAirconSettings.TemperatureSetpoint_Cool_oC")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.SetTemperatureCooling);
										updateItems |= UpdateItems.Main;
									}
									// Set Temperature Heating
									else if (change.Name == "UserAirconSettings.TemperatureSetpoint_Heat_oC")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.SetTemperatureHeating);
										updateItems |= UpdateItems.Main;
									}
									// Coil Inlet Temperature
									else if (change.Name == "LiveAircon.CoilInlet")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.CoilInletTemperature);
										updateItems |= UpdateItems.Main;
									}
									// Fan PWM
									else if (change.Name == "LiveAircon.FanPWM")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.FanPWM);
										updateItems |= UpdateItems.Main;
									}
									// Fan RPM
									else if (change.Name == "LiveAircon.FanRPM")
									{
										ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerData.FanRPM);
										updateItems |= UpdateItems.Main;
									}
									// Remote Zone
									else if (change.Name.StartsWith("RemoteZoneInfo["))
									{
										iIndex = int.Parse(change.Name.Substring(change.Name.IndexOf("[") + 1, 1));

										if (_airConditionerZones.ContainsKey(iIndex + 1))
										{
											updateItems |= (UpdateItems)Math.Pow(2, iIndex + 1);

											// Live Temperature
											if (change.Name.EndsWith("].LiveTemp_oC"))
												ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerZones[iIndex + 1].Temperature);
											// Cooling Set Temperature
											else if (change.Name.EndsWith("].TemperatureSetpoint_Cool_oC"))
												ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerZones[iIndex + 1].SetTemperatureCooling);
											// Heating Set Temperature
											else if (change.Name.EndsWith("].TemperatureSetpoint_Heat_oC"))
												ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerZones[iIndex + 1].SetTemperatureHeating);
											// Zone Position
											else if (change.Name.EndsWith("].ZonePosition"))
												ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerZones[iIndex + 1].Position);
										}
									}
									// Enabled Zone
									else if (change.Name.StartsWith("UserAirconSettings.EnabledZones["))
									{
										iIndex = int.Parse(change.Name.Substring(change.Name.IndexOf("[") + 1, 1));

										if (_airConditionerZones.ContainsKey(iIndex + 1))
										{
											ProcessPartialStatus(lRequestId, change.Name, change.Value.ToString(), ref _airConditionerZones[iIndex + 1].State);
											updateItems |= UpdateItems.Main;
											updateItems |= (UpdateItems)Math.Pow(2, iIndex + 1);
										}
									}
								}

								break;

							case "full-status-broadcast":
								ProcessFullStatus(lRequestId, jsonResponse.events[iEvent].data);

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
				_strNextEventURL = "";

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

							updateItems = await GetAirConditionerEvents();
							if (updateItems != UpdateItems.None)
							{
								MQTTUpdateData(updateItems);
								MQTT.Update(null);
							}
						}
						// Neo No Events Mode
						else
						{
							Thread.Sleep(_iPostCommandSleepTimerNeoNoEventsMode * 1000);

							updateItems = await GetAirConditionerFullStatus();
							if (updateItems != UpdateItems.None)
							{
								MQTTUpdateData(updateItems);
								MQTT.Update(null);
							}
						}

						break;

					case WaitHandle.WaitTimeout: // Wait Timeout
						if (_strSerialNumber == "")
							if (!await GetAirConditionerSerial())
								continue;

						if (_airConditionerZones.Count == 0)
						{
							if (!await GetAirConditionerZones())
								continue;
							else
								MQTTRegister();
						}

						// Normal Mode
						if (!_bNeoNoEventMode)
						{
							updateItems = await GetAirConditionerEvents();
							if (updateItems != UpdateItems.None)
							{
								if (_bEventsReceived)
								{
									MQTTUpdateData(updateItems);
									MQTT.Update(null);
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
							updateItems = await GetAirConditionerFullStatus();
							if (updateItems != UpdateItems.None)
							{
								MQTTUpdateData(updateItems);
								MQTT.Update(null);
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
			Logging.WriteDebugLog("Que.MQTTRegister()");

			MQTT.SendMessage("homeassistant/climate/actronque/config", "{{\"name\":\"{1}\",\"unique_id\":\"{0}-AC\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"modes\":[\"off\",\"auto\",\"cool\",\"fan_only\",\"heat\"],\"fan_modes\":[\"high\",\"medium\",\"low\",\"auto\"],\"mode_command_topic\":\"actronque/mode/set\",\"temperature_command_topic\":\"actronque/temperature/set\",\"fan_mode_command_topic\":\"actronque/fan/set\",\"min_temp\":\"12\",\"max_temp\":\"30\",\"temp_step\":\"0.5\",\"fan_mode_state_topic\":\"actronque/fanmode\",\"action_topic\":\"actronque/compressor\",\"temperature_state_topic\":\"actronque/settemperature\",\"mode_state_topic\":\"actronque/mode\",\"current_temperature_topic\":\"actronque/temperature\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT);
			MQTT.SendMessage("homeassistant/sensor/actronquehumidity/config", "{{\"name\":\"{1} Humidity\",\"unique_id\":\"{0}-Humidity\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/humidity\",\"unit_of_measurement\":\"%\",\"device_class\":\"humidity\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT);

			if (_strSystemType == "que")
			{
				MQTT.SendMessage("homeassistant/sensor/actronquecompressorcapacity/config", "{{\"name\":\"{1} Compressor Capacity\",\"unique_id\":\"{0}-CompressorCapacity\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/compressorcapacity\",\"unit_of_measurement\":\"%\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT);
				MQTT.SendMessage("homeassistant/sensor/actronquecompressorpower/config", "{{\"name\":\"{1} Compressor Power\",\"unique_id\":\"{0}-CompressorPower\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/compressorpower\",\"unit_of_measurement\":\"W\",\"device_class\":\"power\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT);
				MQTT.SendMessage("homeassistant/sensor/actronqueoutdoortemperature/config", "{{\"name\":\"{1} Outdoor Temperature\",\"unique_id\":\"{0}-OutdoorTemperature\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/outdoortemperature\",\"unit_of_measurement\":\"\u00B0C\",\"device_class\":\"temperature\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT);
				MQTT.SendMessage("homeassistant/sensor/actronquecoilinlettemperature/config", "{{\"name\":\"{1} Coil Inlet Temperature\",\"unique_id\":\"{0}-CoilInletTemperature\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/coilinlettemperature\",\"unit_of_measurement\":\"\u00B0C\",\"device_class\":\"temperature\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT);
				MQTT.SendMessage("homeassistant/sensor/actronquefanpwm/config", "{{\"name\":\"{1} Fan PWM\",\"unique_id\":\"{0}-FanPWM\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/fanpwm\",\"unit_of_measurement\":\"%\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT);
				MQTT.SendMessage("homeassistant/sensor/actronquefanrpm/config", "{{\"name\":\"{1} Fan RPM\",\"unique_id\":\"{0}-FanRPM\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/fanrpm\",\"unit_of_measurement\":\"RPM\",\"availability_topic\":\"{0}/status\"}}", Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT);
			}
			else
			{
				MQTT.SendMessage("homeassistant/sensor/actronquecompressorcapacity/config", "");
				MQTT.SendMessage("homeassistant/sensor/actronquecompressorpower/config", "");
				MQTT.SendMessage("homeassistant/sensor/actronqueoutdoortemperature/config", "");
			}

			foreach (int iZone in _airConditionerZones.Keys)
			{
				MQTT.SendMessage(string.Format("homeassistant/switch/actronque/airconzone{0}/config", iZone), "{{\"name\":\"{0} Zone\",\"unique_id\":\"{2}-z{1}s\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{3}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/zone{1}\",\"command_topic\":\"actronque/zone{1}/set\",\"payload_on\":\"ON\",\"payload_off\":\"OFF\",\"state_on\":\"ON\",\"state_off\":\"OFF\",\"availability_topic\":\"{2}/status\"}}", _airConditionerZones[iZone].Name, iZone, Service.ServiceName.ToLower(), Service.DeviceNameMQTT);
				MQTT.Subscribe("actronque/zone{0}/set", iZone);

				MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/airconzone{0}/config", iZone), "{{\"name\":\"{0}\",\"unique_id\":\"{2}-z{1}t\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{3}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/zone{1}/temperature\",\"unit_of_measurement\":\"\u00B0C\",\"availability_topic\":\"{2}/status\"}}", _airConditionerZones[iZone].Name, iZone, Service.ServiceName.ToLower(), Service.DeviceNameMQTT);
				MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/airconzone{0}position/config", iZone), "{{\"name\":\"{0} Position\",\"unique_id\":\"{2}-z{1}p\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{3}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/zone{1}/position\",\"unit_of_measurement\":\"%\",\"availability_topic\":\"{2}/status\"}}", _airConditionerZones[iZone].Name, iZone, Service.ServiceName.ToLower(), Service.DeviceNameMQTT);

				if (_bPerZoneControls)
				{
					MQTT.SendMessage(string.Format("homeassistant/climate/actronque/zone{0}/config", iZone), "{{\"name\":\"{0} {3}\",\"unique_id\":\"{2}-z{1}ac\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{4}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"modes\":[\"off\",\"auto\",\"cool\",\"fan_only\",\"heat\"],\"mode_command_topic\":\"actronque/zone{1}/mode/set\",\"temperature_command_topic\":\"actronque/zone{1}/temperature/set\",\"min_temp\":\"12\",\"max_temp\":\"30\",\"temp_step\":\"0.5\",\"temperature_state_topic\":\"actronque/zone{1}/settemperature\",\"mode_state_topic\":\"actronque/zone{1}/mode\",\"current_temperature_topic\":\"actronque/zone{1}/temperature\",\"availability_topic\":\"{2}/status\"}}", _airConditionerZones[iZone].Name, iZone, Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT);
					MQTT.Subscribe("actronque/zone{0}/temperature/set", iZone);
					MQTT.Subscribe("actronque/zone{0}/mode/set", iZone);

					// Clear Old Entities
					if (_strSystemType == "que")
					{
						MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/zone{0}humidity/config", iZone), "");
						MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/zone{0}battery/config", iZone), "");
					}
				}
				else
				{
					MQTT.SendMessage(string.Format("homeassistant/climate/actronque/zone{0}/config", iZone), "");

					// Clear Old Entities
					if (_strSystemType == "que")
					{
						MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/zone{0}humidity/config", iZone), "");
						MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/zone{0}battery/config", iZone), "");
					}
				}

				if (_bPerZoneSensors && _strSystemType == "que")
				{
					foreach (string sensor in _airConditionerZones[iZone].Sensors.Keys)
					{
						MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/zone{0}sensor{1}temperature/config", iZone, sensor), "{{\"name\":\"{0} Temperature\",\"unique_id\":\"{2}-z{1}s{5}temperature\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{4}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/zone{1}sensor{5}/temperature\",\"unit_of_measurement\":\"%\",\"device_class\":\"temperature\",\"availability_topic\":\"{2}/status\"}}", _airConditionerZones[iZone].Sensors[sensor].Name, iZone, Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT, sensor);
						MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/zone{0}sensor{1}battery/config", iZone, sensor), "{{\"name\":\"{0} Battery\",\"unique_id\":\"{2}-z{1}s{5}battery\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{4}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actronque/zone{1}sensor{5}/battery\",\"unit_of_measurement\":\"%\",\"device_class\":\"battery\",\"availability_topic\":\"{2}/status\"}}", _airConditionerZones[iZone].Sensors[sensor].Name, iZone, Service.ServiceName.ToLower(), _strAirConditionerName, Service.DeviceNameMQTT, sensor);
					}

				}
				else if (_strSystemType == "que")
				{
					foreach (string sensor in _airConditionerZones[iZone].Sensors.Keys)
					{
						MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/zone{0}sensor{1}temperature/config", iZone, sensor), "");
						MQTT.SendMessage(string.Format("homeassistant/sensor/actronque/zone{0}sensor{1}battery/config", iZone, sensor), "");
					}
				}
			}

			MQTT.Subscribe("actronque/mode/set");
			MQTT.Subscribe("actronque/fan/set");
			MQTT.Subscribe("actronque/temperature/set");
		}

		private static void MQTTUpdateData(UpdateItems items)
		{
			Logging.WriteDebugLog("Que.MQTTUpdateData() Items: {0}", items.ToString());

			if (_airConditionerData.LastUpdated == DateTime.MinValue)
			{
				Logging.WriteDebugLog("Que.MQTTUpdateData() Skipping update, No data received");
				return;
			}

			if (items.HasFlag(UpdateItems.Main))
			{
				// Fan Mode
				switch (_airConditionerData.FanMode)
				{
					case "AUTO":
						MQTT.SendMessage("actronque/fanmode", "auto");
						break;

					case "AUTO+CONT":
						MQTT.SendMessage("actronque/fanmode", "auto");
						break;

					case "LOW":
						MQTT.SendMessage("actronque/fanmode", "low");
						break;

					case "LOW+CONT":
						MQTT.SendMessage("actronque/fanmode", "low");
						break;

					case "MED":
						MQTT.SendMessage("actronque/fanmode", "medium");
						break;

					case "MED+CONT":
						MQTT.SendMessage("actronque/fanmode", "medium");
						break;

					case "HIGH":
						MQTT.SendMessage("actronque/fanmode", "high");
						break;

					case "HIGH+CONT":
						MQTT.SendMessage("actronque/fanmode", "high");
						break;

					default:
						Logging.WriteDebugLog("Que.MQTTUpdateData() Unexpected Fan Mode: {0}", _airConditionerData.FanMode);
						break;
				}

				// Temperature
				MQTT.SendMessage("actronque/temperature", _airConditionerData.Temperature.ToString("N1"));

				if (_strSystemType == "que")
					MQTT.SendMessage("actronque/outdoortemperature", _airConditionerData.OutdoorTemperature.ToString("N1"));

				// Humidity
				MQTT.SendMessage("actronque/humidity", _airConditionerData.Humidity.ToString("N1"));

				// Power, Mode & Set Temperature
				if (!_airConditionerData.On)
				{
					MQTT.SendMessage("actronque/mode", "off");
					MQTT.SendMessage("actronque/settemperature", GetSetTemperature(_airConditionerData.SetTemperatureHeating, _airConditionerData.SetTemperatureCooling).ToString("N1"));
				}
				else
				{
					switch (_airConditionerData.Mode)
					{
						case "AUTO":
							MQTT.SendMessage("actronque/mode", "auto");
							MQTT.SendMessage("actronque/settemperature", GetSetTemperature(_airConditionerData.SetTemperatureHeating, _airConditionerData.SetTemperatureCooling).ToString("N1"));
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
							MQTT.SendMessage("actronque/settemperature", "");
							break;

						default:
							Logging.WriteDebugLog("Que.MQTTUpdateData() Unexpected Mode: {0}", _airConditionerData.Mode);
							break;
					}
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

				if (_strSystemType == "que")
				{
					// Compressor Capacity
					MQTT.SendMessage("actronque/compressorcapacity", _airConditionerData.CompressorCapacity.ToString("F1"));

					// Compressor Power
					MQTT.SendMessage("actronque/compressorpower", _airConditionerData.CompressorPower.ToString("F2"));

					// Coil Inlet Temperature
					MQTT.SendMessage("actronque/coilinlettemperature", _airConditionerData.CoilInletTemperature.ToString("F2"));

					// Fan PWM
					MQTT.SendMessage("actronque/fanpwm", _airConditionerData.FanPWM.ToString("F0"));

					// Fan RPM
					MQTT.SendMessage("actronque/fanrpm", _airConditionerData.FanRPM.ToString("F0"));
				}
			}

			// Zones
			foreach (int iIndex in _airConditionerZones.Keys)
			{
				if (items.HasFlag((UpdateItems)Math.Pow(2, iIndex)))
				{
					MQTT.SendMessage(string.Format("actronque/zone{0}", iIndex), _airConditionerZones[iIndex].State ? "ON" : "OFF");
					MQTT.SendMessage(string.Format("actronque/zone{0}/temperature", iIndex), _airConditionerZones[iIndex].Temperature.ToString());
					MQTT.SendMessage(string.Format("actronque/zone{0}/position", iIndex), (_airConditionerZones[iIndex].Position * 5).ToString()); // 0-20 numeric displayed as 0-100 percentage

					// Per Zone Controls
					if (_bPerZoneControls)
					{
						if (!_airConditionerData.On)
						{
							MQTT.SendMessage(string.Format("actronque/zone{0}/mode", iIndex), "off");
							MQTT.SendMessage(string.Format("actronque/zone{0}/settemperature", iIndex), GetSetTemperature(_airConditionerZones[iIndex].SetTemperatureHeating, _airConditionerZones[iIndex].SetTemperatureCooling).ToString("N1"));
						}
						else
						{
							switch (_airConditionerData.Mode)
							{
								case "AUTO":
									MQTT.SendMessage(string.Format("actronque/zone{0}/mode", iIndex), (_airConditionerZones[iIndex].State ? "auto" : "off"));
									MQTT.SendMessage(string.Format("actronque/zone{0}/settemperature", iIndex), GetSetTemperature(_airConditionerZones[iIndex].SetTemperatureHeating, _airConditionerZones[iIndex].SetTemperatureCooling).ToString("N1"));
									break;

								case "COOL":
									MQTT.SendMessage(string.Format("actronque/zone{0}/mode", iIndex), (_airConditionerZones[iIndex].State ? "cool" : "off"));
									MQTT.SendMessage(string.Format("actronque/zone{0}/settemperature", iIndex), _airConditionerZones[iIndex].SetTemperatureCooling.ToString("N1"));
									break;

								case "HEAT":
									MQTT.SendMessage(string.Format("actronque/zone{0}/mode", iIndex), (_airConditionerZones[iIndex].State ? "heat" : "off"));
									MQTT.SendMessage(string.Format("actronque/zone{0}/settemperature", iIndex), _airConditionerZones[iIndex].SetTemperatureHeating.ToString("N1"));
									break;

								case "FAN":
									MQTT.SendMessage(string.Format("actronque/zone{0}/mode", iIndex), (_airConditionerZones[iIndex].State ? "fan_only" : "off"));
									MQTT.SendMessage(string.Format("actronque/zone{0}/settemperature", iIndex), GetSetTemperature(_airConditionerZones[iIndex].SetTemperatureHeating, _airConditionerZones[iIndex].SetTemperatureCooling).ToString("N1"));
									break;

								default:
									Logging.WriteDebugLog("Que.MQTTUpdateData() Unexpected Mode: {0}", _airConditionerData.Mode);
									break;
							}
						}
					}

					// Per Zone Sensors
					if (_bPerZoneSensors && _strSystemType == "que")
					{
						foreach (AirConditionerSensor sensor in _airConditionerZones[iIndex].Sensors.Values)
						{
							MQTT.SendMessage(string.Format("actronque/zone{0}sensor{1}/temperature", iIndex, sensor.Serial), sensor.Temperature.ToString("F1"));
							MQTT.SendMessage(string.Format("actronque/zone{0}sensor{1}/battery", iIndex, sensor.Serial), sensor.Battery.ToString("F1"));
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

		public static void ChangeZone(long lRequestId, int iZone, bool bState)
		{
			bool[] bZones;
			QueueCommand command = new QueueCommand(lRequestId, DateTime.Now.AddSeconds(_iCommandExpiry));

			Logging.WriteDebugLog("Que.ChangeZone() [0x{0}] Zone {1}: {2}", lRequestId.ToString("X8"), iZone, bState ? "On" : "Off");

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
						if (_airConditionerZones.ContainsKey(iZone))
							_airConditionerZones[iZone].State = bState;

						MQTT.SendMessage(string.Format("actronque/zone{0}", iZone), _airConditionerZones[iZone].State ? "ON" : "OFF");
					}

					for (int iIndex = 0; iIndex < bZones.Length; iIndex++)
					{
						if ((iIndex + 1) == iZone)
							bZones[iIndex] = bState;
						else
							bZones[iIndex] = (_airConditionerZones.ContainsKey(iIndex + 1) ? _airConditionerZones[iIndex + 1].State : false);
					}

					command.Data.command.Add("UserAirconSettings.EnabledZones", bZones);

					break;

				default:
					return;
			}

			AddCommandToQueue(command);
		}

		public static void ChangeMode(long lRequestId, AirConditionerMode mode)
		{
			QueueCommand command = new QueueCommand(lRequestId, DateTime.Now.AddSeconds(_iCommandExpiry));

			Logging.WriteDebugLog("Que.ChangeMode() [0x{0}] Mode: {1}", lRequestId.ToString("X8"), mode.ToString());

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

		public static void ChangeFanMode(long lRequestId, FanMode fanMode)
		{
			QueueCommand command = new QueueCommand(lRequestId, DateTime.Now.AddSeconds(_iCommandExpiry));

			Logging.WriteDebugLog("Que.ChangeFanMode() [0x{0}] Fan Mode: {1}", lRequestId.ToString("X8"), fanMode.ToString());

			switch (fanMode)
			{
				case FanMode.Automatic:
					command.Data.command.Add("UserAirconSettings.FanMode", _airConditionerData.FanMode.EndsWith("CONT") ? "AUTO+CONT" : "AUTO");

					break;

				case FanMode.Low:
					command.Data.command.Add("UserAirconSettings.FanMode", _airConditionerData.FanMode.EndsWith("CONT") ? "LOW+CONT" : "LOW");

					break;

				case FanMode.Medium:
					command.Data.command.Add("UserAirconSettings.FanMode", _airConditionerData.FanMode.EndsWith("CONT") ? "MED+CONT" : "MED");

					break;

				case FanMode.High:
					command.Data.command.Add("UserAirconSettings.FanMode", _airConditionerData.FanMode.EndsWith("CONT") ? "HIGH+CONT" : "HIGH");

					break;
			}

			command.Data.command.Add("type", "set-settings");

			AddCommandToQueue(command);
		}

		public static void ChangeTemperature(long lRequestId, double dblTemperature, int iZone)
		{
			QueueCommand command = new QueueCommand(lRequestId, DateTime.Now.AddSeconds(_iCommandExpiry));

			Logging.WriteDebugLog("Que.ChangeTemperature() [0x{0}] Zone: {1}, Temperature: {2}", lRequestId.ToString("X8"), iZone, dblTemperature);

			if (iZone == 0)
			{
				switch (_airConditionerData.Mode)
				{
					case "OFF":
						return;

					case "FAN":
						return;

					case "COOL":
						command.Data.command.Add("UserAirconSettings.TemperatureSetpoint_Cool_oC", dblTemperature);

						break;

					case "HEAT":
						command.Data.command.Add("UserAirconSettings.TemperatureSetpoint_Heat_oC", dblTemperature);

						break;

					case "AUTO":
						command.Data.command.Add("UserAirconSettings.TemperatureSetpoint_Heat_oC", dblTemperature);
						command.Data.command.Add("UserAirconSettings.TemperatureSetpoint_Cool_oC", dblTemperature);

						break;
				}
			}
			else
			{
				switch (_airConditionerData.Mode)
				{
					case "OFF":
						return;

					case "FAN":
						return;

					case "COOL":
						command.Data.command.Add(string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Cool_oC", iZone - 1), dblTemperature);

						break;

					case "HEAT":
						command.Data.command.Add(string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Heat_oC", iZone - 1), dblTemperature);

						break;

					case "AUTO":
						command.Data.command.Add(string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Heat_oC", iZone - 1), dblTemperature);
						command.Data.command.Add(string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Cool_oC", iZone - 1), dblTemperature);

						break;
				}
			}

			command.Data.command.Add("type", "set-settings");

			AddCommandToQueue(command);
		}

		private static async Task<bool> SendCommand(QueueCommand command)
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			StringContent content;
			long lRequestId = RequestManager.GetRequestId(command.RequestId);
			string strPageURL = "api/v0/client/ac-systems/cmds/send?serial=";
			bool bRetVal = true;

			Logging.WriteDebugLog("Que.SendCommand() [0x{0}] Base: {1}{2}{3}", lRequestId.ToString("X8"), _httpClient.BaseAddress, strPageURL, _strSerialNumber);

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

				httpResponse = await _httpClientCommands.PostAsync(strPageURL + _strSerialNumber, content, cancellationToken.Token);

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
