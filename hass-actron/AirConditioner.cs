using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;

namespace HMX.HASSActron
{
	public class AirConditioner
	{
		private static AirConditionerData _airConditionerData;
		private static AirConditionerCommand _airConditionerCommand = new AirConditionerCommand();
		private static object _oLockCommand = new object();
		private static object _oLockData = new object();
		private static bool _bPendingCommand = false;
		private static bool _bPendingZone = false;
		private static Dictionary<int, Zone> _dZones = new Dictionary<int, Zone>();
		private static bool _bDataReceived = false;
		private static ManualResetEvent _eventCommand;
		private static DateTime _dtLastCommand = DateTime.MinValue;
		private static int _iSuppressTimer = 8; // Seconds

		public static Dictionary<int, Zone> Zones
		{
			get { return _dZones; }
		}

		public static DateTime LastUpdate
		{
			get { return _airConditionerData.dtLastUpdate; }
		}

		public static DateTime LastRequest
		{
			get { return _airConditionerData.dtLastRequest; }
		}

		public static ManualResetEvent EventCommand
		{
			get { return _eventCommand; }
		}
		
		public static bool Configure(IConfigurationRoot configuration)
		{
			Zone zone;

			Logging.WriteDebugLog("AirConditioner.Configure()");

			_eventCommand = new ManualResetEvent(false);

			lock (_oLockData)
			{
				_airConditionerData.dtLastUpdate = DateTime.MinValue;
			}

			try
			{
				foreach (IConfigurationSection zoneConfig in configuration.GetSection("Zones").GetChildren())
				{
					zone = new Zone(zoneConfig.GetValue<string>("Name"), zoneConfig.GetValue<int>("Id"));

					Logging.WriteDebugLog("AirConditioner.Configure() Zone: {0}, Id: {1}", zone.Name, zone.Id);

					_dZones.Add(zone.Id, zone);

					if (_dZones.Count > 8)
					{
						Logging.WriteDebugLog("AirConditioner.Configure() Maximum Zones Reached (8)");
						break;
					}
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("AirConditioner.Configure()", eException, "Unable to read zones.");

				return false;
			}

			return true;
		}

		public static void PostData(AirConditionerData data)
		{
			string strZones;
			DateTime dtLastRequest;

			if (!_bDataReceived)
			{
				Logging.WriteDebugLog("AirConditioner.PostData() First Data Received");
				_bDataReceived = true;
			}
			else
				Logging.WriteDebugLog("AirConditioner.PostData()");

			
			if (DateTime.Now.Subtract(_dtLastCommand) < TimeSpan.FromSeconds(_iSuppressTimer))
				Logging.WriteDebugLog("AirConditioner.PostData() Suppressing Data Update");
			else
			{
				lock (_oLockData)
				{
					dtLastRequest = _airConditionerData.dtLastRequest;

					_airConditionerData = data;

					_airConditionerData.dtLastUpdate = DateTime.Now;
					_airConditionerData.dtLastRequest = dtLastRequest;

					MQTT.SendMessage("actron/aircon/fanmode", Enum.GetName(typeof(FanSpeed), _airConditionerData.iFanSpeed).ToLower());
					MQTT.SendMessage("actron/aircon/mode", (_airConditionerData.bOn ? Enum.GetName(typeof(ModeMQTT), _airConditionerData.iMode).ToLower() : "off"));
					MQTT.SendMessage("actron/aircon/settemperature", _airConditionerData.dblSetTemperature.ToString());
					MQTT.SendMessage("actron/aircon/temperature", _airConditionerData.dblRoomTemperature.ToString());
					// Need to move to an array instead of 8 x boolean.
					if (_dZones.Count >= 1) MQTT.SendMessage("actron/aircon/zone1", _airConditionerData.bZone1 ? "ON" : "OFF");
					if (_dZones.Count >= 2) MQTT.SendMessage("actron/aircon/zone2", _airConditionerData.bZone2 ? "ON" : "OFF");
					if (_dZones.Count >= 3) MQTT.SendMessage("actron/aircon/zone3", _airConditionerData.bZone3 ? "ON" : "OFF");
					if (_dZones.Count >= 4) MQTT.SendMessage("actron/aircon/zone4", _airConditionerData.bZone4 ? "ON" : "OFF");
					if (_dZones.Count >= 5) MQTT.SendMessage("actron/aircon/zone5", _airConditionerData.bZone5 ? "ON" : "OFF");
					if (_dZones.Count >= 6) MQTT.SendMessage("actron/aircon/zone6", _airConditionerData.bZone6 ? "ON" : "OFF");
					if (_dZones.Count >= 7) MQTT.SendMessage("actron/aircon/zone7", _airConditionerData.bZone7 ? "ON" : "OFF");
					if (_dZones.Count >= 8) MQTT.SendMessage("actron/aircon/zone8", _airConditionerData.bZone8 ? "ON" : "OFF");

					if (Service.RegisterZoneTemperatures)
					{
						if (_dZones.Count >= 1) MQTT.SendMessage("actron/aircon/zone1/temperature", _airConditionerData.dblZone1Temperature.ToString());
						if (_dZones.Count >= 2) MQTT.SendMessage("actron/aircon/zone2/temperature", _airConditionerData.dblZone2Temperature.ToString());
						if (_dZones.Count >= 3) MQTT.SendMessage("actron/aircon/zone3/temperature", _airConditionerData.dblZone3Temperature.ToString());
						if (_dZones.Count >= 4) MQTT.SendMessage("actron/aircon/zone4/temperature", _airConditionerData.dblZone4Temperature.ToString());
						if (_dZones.Count >= 5) MQTT.SendMessage("actron/aircon/zone5/temperature", _airConditionerData.dblZone5Temperature.ToString());
						if (_dZones.Count >= 6) MQTT.SendMessage("actron/aircon/zone6/temperature", _airConditionerData.dblZone6Temperature.ToString());
						if (_dZones.Count >= 7) MQTT.SendMessage("actron/aircon/zone7/temperature", _airConditionerData.dblZone7Temperature.ToString());
						if (_dZones.Count >= 8) MQTT.SendMessage("actron/aircon/zone8/temperature", _airConditionerData.dblZone8Temperature.ToString());
					}

					switch (_airConditionerData.iCompressorActivity)
					{
						case 0:
							MQTT.SendMessage("actron/aircon/compressor", "heating");
							break;

						case 1:
							MQTT.SendMessage("actron/aircon/compressor", "cooling");
							break;

						case 2:
							if (_airConditionerData.bOn)
								MQTT.SendMessage("actron/aircon/compressor", "idle");
							else
								MQTT.SendMessage("actron/aircon/compressor", "off");

							break;

						default:
							MQTT.SendMessage("actron/aircon/compressor", "off");
							break;
					}
				}
			}

			lock (_oLockCommand)
			{
				if (!_bPendingCommand)
				{
					if (DateTime.Now.Subtract(_dtLastCommand) < TimeSpan.FromSeconds(_iSuppressTimer))
						Logging.WriteDebugLog("AirConditioner.PostData() Suppressing Command Update");
					else if (_airConditionerCommand.amOn != _airConditionerData.bOn || _airConditionerCommand.fanSpeed != _airConditionerData.iFanSpeed || _airConditionerCommand.mode != _airConditionerData.iMode || _airConditionerCommand.tempTarget != _airConditionerData.dblSetTemperature)
					{
						Logging.WriteDebugLog("AirConditioner.PostData() Updating Command");

						_airConditionerCommand.amOn = _airConditionerData.bOn;
						_airConditionerCommand.fanSpeed = _airConditionerData.iFanSpeed;
						_airConditionerCommand.mode = _airConditionerData.iMode;
						_airConditionerCommand.tempTarget = _airConditionerData.dblSetTemperature;
					}
				}

				if (DateTime.Now.Subtract(_dtLastCommand) < TimeSpan.FromSeconds(_iSuppressTimer))
					Logging.WriteDebugLog("AirConditioner.PostData() Suppressing Zone Update");
				else if (!_bPendingZone)
				{
					strZones = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}", _airConditionerData.bZone1 ? "1" : "0", _airConditionerData.bZone2 ? "1" : "0", _airConditionerData.bZone3 ? "1" : "0", _airConditionerData.bZone4 ? "1" : "0", _airConditionerData.bZone5 ? "1" : "0", _airConditionerData.bZone6 ? "1" : "0", _airConditionerData.bZone7 ? "1" : "0", _airConditionerData.bZone8 ? "1" : "0");
					
					if (_airConditionerCommand.enabledZones != strZones)
					{
						Logging.WriteDebugLog("AirConditioner.PostData() Updating Zones");

						_airConditionerCommand.enabledZones = strZones;
					}
				}
			}

			MQTT.Update(null);
		}

