using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HMX.HASSActron
{
	internal class Proxy
	{
		private static HttpClient _httpClient = null;
		private static int _iWaitTime = 5000; //ms
		private static int _iDefaultTTL = 60; //s
		private static IPAddress _ipProxy = null;
		private static object _oProxyLock = new object();
		private static DateTime _dtProxyIPValidity = DateTime.MinValue;

		static Proxy()
		{
			HttpClientHandler httpClientHandler = null;

			Logging.WriteDebugLog("Proxy.Proxy()");

			httpClientHandler = new HttpClientHandler();
			httpClientHandler.Proxy = null;
			httpClientHandler.UseProxy = false;

			_httpClient = new HttpClient(httpClientHandler);

			_httpClient.DefaultRequestHeaders.Connection.Add("close");

			Logging.WriteDebugLog("Proxy.Proxy() Complete");
		}

		public static async Task<IPAddress> GetTargetAddress(string strHost)
		{
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			dynamic dResults;
			IPAddress ipResult = null;

			Logging.WriteDebugLog("Proxy.GetTargetAddress()");

			lock (_oProxyLock)
			{
				if (_dtProxyIPValidity >= DateTime.Now)
				{
					Logging.WriteDebugLog("Proxy.GetTargetAddress() Using cached entry: {0}", _ipProxy.ToString());
					ipResult = _ipProxy;
					goto Cleanup;
				}
			}

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(_iWaitTime);

				httpResponse = await _httpClient.GetAsync(string.Format("https://dns.google.com/resolve?name={0}&type=A", strHost), cancellationToken.Token);

				if (httpResponse.IsSuccessStatusCode)
				{
					dResults = JsonConvert.DeserializeObject(await httpResponse.Content.ReadAsStringAsync());

					foreach (dynamic dAnswer in dResults.Answer)
					{
						if (dAnswer.type == "1")
						{
							Logging.WriteDebugLog("Proxy.GetTargetAddress() Name: {0}, IP: {1}", dAnswer.name.ToString(), dAnswer.data.ToString());
							ipResult = IPAddress.Parse(dAnswer.data.ToString());
							break;
						}
					}
				}
				else
				{
					Logging.WriteDebugLog("Proxy.GetTargetAddress() Unable to process HTTP response: {0}/{1}", httpResponse.StatusCode.ToString(), httpResponse.ReasonPhrase);

					goto Cleanup;
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Proxy.GetTargetAddress()", eException, "Unable to process API HTTP response.");
				goto Cleanup;
			}

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();

			lock (_oProxyLock)
			{
				if (ipResult != null)
				{
					_ipProxy = ipResult;
					_dtProxyIPValidity = DateTime.Now.AddSeconds(_iDefaultTTL);
				}
			}

			return ipResult;
		}

		public static async void ForwardDataToOriginalWebService(string strUserAgent, string strContentType, string strNinjaToken, string strHost, string strPath, string strData)
		{
			HttpClient httpClient = null;
			HttpClientHandler httpClientHandler;
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			StringContent stringContent;
			IPAddress ipProxy; 
			string strContent;
			string strURL = string.Format("http://{0}{1}", strHost, strPath);

			Logging.WriteDebugLog("Proxy.ForwardDataToOriginalWebService() URL: " + strURL);

			ipProxy = await GetTargetAddress(strHost);
			if (ipProxy == null)
			{
				Logging.WriteDebugLog("Proxy.ForwardDataToOriginalWebService() Abort (no proxy)");
				return;
			}

			httpClientHandler = new HttpClientHandler();
			httpClientHandler.Proxy = new WebProxy(string.Format("http://{0}:80", ipProxy.ToString()));
			httpClientHandler.UseProxy = true;

			httpClient = new HttpClient(httpClientHandler);

			httpClient.DefaultRequestHeaders.Connection.Add("close");
			httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(strUserAgent);
			httpClient.DefaultRequestHeaders.Add("X-Ninja-Token", strNinjaToken);

			stringContent = new StringContent(strData);
			stringContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(strContentType);

			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(_iWaitTime);

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
				Logging.WriteDebugLogError("Proxy.ForwardDataToOriginalWebService()", eException, "Unable to process API HTTP response.");
			}

			cancellationToken?.Dispose();
			httpResponse?.Dispose();
			httpClient?.Dispose();
		}

		public static async Task<ProxyResponse> ForwardRequestToOriginalWebService(string strMethod, string strUserAgent, string strHost, string strPath)
		{
			ProxyResponse response = new ProxyResponse();
			HttpClient httpClient = null;
			HttpClientHandler httpClientHandler;
			HttpResponseMessage httpResponse = null;
			CancellationTokenSource cancellationToken = null;
			IPAddress ipProxy;
			string strURL = string.Format("http://{0}{1}", strHost, strPath);

			Logging.WriteDebugLog("Proxy.ForwardRequestToOriginalWebService() URL: " + strURL);

			response.ProxySuccessful = true;

			ipProxy = await GetTargetAddress(strHost);
			if (ipProxy == null)
			{
				Logging.WriteDebugLog("Proxy.ForwardRequestToOriginalWebService() Abort (no proxy)");
				response.ProxySuccessful = false;
				goto Cleanup;
			}

			httpClientHandler = new HttpClientHandler();
			httpClientHandler.Proxy = new WebProxy(string.Format("http://{0}:80", ipProxy.ToString()));
			httpClientHandler.UseProxy = true;

			httpClient = new HttpClient(httpClientHandler);

			httpClient.DefaultRequestHeaders.Connection.Add("close");
			httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(strUserAgent);
			
			try
			{
				cancellationToken = new CancellationTokenSource();
				cancellationToken.CancelAfter(_iWaitTime);

				switch (strMethod)
				{
					case "DELETE":
						httpResponse = await httpClient.DeleteAsync(strURL, cancellationToken.Token);

						break;

					case "GET":
						httpResponse = await httpClient.GetAsync(strURL, cancellationToken.Token);

						break;

					default:
						response.ProxySuccessful = false;
						goto Cleanup;						
				}				

				if (httpResponse.IsSuccessStatusCode)
				{
					response.ResponseCode = httpResponse.StatusCode;
					response.Response = await httpResponse.Content.ReadAsStringAsync();
					Logging.WriteDebugLog("Response: " + response.Response);
				}
				else
				{
					response.ResponseCode = httpResponse.StatusCode;
					Logging.WriteDebugLog("Response: " + httpResponse.StatusCode);
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("Proxy.ForwardRequestToOriginalWebService()", eException, "Unable to process API HTTP response.");
			}

		Cleanup:
			cancellationToken?.Dispose();
			httpResponse?.Dispose();
			httpClient?.Dispose();

			return response;
		}
	}
}
