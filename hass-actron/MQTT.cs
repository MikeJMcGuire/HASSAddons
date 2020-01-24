using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Client.Receiving;
using MQTTnet.Extensions.ManagedClient;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace HMX.HASSActron
{
    public class MQTT
    {
		public delegate void MessageHandler(string strTopic, string strPayload);

		private static IManagedMqttClient _mqtt = null;
		private static string _strClientId = "";
		private static Timer _timerMQTT = null;
		private static MessageHandler _messageHandler = null;
		
		public static async void StartMQTT(string strMQTTServer, string strClientId, string strUser, string strPassword, MessageHandler messageHandler)
		{
			Logging.WriteDebugLog("MQTT.StartMQTT()");

			if (strMQTTServer == null || strMQTTServer == "")
				return;

			_timerMQTT = new Timer(Update, null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));

			_strClientId = strClientId;
			_messageHandler = messageHandler;

			IManagedMqttClientOptions options = new ManagedMqttClientOptionsBuilder().WithAutoReconnectDelay(TimeSpan.FromSeconds(5)).WithClientOptions(new MqttClientOptionsBuilder()
				.WithClientId(_strClientId)
				.WithCredentials(strUser, strPassword)
				.WithTcpServer(strMQTTServer)
				.Build())
			.Build();

			_mqtt = new MqttFactory().CreateManagedMqttClient();

			_mqtt.ApplicationMessageReceivedHandler = new MqttApplicationMessageReceivedHandlerDelegate(MessageProcessor);

			await _mqtt.StartAsync(options);
		}

		private static void MessageProcessor(MqttApplicationMessageReceivedEventArgs e)
		{
			Logging.WriteDebugLog("MQTT.MessageProcessor() {0}", e.ApplicationMessage.Topic);

			if (_messageHandler != null)
				_messageHandler.Invoke(e.ApplicationMessage.Topic, ASCIIEncoding.ASCII.GetString(e.ApplicationMessage.Payload));
		}

		public static void Subscribe(string strTopicFormat, params object[] strParams)
		{
			Logging.WriteDebugLog("MQTT.Subscribe() {0}", string.Format(strTopicFormat, strParams));

			if (_mqtt != null)
				_mqtt.SubscribeAsync(string.Format(strTopicFormat, strParams));
		}

		private static void Update(object oState)
		{
			SendMessage(string.Format("{0}/status", _strClientId.ToLower()), "online");
		}

		public static void StopMQTT()
		{
			Logging.WriteDebugLog("MQTT.StopMQTT()");

			_timerMQTT.Dispose();

			SendMessage(string.Format("{0}/status", _strClientId.ToLower()), "offline");

			_mqtt.StopAsync();
			_mqtt.Dispose();

			_mqtt = null;
		}
		
		public static async void SendMessage(string strTopic, string strPayloadFormat, params object[] strParams)
		{
			Logging.WriteDebugLog("MQTT.SendMessage() {0}", strTopic);
			
			if (_mqtt != null)
			{
				MqttApplicationMessage message = new MqttApplicationMessageBuilder()
				.WithTopic(strTopic)
				.WithPayload(string.Format(strPayloadFormat, strParams))
				.WithExactlyOnceQoS()
				.WithRetainFlag()
				.Build();

				await _mqtt.PublishAsync(message);
			}
		}
	}
}
