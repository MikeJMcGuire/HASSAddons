using HMX.HASSActron;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

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
		private static string _strApplicationPath;
		private static Dictionary<int, Zone> _dZones = new Dictionary<int, Zone>();
		private const string _strZonesFile = "\\Zones.json";
		private static bool _bDataReceived = false;

		public static Dictionary<int, Zone> Zones
		{
			get { return _dZones; }
		}

		public static bool Configure(string strApplicationPath)
		{
			string strZoneDataFile;
			List<Zone> lZones;

			Logging.WriteDebugLog("AirConditioner.Configure()");

			_strApplicationPath = strApplicationPath;

			lock (_oLockData)
			{
				_airConditionerData.dtLastUpdate = DateTime.MinValue;
			}

			try
			{
				strZoneDataFile = strApplicationPath + _strZonesFile;

				if (File.Exists(strZoneDataFile))
				{
					lZones = JsonConvert.DeserializeObject<List<Zone>>(File.ReadAllText(strZoneDataFile));

					foreach (Zone zone in lZones)
					{
						Logging.WriteDebugLog("AirConditioner.Configure() Zone: {0}, Id: {1}", zone.Name, zone.Id);
						_dZones.Add(zone.Id, zone);
					}
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("AirConditioner.Configure()", eException, "Unable to read json file.");

				return false;
			}

			return true;
		}

		public static void PostData(AirConditionerData data)
		{
			string strZones;
			
			if (!_bDataReceived)
			{
				Logging.WriteDebugLog("AirConditioner.PostData() First Data Received");
				_bDataReceived = true;
			}
			else
				Logging.WriteDebugLog("AirConditioner.PostData()");

			lock (_oLockData)
			{
				_airConditionerData = data;
				_airConditionerData.dtLastUpdate = DateTime.Now;
				_airConditionerData.bPendingCommand = _bPendingCommand;
				_airConditionerData.bPendingZone = _bPendingZone;

				MQTT.SendMessage("actron/aircon/fanmode", Enum.GetName(typeof(FanSpeed), _airConditionerData.iFanSpeed).ToLower());
				MQTT.SendMessage("actron/aircon/mode", (_airConditionerData.bOn ? Enum.GetName(typeof(ModeMQTT), _airConditionerData.iMode).ToLower() : "off"));
				MQTT.SendMessage("actron/aircon/settemperature", _airConditionerData.dblSetTemperature.ToString());
				MQTT.SendMessage("actron/aircon/temperature", _airConditionerData.dblRoomTemperature.ToString());
				MQTT.SendMessage("actron/aircon/zone1", _airConditionerData.bZone1 ? "ON" : "OFF");
				MQTT.SendMessage("actron/aircon/zone2", _airConditionerData.bZone2 ? "ON" : "OFF");
				MQTT.SendMessage("actron/aircon/zone3", _airConditionerData.bZone3 ? "ON" : "OFF");
				MQTT.SendMessage("actron/aircon/zone4", _airConditionerData.bZone4 ? "ON" : "OFF");
			}

			lock (_oLockCommand)
			{
				if (!_bPendingCommand)
				{
					if (_airConditionerCommand.amOn != _airConditionerData.bOn || _airConditionerCommand.fanSpeed != _airConditionerData.iFanSpeed || _airConditionerCommand.mode != _airConditionerData.iMode || _airConditionerCommand.tempTarget != _airConditionerData.dblSetTemperature)
					{
						Logging.WriteDebugLog("AirConditioner.PostData() Updating Command");

						_airConditionerCommand.amOn = _airConditionerData.bOn;
						_airConditionerCommand.fanSpeed = _airConditionerData.iFanSpeed;
						_airConditionerCommand.mode = _airConditionerData.iMode;
						_airConditionerCommand.tempTarget = _airConditionerData.dblSetTemperature;
					}
				}

				if (!_bPendingZone)
				{
					strZones = string.Format("{0},{1},{2},{3},0,0,0,0", _airConditionerData.bZone1 ? "1" : "0", _airConditionerData.bZone2 ? "1" : "0", _airConditionerData.bZone3 ? "1" : "0", _airConditionerData.bZone4 ? "1" : "0");
					
					if (_airConditionerCommand.enabledZones != strZones)
					{
						Logging.WriteDebugLog("AirConditioner.PostData() Updating Zones");

						_airConditionerCommand.enabledZones = strZones;
					}
				}
			}
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
				}

				if (_airConditionerCommand.enabledZones != command.enabledZones)
				{
					Logging.WriteDebugLog("AirConditioner.PostCommand() [0x{0}] Zone Update", lRequestId.ToString("X8"));
					_bPendingZone = true;
				}

				_airConditionerCommand = command;
			}
		}

	
		public static void ChangeMode(long lRequestId, AirConditionerMode mode)
		{
			AirConditionerCommand command = new AirConditionerCommand();

			Logging.WriteDebugLog("AirConditioner.ChangeMode() [0x{0}] Changing Mode: {1}", lRequestId.ToString("X8"), Enum.GetName(typeof(AirConditionerMode), mode));

			lock (_oLockData)
			{
				command.amOn = (mode == AirConditionerMode.None ? false : true);
				command.tempTarget = _airConditionerData.dblSetTemperature;
				command.fanSpeed = _airConditionerData.iFanSpeed;
				command.mode = (mode == AirConditionerMode.None ? _airConditionerData.iMode : (int)mode);
				command.enabledZones = string.Format("{0},{1},{2},{3},0,0,0,0", _airConditionerData.bZone1 ? "1" : "0", _airConditionerData.bZone2 ? "1" : "0", _airConditionerData.bZone3 ? "1" : "0", _airConditionerData.bZone4 ? "1" : "0");
			}

			PostCommand(lRequestId, "System", command);
		}

		public static void ChangeFanSpeed(long lRequestId, FanSpeed speed)
		{
			AirConditionerCommand command = new AirConditionerCommand();

			Logging.WriteDebugLog("AirConditioner.ChangeFanSpeed() [0x{0}] Changing Fan Speed: {1}", lRequestId.ToString("X8"), Enum.GetName(typeof(FanSpeed), speed));

			lock (_oLockData)
			{
				command.amOn = _airConditionerCommand.amOn;
				command.tempTarget = _airConditionerCommand.tempTarget;
				command.fanSpeed = (int)speed;
				command.mode = _airConditionerCommand.mode;
				command.enabledZones = _airConditionerCommand.enabledZones;
			}

			PostCommand(lRequestId, "System", command);
		}

		public static void ChangeZone(long lRequestId, int iZone, bool bOn)
		{
			AirConditionerCommand command = new AirConditionerCommand();
			string[] strZones;

			Logging.WriteDebugLog("AirConditioner.ChangeZone() [0x{0}] Changing Zone: {1}", lRequestId.ToString("X8"), iZone);

			lock (_oLockData)
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

					command.enabledZones = string.Format("{0},{1},{2},{3},0,0,0,0", strZones[0], strZones[1], strZones[2], strZones[3]);
				}
			}

			PostCommand(lRequestId, "System", command);
		}

		public static void ChangeTemperature(long lRequestId, double dblTemperature)
		{
			AirConditionerCommand command = new AirConditionerCommand();

			Logging.WriteDebugLog("AirConditioner.ChangeTemperature() [0x{0}] Changing Temperature: {1}", lRequestId.ToString("X8"), dblTemperature);

			lock (_oLockData)
			{
				command.amOn = _airConditionerCommand.amOn;
				command.tempTarget = dblTemperature;
				command.fanSpeed = _airConditionerCommand.fanSpeed;
				command.mode = _airConditionerCommand.mode;
				command.enabledZones = _airConditionerCommand.enabledZones;
			}

			PostCommand(lRequestId, "System", command);
		}

	
	}
}