		public static AirConditionerCommand GetCommand(out string strCommandType)
		{
			Logging.WriteDebugLog("AirConditioner.GetCommand()");

			lock (_oLockCommand)
			{
				if (_bPendingCommand)
				{
					Logging.WriteDebugLog("AirConditioner.GetCommand() Command 4 (Command) Released");
					strCommandType = "4";
					_bPendingCommand = false;
				}
				else if (_bPendingZone)
				{
					Logging.WriteDebugLog("AirConditioner.GetCommand() Command 5 (Zone) Released");
					strCommandType = "5";
					_bPendingZone = false;
				}
				else
					strCommandType = "";

				if (!_bPendingCommand & !_bPendingZone)
				{
					Logging.WriteDebugLog("AirConditioner.GetCommand() Command Event Reset");
					_eventCommand.Reset();
				}

				return _airConditionerCommand;
			}
		}

		public static void PostCommand(long lRequestId, string strUser, AirConditionerCommand command)
		{
			Logging.WriteDebugLog("AirConditioner.PostCommand() [0x{0}]", lRequestId.ToString("X8"));

			lock (_oLockCommand)
			{
				if (_airConditionerCommand.amOn != command.amOn || _airConditionerCommand.fanSpeed != command.fanSpeed || _airConditionerCommand.mode != command.mode || _airConditionerCommand.tempTarget != command.tempTarget)
				{
					Logging.WriteDebugLog("AirConditioner.PostCommand() [0x{0}] Command Update", lRequestId.ToString("X8"));
					_bPendingCommand = true;
					Logging.WriteDebugLog("AirConditioner.GetCommand() Command Event Set");
					_eventCommand.Set();

					_dtLastCommand = DateTime.Now;
				}

				if (_airConditionerCommand.enabledZones != command.enabledZones)
				{
					Logging.WriteDebugLog("AirConditioner.PostCommand() [0x{0}] Zone Update", lRequestId.ToString("X8"));
					_bPendingZone = true;
					Logging.WriteDebugLog("AirConditioner.GetCommand() Command Event Set");
					_eventCommand.Set();

					_dtLastCommand = DateTime.Now;
				}

				_airConditionerCommand = command;
			}
		}
	
