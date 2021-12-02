using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HMX.HASSActronQue
{
	public class Que
	{
		private static string _strBaseURLQue = "https://que.actronair.com.au/";
		private static string _strBaseURLNeo = "https://nimbus.actronair.com.au/";
		private static string _strSystemType;
		//private static string _strBaseUserAgent = "nxgen-ios/1.1.2 (iPhone; iOS 12.1.4; Scale/3.00)";
		private static string _strDeviceName = "HASSActronQue";
		private static string _strAirConditionerName = "Air Conditioner";
		private static string _strDeviceIdFile = "/data/deviceid.json";
		private static string _strPairingTokenFile = "/data/pairingtoken.json";
		private static string _strBearerTokenFile = "/data/bearertoken.json";
		private static string _strDeviceUniqueIdentifier = "";
		private static string _strQueUser, _strQuePassword, _strSerialNumber;
		private static string _strNextEventURL = "";
		private static bool _bPerZoneControls = false;
		private static Queue<QueueCommand> _queueCommands = new Queue<QueueCommand>();
		private static HttpClient _httpClient = null, _httpClientAuth = null, _httpClientCommands = null;
		private static int _iCancellationTime = 15; // Seconds
		private static int _iPollInterval = 15; // Seconds
		private static int _iPollIntervalUpdate = 5; // Seconds
		private static int _iAuthenticationInterval = 60; // Seconds
		private static int _iQueueInterval = 10; // Seconds
		private static int _iCommandExpiry = 10; // Seconds
		private static int _iPostCommandSleepTimer = 2; // Seconds
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

			_httpClientAuth = new HttpClient(httpClientHandler);

			_httpClient = new HttpClient(httpClientHandler);

			_httpClientCommands = new HttpClient(httpClientHandler);		
		}

		public static async void Initialise(string strQueUser, string strQuePassword, string strSerialNumber, string strSystemType, int iPollInterval, bool bPerZoneControls, ManualResetEvent eventStop)
		{
			Thread threadMonitor;

			Logging.WriteDebugLog("Que.Initialise()");

			_strQueUser = strQueUser;
			_strQuePassword = strQuePassword;
			_strSerialNumber = strSerialNumber;
			_strSystemType = strSystemType;
			_bPerZoneControls = bPerZoneControls;
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
					for (int iZoneIndex = 0; iZoneIndex < jsonResponse.lastKnownState.RemoteZoneInfo.Count; iZoneIndex++)
					{
						if (bool.Parse(jsonResponse.lastKnownState.RemoteZoneInfo[iZoneIndex].NV_Exists.ToString()))
						{
							zone = new AirConditionerZone();

							zone.Name = jsonResponse.lastKnownState.RemoteZoneInfo[iZoneIndex].NV_Title;
							if (zone.Name == "")
								zone.Name = "Zone " + (iZoneIndex + 1);
							zone.Temperature = jsonResponse.lastKnownState.RemoteZoneInfo[iZoneIndex].LiveTemp_oC;

							Logging.WriteDebugLog("Que.GetAirConditionerZones() [0x{0}] Zone: {1} - {2}", lRequestId.ToString("X8"), iZoneIndex + 1, zone.Name);

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

		private async static Task<bool> GetAirConditionerEvents()
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			long lRequestId = RequestManager.GetRequestId();
			string strPageURL, strPageURLFirstEvent = "api/v0/client/ac-systems/events/latest?serial=";
			string strResponse;
			dynamic jsonResponse;
			bool bRetVal = true;
			bool bTemp;
			double dblTemp;
			string strEventType, strInput;
			JArray aEnabledZones;
			int iIndex;

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

					//Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Response: {1}", lRequestId.ToString("X8"), strResponse);

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
										strInput = change.Value.ToString();
										if (strInput == "")
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "LiveAircon.CompressorMode");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.CompressorState = strInput;
											}
										}
									}
									// Compressor Capacity
									else if (change.Name == "LiveAircon.CompressorCapacity")
									{
										if (!double.TryParse(change.Value.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "LiveAircon.CompressorCapacity");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.CompressorCapacity = dblTemp;
											}
										}
									}
									// Compressor Power
									else if (change.Name == "LiveAircon.OutdoorUnit.CompPower")
									{
										if (!double.TryParse(change.Value.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "LiveAircon.OutdoorUnit.CompPower");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.CompressorPower = dblTemp;
											}
										}
									}
									// Mode
									else if (change.Name == "UserAirconSettings.Mode")
									{
										strInput = change.Value.ToString();
										if (strInput == "")
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.Mode");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.Mode = strInput;
											}
										}
									}
									// Fan Mode
									else if (change.Name == "UserAirconSettings.FanMode")
									{
										strInput = change.Value.ToString();
										if (strInput == "")
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.FanMode");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.FanMode = strInput;
												_airConditionerData.Continuous = strInput.EndsWith("CONT");
											}
										}
									}
									// On
									else if (change.Name == "UserAirconSettings.isOn")
									{
										if (!bool.TryParse(change.Value.ToString(), out bTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.isOn");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.On = bTemp;
											}
										}
									}
									// Live Temperature
									else if (change.Name == "MasterInfo.LiveTemp_oC")
									{
										if (!double.TryParse(change.Value.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "MasterInfo.LiveTemp_oC");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.Temperature = dblTemp;
											}
										}
									}
									// Live Temperature Outside
									else if (change.Name == "MasterInfo.LiveOutdoorTemp_oC")
									{
										if (!double.TryParse(change.Value.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "MasterInfo.LiveOutdoorTemp_oC");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.OutdoorTemperature = dblTemp;
											}
										}
									}
									// Live Humidity
									else if (change.Name == "MasterInfo.LiveHumidity_pc")
									{
										if (!double.TryParse(change.Value.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "MasterInfo.LiveHumidity_pc");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.Humidity = dblTemp;
											}
										}
									}
									// Set Temperature Cooling
									else if (change.Name == "UserAirconSettings.TemperatureSetpoint_Cool_oC")
									{
										if (!double.TryParse(change.Value.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.TemperatureSetpoint_Cool_oC");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.SetTemperatureCooling = dblTemp;
											}
										}
									}
									// Set Temperature Heating
									else if (change.Name == "UserAirconSettings.TemperatureSetpoint_Heat_oC")
									{
										if (!double.TryParse(change.Value.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.TemperatureSetpoint_Heat_oC");
										else
										{
											lock (_oLockData)
											{
												_airConditionerData.SetTemperatureHeating = dblTemp;
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
											if (!double.TryParse(change.Value.ToString(), out dblTemp))
												Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), string.Format("RemoteZoneInfo[{0}].LiveTemp_oC", iIndex));
											else
											{
												lock (_oLockData)
												{
													if (_airConditionerZones.ContainsKey(iIndex + 1))
														_airConditionerZones[iIndex + 1].Temperature = dblTemp;
												}
											}
										}
										// Cooling Set Temperature
										else if (change.Name.EndsWith("].TemperatureSetpoint_Cool_oC"))
										{
											if (!double.TryParse(change.Value.ToString(), out dblTemp))
												Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Cool_oC", iIndex));
											else
											{
												lock (_oLockData)
												{
													if (_airConditionerZones.ContainsKey(iIndex + 1))
														_airConditionerZones[iIndex + 1].SetTemperatureCooling = dblTemp;
												}
											}
										}
										// Heating Set Temperature
										else if (change.Name.EndsWith("].TemperatureSetpoint_Heat_oC"))
										{
											if (!double.TryParse(change.Value.ToString(), out dblTemp))
												Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Heat_oC", iIndex));
											else
											{
												lock (_oLockData)
												{
													if (_airConditionerZones.ContainsKey(iIndex + 1))
														_airConditionerZones[iIndex + 1].SetTemperatureHeating = dblTemp;
												}
											}
										}
										// Zone Position
										else if (change.Name.EndsWith("].ZonePosition"))
										{
											if (!double.TryParse(change.Value.ToString(), out dblTemp))
												Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), string.Format("RemoteZoneInfo[{0}].ZonePosition", iIndex));
											else
											{
												lock (_oLockData)
												{
													if (_airConditionerZones.ContainsKey(iIndex + 1))
														_airConditionerZones[iIndex + 1].Position = dblTemp;
												}
											}
										}
									}
									// Enabled Zone
									else if (change.Name.StartsWith("UserAirconSettings.EnabledZones["))
									{
										iIndex = int.Parse(change.Name.Substring(change.Name.IndexOf("[") + 1, 1));

										if (!bool.TryParse(change.Value.ToString(), out bTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.EnabledZones");
										else
										{
											lock (_oLockData)
											{
												if (_airConditionerZones.ContainsKey(iIndex + 1))
													_airConditionerZones[iIndex + 1].State = bTemp;
											}
										}
									}
								}

								break;

							case "full-status-broadcast":
								// Compressor Mode
								strInput = jsonResponse.events[iEvent].data.LiveAircon.CompressorMode;
								if (strInput == "")
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "LiveAircon.CompressorMode");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.CompressorState = strInput;
									}
								}

								// Compressor Capacity
								if (!double.TryParse(jsonResponse.events[iEvent].data.LiveAircon.CompressorCapacity.ToString(), out dblTemp))
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "LiveAircon.CompressorCapacity");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.CompressorCapacity = dblTemp;
									}
								}

								// Compressor Power
								if (!double.TryParse(jsonResponse.events[iEvent].data.LiveAircon.OutdoorUnit.CompPower.ToString(), out dblTemp))
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "LiveAircon.OutdoorUnit.CompPower");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.CompressorPower = dblTemp;
									}
								}								

								// On
								if (!bool.TryParse(jsonResponse.events[iEvent].data.UserAirconSettings.isOn.ToString(), out bTemp))
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.isOn");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.On = bTemp;
									}
								}

								// Mode
								strInput = jsonResponse.events[iEvent].data.UserAirconSettings.Mode;
								if (strInput == "")
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.Mode");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.Mode = strInput;
									}
								}

								// Fan Mode
								strInput = jsonResponse.events[iEvent].data.UserAirconSettings.FanMode;
								if (strInput == "")
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.FanMode");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.FanMode = strInput;
										_airConditionerData.Continuous = strInput.EndsWith("CONT");
									}
								}

								// Set Cooling Temperature
								if (!double.TryParse(jsonResponse.events[iEvent].data.UserAirconSettings.TemperatureSetpoint_Cool_oC.ToString(), out dblTemp))
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.TemperatureSetpoint_Cool_oC");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.SetTemperatureCooling = dblTemp;
									}
								}

								// Set Heating Temperature
								if (!double.TryParse(jsonResponse.events[iEvent].data.UserAirconSettings.TemperatureSetpoint_Heat_oC.ToString(), out dblTemp))
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.TemperatureSetpoint_Heat_oC");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.SetTemperatureHeating = dblTemp;
									}
								}

								// Live Temperature
								if (!double.TryParse(jsonResponse.events[iEvent].data.MasterInfo.LiveTemp_oC.ToString(), out dblTemp))
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "MasterInfo.LiveTemp_oC");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.Temperature = dblTemp;
									}
								}

								// Live Temperature Outside
								if (!double.TryParse(jsonResponse.events[iEvent].data.MasterInfo.LiveOutdoorTemp_oC.ToString(), out dblTemp))
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "MasterInfo.LiveOutdoorTemp_oC");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.OutdoorTemperature = dblTemp;
									}
								}			
								
								// Live Humidity
								if (!double.TryParse(jsonResponse.events[iEvent].data.MasterInfo.LiveHumidity_pc.ToString(), out dblTemp))
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "MasterInfo.LiveHumidity_pc");
								else
								{
									lock (_oLockData)
									{
										_airConditionerData.Humidity = dblTemp;
									}
								}

								// Zones
								aEnabledZones = jsonResponse.events[iEvent].data.UserAirconSettings.EnabledZones;
								if (aEnabledZones.Count != 8)
									Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.EnabledZones");
								else
								{
									for (int iZoneIndex = 0; iZoneIndex < 8; iZoneIndex++)
									{
										// Enabled
										if (!bool.TryParse(aEnabledZones[iZoneIndex].ToString(), out bTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read zone information: {1}", lRequestId.ToString("X8"), "UserAirconSettings.EnabledZones");
										else
										{
											lock (_oLockData)
											{
												if (_airConditionerZones.ContainsKey(iZoneIndex + 1))
													_airConditionerZones[iZoneIndex + 1].State = bTemp;
											}
										}

										// Temperature
										if (!double.TryParse(jsonResponse.events[iEvent].data.RemoteZoneInfo[iZoneIndex].LiveTemp_oC.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), string.Format("RemoteZoneInfo[{0}].LiveTemp_oC", iZoneIndex));
										else
										{
											lock (_oLockData)
											{
												if (_airConditionerZones.ContainsKey(iZoneIndex + 1))
													_airConditionerZones[iZoneIndex + 1].Temperature = dblTemp;
											}
										}

										// Cooling Set Temperature
										if (!double.TryParse(jsonResponse.events[iEvent].data.RemoteZoneInfo[iZoneIndex].TemperatureSetpoint_Cool_oC.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Cool_oC", iZoneIndex));
										else
										{
											lock (_oLockData)
											{
												if (_airConditionerZones.ContainsKey(iZoneIndex + 1))
													_airConditionerZones[iZoneIndex + 1].SetTemperatureCooling = dblTemp;
											}											
										}
										// Heating Set Temperature
										if (!double.TryParse(jsonResponse.events[iEvent].data.RemoteZoneInfo[iZoneIndex].TemperatureSetpoint_Heat_oC.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), string.Format("RemoteZoneInfo[{0}].TemperatureSetpoint_Heat_oC", iZoneIndex));
										else
										{
											lock (_oLockData)
											{
												if (_airConditionerZones.ContainsKey(iZoneIndex + 1))
													_airConditionerZones[iZoneIndex + 1].SetTemperatureHeating = dblTemp;
											}
										}

										// Position
										if (!double.TryParse(jsonResponse.events[iEvent].data.RemoteZoneInfo[iZoneIndex].ZonePosition.ToString(), out dblTemp))
											Logging.WriteDebugLog("Que.GetAirConditionerEvents() [0x{0}] Unable to read state information: {1}", lRequestId.ToString("X8"), string.Format("RemoteZoneInfo[{0}].ZonePosition", iZoneIndex));
										else
										{
											lock (_oLockData)
											{
												if (_airConditionerZones.ContainsKey(iZoneIndex + 1))
													_airConditionerZones[iZoneIndex + 1].Position = dblTemp;
											}
										}
									}
								}

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

			return bRetVal;
		}

		private async static void AirConditionerMonitor()
		{
			WaitHandle[] waitHandles = new WaitHandle[] { _eventStop , _eventUpdate };
			int iWaitHandle = 0, iWaitInterval = 5, iCommandAckRetries = 0;
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

					case 1: // Pull Update
						Logging.WriteDebugLog("Que.AirConditionerMonitor() Quick Update");

						_bCommandAckPending = true;
						iCommandAckRetries = _iCommandAckRetryCounter;

						Thread.Sleep(_iPostCommandSleepTimer * 1000);

						if (await GetAirConditionerEvents())
						{
							MQTTUpdateData();
							MQTT.Update(null);
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
						if (await GetAirConditionerEvents())
						{
							MQTTUpdateData();
							MQTT.Update(null);
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
					iWaitInterval = _iPollInterval;
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
				}
				else
					MQTT.SendMessage(string.Format("homeassistant/climate/actronque/zone{0}/config", iZone), "");
			}

			MQTT.Subscribe("actronque/mode/set");
			MQTT.Subscribe("actronque/fan/set");
			MQTT.Subscribe("actronque/temperature/set");
		}

		private static void MQTTUpdateData()
		{
			Logging.WriteDebugLog("Que.MQTTUpdateData()");

			if (_airConditionerData.LastUpdated == DateTime.MinValue)
			{
				Logging.WriteDebugLog("Que.MQTTUpdateData() Skipping update, No data received");
				return;
			}

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

			// Zones
			foreach (int iIndex in _airConditionerZones.Keys)
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
				MQTT.SendMessage("actronque/compressorcapacity", _airConditionerData.CompressorCapacity.ToString("N1"));

				// Compressor Power
				MQTT.SendMessage("actronque/compressorpower", _airConditionerData.CompressorPower.ToString("N2"));
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
					command.Data.command.Add("UserAirconSettings.FanMode", _airConditionerData.Continuous ? "AUTO+CONT" : "AUTO");

					break;

				case FanMode.Low:
					command.Data.command.Add("UserAirconSettings.FanMode", _airConditionerData.Continuous ? "LOW+CONT" : "LOW");

					break;

				case FanMode.Medium:
					command.Data.command.Add("UserAirconSettings.FanMode", _airConditionerData.Continuous ? "MED+CONT" : "MED");

					break;

				case FanMode.High:
					command.Data.command.Add("UserAirconSettings.FanMode", _airConditionerData.Continuous ? "HIGH+CONT" : "HIGH");

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
