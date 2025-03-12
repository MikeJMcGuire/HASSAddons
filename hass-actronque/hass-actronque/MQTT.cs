using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HMX.HASSActronQue
{
    public class MQTT
    {
		public delegate void MessageHandler(string strTopic, string strPayload);

		private static IManagedMqttClient _mqtt = null;
		private static string _strClientId = "";
		private static Timer _timerMQTT = null;
		private static MessageHandler _messageHandler = null;
		private static int _iLastUpdateThreshold = 5; // Minutes
		private static bool _bMQTTLogging = true;

		public static async void StartMQTT(string strMQTTServer, bool bMQTTLogging, bool bMQTTTLS, string strClientId, string strUser, string strPassword, MessageHandler messageHandler)
		{
			ManagedMqttClientOptions options;
			MqttClientOptionsBuilder clientOptions;
			int iPort = 0;
			string[] strMQTTServerArray;
			string strMQTTBroker;

			Logging.WriteDebugLog("MQTT.StartMQTT()");

			if (strMQTTServer == null || strMQTTServer == "")
				return;

			_timerMQTT = new Timer(Update, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));

			_strClientId = strClientId;
			_messageHandler = messageHandler;
			_bMQTTLogging = bMQTTLogging;

			if (strMQTTServer.Contains(":"))
			{
				strMQTTServerArray = strMQTTServer.Split(new char[] { ':' });
				if (strMQTTServerArray.Length != 2)
				{
					Logging.WriteDebugLog("MQTT.StartMQTT() MQTTBroker field has incorrect syntax (host or host:port)");
					return;
				}

				if (!int.TryParse(strMQTTServerArray[1], out iPort) || iPort == 0)
				{
					Logging.WriteDebugLog("MQTT.StartMQTT() MQTTBroker field has incorrect syntax - port not non-zero numeric (host or host:port)");
					return;
				}

				if (strMQTTServerArray[0].Length == 0)
				{
					Logging.WriteDebugLog("MQTT.StartMQTT() MQTTBroker field has incorrect syntax - missing host (host or host:port)");
					return;
				}

				strMQTTBroker = strMQTTServerArray[0];

				Logging.WriteDebugLog("MQTT.StartMQTT() Host: {0}, Port: {1}", strMQTTBroker, iPort);
			}
			else
			{
				strMQTTBroker = strMQTTServer;

				Logging.WriteDebugLog("MQTT.StartMQTT() Host: {0}", strMQTTBroker);
			}

			clientOptions = new MqttClientOptionsBuilder().WithClientId(_strClientId).WithTcpServer(strMQTTBroker, (iPort == 0 ? null : iPort));
			if (strUser != "")
				clientOptions = clientOptions.WithCredentials(strUser, strPassword);
			if (bMQTTTLS)
			{
				clientOptions = clientOptions.WithTlsOptions(
					o =>
					{
						o.WithAllowUntrustedCertificates(true);
						o.WithCertificateValidationHandler(_ => true);
						o.WithIgnoreCertificateChainErrors(true);
						o.WithIgnoreCertificateRevocationErrors(true);
						o.WithSslProtocols(System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13);
					});
			}

			options = new ManagedMqttClientOptionsBuilder().WithAutoReconnectDelay(TimeSpan.FromSeconds(5)).WithClientOptions(clientOptions.Build()).Build();

			_mqtt = new MqttFactory().CreateManagedMqttClient();

			_mqtt.ApplicationMessageReceivedAsync += new Func<MqttApplicationMessageReceivedEventArgs, Task>(MessageProcessor);
			_mqtt.ConnectingFailedAsync += new Func<ConnectingFailedEventArgs, Task>(ConnectionProcessor);

			await _mqtt.StartAsync(options);
		}

		private static Task MessageProcessor(MqttApplicationMessageReceivedEventArgs e)
		{
			Logging.WriteDebugLog("MQTT.MessageProcessor() {0}", e.ApplicationMessage.Topic);

			try
			{
				if (_messageHandler != null)
					_messageHandler.Invoke(e.ApplicationMessage.Topic, Encoding.ASCII.GetString(e.ApplicationMessage.PayloadSegment.ToArray()));
			}
			catch { }

			return Task.CompletedTask;
		}

		private static Task ConnectionProcessor(ConnectingFailedEventArgs e)
		{
			string strMessage = "MQTT.ConnectionProcessor() Unable to connect to MQTT broker: {0}";

			if (e.Exception != null)
			{
				if (e.Exception.InnerException != null)
					Logging.WriteDebugLog(strMessage, e.Exception.Message + " " + e.Exception.InnerException.Message);
				else
					Logging.WriteDebugLog(strMessage, e.Exception.Message);
			}
			else
				Logging.WriteDebugLog(strMessage, "unspecified error");

			return Task.CompletedTask;
		}

		public static void Subscribe(string strTopicFormat, params object[] strParams)
		{
			Logging.WriteDebugLog("MQTT.Subscribe() {0}", string.Format(strTopicFormat, strParams));

			if (_mqtt != null)
				_mqtt.SubscribeAsync(string.Format(strTopicFormat, strParams));
		}

		public static void Update(object oState)
		{
			foreach (AirConditionerUnit unit in Que.Units.Values)
			{
				if (DateTime.Now >= unit.Data.LastUpdated.AddMinutes(_iLastUpdateThreshold))
					SendMessage(string.Format("{0}{1}/status", _strClientId.ToLower(), unit.Serial), "offline");
				else
					SendMessage(string.Format("{0}{1}/status", _strClientId.ToLower(), unit.Serial), "online");
			}
		}

		public static async void StopMQTT()
		{
			Logging.WriteDebugLog("MQTT.StopMQTT()");

			_timerMQTT.Dispose();

			foreach (AirConditionerUnit unit in Que.Units.Values)
				SendMessage(string.Format("{0}{1}/status", _strClientId.ToLower(), unit.Serial), "offline");

			Thread.Sleep(500);

			await _mqtt.StopAsync();

			Thread.Sleep(50);

			_mqtt.Dispose();

			_mqtt = null;
		}
		
		public static async void SendMessage(string strTopic, string strPayloadFormat, params object[] strParams)
		{
			if (_bMQTTLogging) 
				Logging.WriteDebugLog("MQTT.SendMessage() {0}", strTopic);

			if (_mqtt != null)
			{
				try
				{
					MqttApplicationMessage message = new MqttApplicationMessageBuilder()
						.WithTopic(strTopic)
						.WithPayload(string.Format(strPayloadFormat, strParams))
						.WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
						.WithRetainFlag()
						.Build();

					await _mqtt.EnqueueAsync(message);
				}
				catch (Exception eException)
				{
					Logging.WriteDebugLogError("MQTT.SendMessage()", eException, "Unable to send MQTT message.");
				}
			}
		}
	}
}