		public static void ChangeMode(long lRequestId, AirConditionerMode mode)
		{
			AirConditionerCommand command = new AirConditionerCommand();

			Logging.WriteDebugLog("AirConditioner.ChangeMode() [0x{0}] Changing Mode: {1}", lRequestId.ToString("X8"), Enum.GetName(typeof(AirConditionerMode), mode));

			lock (_oLockCommand)
			{
				command.amOn = (mode == AirConditionerMode.None ? false : true);
				command.tempTarget = _airConditionerCommand.tempTarget;
				command.fanSpeed = _airConditionerCommand.fanSpeed;
				command.mode = (mode == AirConditionerMode.None ? _airConditionerData.iMode : (int)mode);
				command.enabledZones = _airConditionerCommand.enabledZones;
			}

			MQTT.SendMessage("actron/aircon/mode", (mode != AirConditionerMode.None ? Enum.GetName(typeof(ModeMQTT), mode).ToLower() : "off"));

			PostCommand(lRequestId, "System", command);
		}

		public static void ChangeFanSpeed(long lRequestId, FanSpeed speed)
		{
			AirConditionerCommand command = new AirConditionerCommand();

			Logging.WriteDebugLog("AirConditioner.ChangeFanSpeed() [0x{0}] Changing Fan Speed: {1}", lRequestId.ToString("X8"), Enum.GetName(typeof(FanSpeed), speed));

			lock (_oLockCommand)
			{
				command.amOn = _airConditionerCommand.amOn;
				command.tempTarget = _airConditionerCommand.tempTarget;
				command.fanSpeed = (int)speed;
				command.mode = _airConditionerCommand.mode;
				command.enabledZones = _airConditionerCommand.enabledZones;
			}

			MQTT.SendMessage("actron/aircon/fanmode", Enum.GetName(typeof(FanSpeed), speed).ToLower());

			PostCommand(lRequestId, "System", command);
		}

		public static void ChangeZone(long lRequestId, int iZone, bool bOn)
		{
			AirConditionerCommand command = new AirConditionerCommand();
			string[] strZones;

			Logging.WriteDebugLog("AirConditioner.ChangeZone() [0x{0}] Changing Zone: {1}", lRequestId.ToString("X8"), iZone);

			lock (_oLockCommand)
			{
				command.amOn = _airConditionerCommand.amOn;
				command.tempTarget = _airConditionerCommand.tempTarget;
				command.fanSpeed = _airConditionerCommand.fanSpeed;
				command.mode = _airConditionerCommand.mode;

				strZones = _airConditionerCommand.enabledZones.Split(new char[] { ',' });
				if (strZones.Length != 8 | iZone < 1 | iZone > 8)
					command.enabledZones = _airConditionerCommand.enabledZones;
				else
				{
					strZones[iZone - 1] = bOn ? "1" : "0";

					command.enabledZones = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}", strZones[0], strZones[1], strZones[2], strZones[3], strZones[4], strZones[5], strZones[6], strZones[7]);

					MQTT.SendMessage(string.Format("actron/aircon/zone{0}", iZone), bOn ? "ON" : "OFF");
				}
			}

			PostCommand(lRequestId, "System", command);
		}

		public static void ChangeTemperature(long lRequestId, double dblTemperature)
		{
			AirConditionerCommand command = new AirConditionerCommand();

			Logging.WriteDebugLog("AirConditioner.ChangeTemperature() [0x{0}] Changing Temperature: {1}", lRequestId.ToString("X8"), dblTemperature);

			lock (_oLockCommand)
			{
				command.amOn = _airConditionerCommand.amOn;
				command.tempTarget = dblTemperature;
				command.fanSpeed = _airConditionerCommand.fanSpeed;
				command.mode = _airConditionerCommand.mode;
				command.enabledZones = _airConditionerCommand.enabledZones;
			}

			MQTT.SendMessage("actron/aircon/settemperature", command.tempTarget.ToString());

			PostCommand(lRequestId, "System", command);
		}
		
		public static void UpdateRequestTime()
		{
			lock (_oLockData)
			{
				_airConditionerData.dtLastRequest = DateTime.Now;
			}
		}
	}
}
