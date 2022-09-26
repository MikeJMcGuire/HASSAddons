using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HMX.HASSActron
{
    public class MQTT
    {
		public delegate void MessageHandler(string strTopic, string strPayload);

		private static IManagedMqttClient _mqtt = null;
		private static string _strClientId = "";
		private static Timer _timerMQTT = null;
		private static MessageHandler _messageHandler = null;
		private static bool _bMQTTLogging = true;

		public static async void StartMQTT(string strMQTTServer, bool bMQTTTLS, bool bMQTTLogging, string strClientId, string strUser, string strPassword, MessageHandler messageHandler)
		{
			ManagedMqttClientOptions options;
			MqttClientOptionsBuilder clientOptions;
			MqttClientOptionsBuilderTlsParameters optionsTLS;
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
				optionsTLS = new MqttClientOptionsBuilderTlsParameters
				{
					AllowUntrustedCertificates = true,
					CertificateValidationHandler = delegate { return true; },
					IgnoreCertificateChainErrors = true,
					IgnoreCertificateRevocationErrors = true,
					UseTls = true,
					SslProtocol = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
				};

				clientOptions = clientOptions.WithTls(optionsTLS);
			}

			options = new ManagedMqttClientOptionsBuilder().WithAutoReconnectDelay(TimeSpan.FromSeconds(5)).WithClientOptions(clientOptions.Build()).Build();

			_mqtt = new MqttFactory().CreateManagedMqttClient();

			_mqtt.ApplicationMessageReceivedAsync += new Func<MqttApplicationMessageReceivedEventArgs, Task>(MessageProcessor);
			_mqtt.ConnectingFailedAsync += new Func<ConnectingFailedEventArgs, Task>(ConnectionProcessor);

			await _mqtt.StartAsync(options);
		}
		
		private static Task MessageProcessor(MqttApplicationMessageReceivedEventArgs e)
		{
			if (_bMQTTLogging)
				Logging.WriteDebugLog("MQTT.MessageProcessor() {0}", e.ApplicationMessage.Topic);

			if (_messageHandler != null)
				_messageHandler.Invoke(e.ApplicationMessage.Topic, Encoding.ASCII.GetString(e.ApplicationMessage.Payload));

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
			AirConditioner.MQTTUpdate();
		}

		public static void StopMQTT()
		{
			Logging.WriteDebugLog("MQTT.StopMQTT()");

			_timerMQTT.Dispose();

			SendMessage(string.Format("{0}/status", _strClientId.ToLower()), "offline");

			Thread.Sleep(500);

			_mqtt.StopAsync();
			_mqtt.Dispose();

			_mqtt = null;
		}
		
		public static async void SendMessage(string strTopic, string strPayloadFormat, params object[] strParams)
		{
			if (_bMQTTLogging)
				Logging.WriteDebugLog("MQTT.SendMessage() {0}", strTopic);
			
			if (_mqtt != null)
			{
				MqttApplicationMessage message = new MqttApplicationMessageBuilder()
				.WithTopic(strTopic)
				.WithPayload(string.Format(strPayloadFormat, strParams))
				.WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
				.WithRetainFlag()
				.Build();

				await _mqtt.EnqueueAsync(message);
			}
		}
	}
}
