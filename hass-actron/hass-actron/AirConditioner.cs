using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HMX.HASSActron
{
	public class AirConditioner
	{
		// Fix: Relook at data supression. Break timer early if expected data received.

		// Static
		private static Dictionary<string, AirConditioner> _dUnits = new Dictionary<string, AirConditioner>();
		private static string _strDeviceName = "Air Conditioner";
		private static string _strDeviceNameMQTT = "Actron Air Conditioner";
		private static int _iLastUpdateThreshold = 10; // Minutes
		private static string _strDefaultUnit = "Default";
		// Instance
		private AirConditionerData _airConditionerData;
		private AirConditionerCommand _airConditionerCommand = new AirConditionerCommand();
		private object _oLockCommand = new object();
		private object _oLockData = new object();
		private bool _bPendingCommand = false;
		private bool _bPendingZone = false;
		private Dictionary<int, Zone> _dZones = new Dictionary<int, Zone>();
		private bool _bDataReceived = false;
		private ManualResetEvent _eventCommand;
		private DateTime _dtLastCommand = DateTime.MinValue;
		private int _iSuppressTimer = 8; // Seconds
		private Timer _timerPoll;
		private string _strUnit, _strClientId;

		public Dictionary<int, Zone> Zones
		{
			get { return _dZones; }
		}

		public bool DataReceived
		{
			get { return _bDataReceived; }
		}

		public string Unit
		{
			get { return _strUnit; }
		}

		public DateTime LastUpdate
		{
			get { return _airConditionerData.dtLastUpdate; }
		}

		public DateTime LastRequest
		{
			get { return _airConditionerData.dtLastRequest; }
		}

		public ManualResetEvent EventCommand
		{
			get { return _eventCommand; }
		}

		public string ClientId
		{
			get { return _strClientId; }
		}

		public static void MQTTUpdate()
		{
			foreach (AirConditioner unit in _dUnits.Values)
			{
				if (DateTime.Now >= unit.LastUpdate.AddMinutes(_iLastUpdateThreshold))
					MQTT.SendMessage(string.Format("{0}/status", unit.ClientId), "offline");
				else
					MQTT.SendMessage(string.Format("{0}/status", unit.ClientId), "online");
			}
		}		

		public static bool Configure(IConfigurationRoot configuration)
		{
			Logging.WriteDebugLog("AirConditioner.Configure()");

			try
			{
				foreach (IConfigurationSection unit in configuration.GetSection("MultipleUnits")?.GetChildren())
				{
					Logging.WriteDebugLog("AirConditioner.Configure() Unit: {0}", unit.Value);

					_dUnits.Add(unit.Value, new AirConditioner(unit.Value, _dUnits.Count, configuration));
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("AirConditioner.Configure()", eException, "Unable to read units.");

				return false;
			}

			if (_dUnits.Count > 0)
				Logging.WriteDebugLog("AirConditioner.Configure() Multiple Unit Mode");
			else
				_dUnits.Add(_strDefaultUnit, new AirConditioner(_strDefaultUnit, 0, configuration));

			return true;
		}

		public AirConditioner(string strUnit, int iUnitIndex, IConfigurationRoot configuration)
		{
			Zone zone;
			string strZoneUnit;
			
			Logging.WriteDebugLog("AirConditioner.AirConditioner() Unit: {0}", strUnit);

			_eventCommand = new ManualResetEvent(false);
			_strUnit = strUnit;

			lock (_oLockData)
			{
				_airConditionerData.dtLastUpdate = DateTime.Now;
			}

			try
			{
				foreach (IConfigurationSection zoneConfig in configuration.GetSection("Zones").GetChildren())
				{
					zone = new Zone(zoneConfig.GetValue<string>("Name"), zoneConfig.GetValue<int>("Id"));

					strZoneUnit = zoneConfig.GetValue<string>("Unit") ?? _strDefaultUnit;

					if (strZoneUnit == _strUnit)
					{
						Logging.WriteDebugLog("AirConditioner.AirConditioner() Zone: {0}, Id: {1}", zone.Name, zone.Id);

						_dZones.Add(zone.Id, zone);

						if (_dZones.Count > 8)
						{
							Logging.WriteDebugLog("AirConditioner.AirConditioner() Maximum Zones Reached (8)");
							break;
						}
					}					
				}

				if (_dZones.Count == 0)
				{
					Logging.WriteDebugLog("AirConditioner.AirConditioner() No zones defined for this unit");
				}
			}
			catch (Exception eException)
			{
				Logging.WriteDebugLogError("AirConditioner.AirConditioner()", eException, "Unable to read zones.");
			}

			if (_strUnit == _strDefaultUnit)
			{
				_strClientId = Service.ServiceName.ToLower();

				MQTT.SendMessage("homeassistant/climate/actronaircon/config", "{{\"name\":\"{1}\",\"unique_id\":\"{0}-AC\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"modes\":[\"off\",\"auto\",\"cool\",\"fan_only\",\"heat\"],\"fan_modes\":[\"high\",\"medium\",\"low\"],\"mode_command_topic\":\"actron/aircon/Default/mode/set\",\"temperature_command_topic\":\"actron/aircon/Default/temperature/set\",\"fan_mode_command_topic\":\"actron/aircon/Default/fan/set\",\"min_temp\":\"12\",\"max_temp\":\"30\",\"temp_step\":\"0.5\",\"fan_mode_state_topic\":\"actron/aircon/Default/fanmode\",\"action_topic\":\"actron/aircon/Default/compressor\",\"temperature_state_topic\":\"actron/aircon/Default/settemperature\",\"mode_state_topic\":\"actron/aircon/Default/mode\",\"current_temperature_topic\":\"actron/aircon/Default/temperature\",\"availability_topic\":\"{0}/status\"}}", _strClientId, _strDeviceName, _strDeviceNameMQTT);

				MQTT.SendMessage("homeassistant/sensor/actron/esp/config", "{{\"name\":\"{1} ESP\",\"unique_id\":\"{0}-AC-ESP\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actron/aircon/Default/esp\",\"availability_topic\":\"{0}/status\"}}", _strClientId, _strDeviceName, _strDeviceNameMQTT);
				MQTT.SendMessage("homeassistant/sensor/actron/fancont/config", "{{\"name\":\"{1} Fan Continuous\",\"unique_id\":\"{0}-AC-FANC\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actron/aircon/Default/fancont\",\"availability_topic\":\"{0}/status\"}}", _strClientId, _strDeviceName, _strDeviceNameMQTT);
				MQTT.SendMessage("homeassistant/sensor/actron/temperature/config", "{{\"name\":\"{1} Temperature\",\"unique_id\":\"{0}-AC-t\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actron/aircon/Default/temperature\",\"unit_of_measurement\":\"\u00B0C\",\"availability_topic\":\"{0}/status\"}}", _strClientId, _strDeviceName, _strDeviceNameMQTT);

				foreach (int iZone in _dZones.Keys)
				{
					MQTT.SendMessage(string.Format("homeassistant/switch/actron/airconzone{0}/config", iZone), "{{\"name\":\"{0} Zone\",\"unique_id\":\"{2}-z{1}s\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{3}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actron/aircon/Default/zone{1}\",\"command_topic\":\"actron/aircon/Default/zone{1}/set\",\"payload_on\":\"ON\",\"payload_off\":\"OFF\",\"state_on\":\"ON\",\"state_off\":\"OFF\",\"availability_topic\":\"{2}/status\"}}", _dZones[iZone].Name, iZone, _strClientId, _strDeviceNameMQTT);
					MQTT.Subscribe("actron/aircon/{0}/zone{1}/set", _strUnit, iZone);

					if (Service.RegisterZoneTemperatures)
						MQTT.SendMessage(string.Format("homeassistant/sensor/actron/airconzone{0}/config", iZone), "{{\"name\":\"{0}\",\"unique_id\":\"{2}-z{1}t\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{3}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actron/aircon/Default/zone{1}/temperature\",\"unit_of_measurement\":\"\u00B0C\",\"availability_topic\":\"{2}/status\"}}", _dZones[iZone].Name, iZone, _strClientId, _strDeviceNameMQTT);
					else
						MQTT.SendMessage(string.Format("homeassistant/sensor/actron/airconzone{0}/config", iZone), "{{}}"); // Clear existing devices
				}

				MQTT.Subscribe("actron/aircon/{0}/mode/set", _strUnit);
				MQTT.Subscribe("actron/aircon/{0}/fan/set", _strUnit);
				MQTT.Subscribe("actron/aircon/{0}/temperature/set", _strUnit);
			}
			else
			{
				_strClientId = Service.ServiceName.ToLower() + _strUnit.ToLower();

				MQTT.SendMessage(string.Format("homeassistant/climate/actronaircon/{0}/config", _strUnit), "{{\"name\":\"{1} {3}\",\"unique_id\":\"{0}-AC\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"modes\":[\"off\",\"auto\",\"cool\",\"fan_only\",\"heat\"],\"fan_modes\":[\"high\",\"medium\",\"low\"],\"mode_command_topic\":\"actron/aircon/{3}/mode/set\",\"temperature_command_topic\":\"actron/aircon/{3}/temperature/set\",\"fan_mode_command_topic\":\"actron/aircon/{3}/fan/set\",\"min_temp\":\"12\",\"max_temp\":\"30\",\"temp_step\":\"0.5\",\"fan_mode_state_topic\":\"actron/aircon/{3}/fanmode\",\"action_topic\":\"actron/aircon/{3}/compressor\",\"temperature_state_topic\":\"actron/aircon/{3}/settemperature\",\"mode_state_topic\":\"actron/aircon/{3}/mode\",\"current_temperature_topic\":\"actron/aircon/{3}/temperature\",\"availability_topic\":\"{0}/status\"}}", _strClientId, _strDeviceName, _strDeviceNameMQTT, _strUnit);

				MQTT.SendMessage(string.Format("homeassistant/sensor/actron{0}/esp/config", _strUnit), "{{\"name\":\"{1} ESP\",\"unique_id\":\"{0}-AC-ESP\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actron/aircon/{3}/esp\",\"availability_topic\":\"{0}/status\"}}", _strClientId, _strDeviceName, _strDeviceNameMQTT, _strUnit);
				MQTT.SendMessage(string.Format("homeassistant/sensor/actron{0}/fancont/config", _strUnit), "{{\"name\":\"{1} Fan Continuous\",\"unique_id\":\"{0}-AC-FANC\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actron/aircon/{3}/fancont\",\"availability_topic\":\"{0}/status\"}}", _strClientId, _strDeviceName, _strDeviceNameMQTT, _strUnit);
				MQTT.SendMessage(string.Format("homeassistant/sensor/actron{0}/temperature/config", _strUnit), "{{\"name\":\"{1} Temperature\",\"unique_id\":\"{0}-AC-t\",\"device\":{{\"identifiers\":[\"{0}\"],\"name\":\"{2}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actron/aircon/{3}/temperature\",\"unit_of_measurement\":\"\u00B0C\",\"availability_topic\":\"{0}/status\"}}", _strClientId, _strDeviceName, _strDeviceNameMQTT);

				foreach (int iZone in _dZones.Keys)
				{
					MQTT.SendMessage(string.Format("homeassistant/switch/actron{0}/airconzone{1}/config", _strUnit, iZone), "{{\"name\":\"{0} Zone\",\"unique_id\":\"{2}-z{1}s\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{3}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actron/aircon/{4}/zone{1}\",\"command_topic\":\"actron/aircon/{4}/zone{1}/set\",\"payload_on\":\"ON\",\"payload_off\":\"OFF\",\"state_on\":\"ON\",\"state_off\":\"OFF\",\"availability_topic\":\"{2}/status\"}}", _dZones[iZone].Name, iZone, _strClientId, _strDeviceNameMQTT, _strUnit);
					MQTT.Subscribe("actron/aircon/{0}/zone{1}/set", _strUnit, iZone);

					if (Service.RegisterZoneTemperatures)
						MQTT.SendMessage(string.Format("homeassistant/sensor/actron{0}/airconzone{1}/config", _strUnit, iZone), "{{\"name\":\"{0}\",\"unique_id\":\"{2}-z{1}t\",\"device\":{{\"identifiers\":[\"{2}\"],\"name\":\"{3}\",\"model\":\"Add-On\",\"manufacturer\":\"ActronAir\"}},\"state_topic\":\"actron/aircon/{4}/zone{1}/temperature\",\"unit_of_measurement\":\"\u00B0C\",\"availability_topic\":\"{2}/status\"}}", _dZones[iZone].Name, iZone, _strClientId, _strDeviceNameMQTT, _strUnit);
					else
						MQTT.SendMessage(string.Format("homeassistant/sensor/actron{0}/airconzone{1}/config", _strUnit, iZone), "{{}}"); // Clear existing devices
				}

				MQTT.Subscribe("actron/aircon/{0}/mode/set", _strUnit);
				MQTT.Subscribe("actron/aircon/{0}/fan/set", _strUnit);
				MQTT.Subscribe("actron/aircon/{0}/temperature/set", _strUnit);
			}

			_timerPoll = new Timer(CheckLastUpdate, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
		}

		public static void GetStatus(ref ContentResult result)
		{
			Logging.WriteDebugLog("AirConditioner.GetStatus()");

			foreach (AirConditioner unit in _dUnits.Values)
			{
				result.Content += "Unit: " + unit.Unit + "<br/>";
				if (unit.DataReceived)
				{
					result.Content += "Last Post from Air Conditioner: " + unit.LastUpdate + "<br/>";
					result.Content += "Last Request from Air Conditioner: " + unit.LastRequest + "<br/><br/>";
				}
				else
					result.Content = "Last Update from Air Conditioner: Never" + "<br/><br/>";
			}
		}

		public void CheckLastUpdate(object oState)
		{
			int iWaitTime = 5; // Minutes

			if (_airConditionerData.dtLastUpdate < DateTime.Now.Subtract(TimeSpan.FromMinutes(iWaitTime)))
				Logging.WriteDebugLog("AirConditioner.CheckLastUpdate() No communication with Actron unit ({0}) for at least {1} minutes. Check Actron Connect unit can connect to the network and locate the add-on.", _strUnit, iWaitTime);
		}

		public static void PostData(string strUnit, AirConditionerData data)
		{
			Logging.WriteDebugLog("AirConditioner.PostData() Unit: {0}", strUnit);

			if (_dUnits.ContainsKey(strUnit))
				_dUnits[strUnit].PostData(data);
			else
				_dUnits.First().Value.PostData(data);
		}

		public void PostData(AirConditionerData data)
		{
			string strZones;

			if (!_bDataReceived)
			{
				Logging.WriteDebugLog("AirConditioner.PostData() First Data Received");
				_bDataReceived = true;
			}
			else
				Logging.WriteDebugLog("AirConditioner.PostData()");

			// Update read/write fields if not suppressed
			if (DateTime.Now.Subtract(_dtLastCommand) < TimeSpan.FromSeconds(_iSuppressTimer))
				Logging.WriteDebugLog("AirConditioner.PostData() Suppressing Data Update");
			else
			{
				lock (_oLockData)
				{									
					_airConditionerData.bOn = data.bOn;
					_airConditionerData.bZone1 = data.bZone1;
					_airConditionerData.bZone2 = data.bZone2;
					_airConditionerData.bZone3 = data.bZone3;
					_airConditionerData.bZone4 = data.bZone4;
					_airConditionerData.bZone5 = data.bZone5;
					_airConditionerData.bZone6 = data.bZone6;
					_airConditionerData.bZone7 = data.bZone7;
					_airConditionerData.bZone8 = data.bZone8;
					_airConditionerData.dblSetTemperature = data.dblSetTemperature;					
					_airConditionerData.iFanSpeed = data.iFanSpeed;
					_airConditionerData.iMode = data.iMode;					

					MQTT.SendMessage(string.Format("actron/aircon/{0}/fanmode", _strUnit), Enum.GetName(typeof(FanSpeed), _airConditionerData.iFanSpeed).ToLower());
					MQTT.SendMessage(string.Format("actron/aircon/{0}/mode", _strUnit), (_airConditionerData.bOn ? Enum.GetName(typeof(ModeMQTT), _airConditionerData.iMode).ToLower() : "off"));
					MQTT.SendMessage(string.Format("actron/aircon/{0}/settemperature", _strUnit), _airConditionerData.dblSetTemperature.ToString());

					// Need to move to an array instead of 8 x boolean.
					if (_dZones.Count >= 1) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone1", _strUnit), _airConditionerData.bZone1 ? "ON" : "OFF");
					if (_dZones.Count >= 2) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone2", _strUnit), _airConditionerData.bZone2 ? "ON" : "OFF");
					if (_dZones.Count >= 3) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone3", _strUnit), _airConditionerData.bZone3 ? "ON" : "OFF");
					if (_dZones.Count >= 4) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone4", _strUnit), _airConditionerData.bZone4 ? "ON" : "OFF");
					if (_dZones.Count >= 5) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone5", _strUnit), _airConditionerData.bZone5 ? "ON" : "OFF");
					if (_dZones.Count >= 6) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone6", _strUnit), _airConditionerData.bZone6 ? "ON" : "OFF");
					if (_dZones.Count >= 7) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone7", _strUnit), _airConditionerData.bZone7 ? "ON" : "OFF");
					if (_dZones.Count >= 8) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone8", _strUnit), _airConditionerData.bZone8 ? "ON" : "OFF");
				}
			}

			// Update read only fields on each post
			lock (_oLockData)
			{
				_airConditionerData.dtLastUpdate = DateTime.Now;

				_airConditionerData.bESPOn = data.bESPOn;
				_airConditionerData.iCompressorActivity = data.iCompressorActivity;
				_airConditionerData.iFanContinuous = data.iFanContinuous;
				_airConditionerData.dblRoomTemperature = data.dblRoomTemperature;
				_airConditionerData.dblZone1Temperature = data.dblZone1Temperature;
				_airConditionerData.dblZone2Temperature = data.dblZone2Temperature;
				_airConditionerData.dblZone3Temperature = data.dblZone3Temperature;
				_airConditionerData.dblZone4Temperature = data.dblZone4Temperature;
				_airConditionerData.dblZone5Temperature = data.dblZone5Temperature;
				_airConditionerData.dblZone6Temperature = data.dblZone6Temperature;
				_airConditionerData.dblZone7Temperature = data.dblZone7Temperature;
				_airConditionerData.dblZone8Temperature = data.dblZone8Temperature;
				_airConditionerData.strErrorCode = data.strErrorCode;

				MQTT.SendMessage(string.Format("actron/aircon/{0}/temperature", _strUnit), _airConditionerData.dblRoomTemperature.ToString());
				MQTT.SendMessage(string.Format("actron/aircon/{0}/esp", _strUnit), _airConditionerData.bESPOn ? "ON" : "OFF");
				MQTT.SendMessage(string.Format("actron/aircon/{0}/fancont", _strUnit), _airConditionerData.iFanContinuous == 0 ? "OFF" : "ON");

				if (Service.RegisterZoneTemperatures)
				{
					if (_dZones.Count >= 1) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone1/temperature", _strUnit), _airConditionerData.dblZone1Temperature.ToString());
					if (_dZones.Count >= 2) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone2/temperature", _strUnit), _airConditionerData.dblZone2Temperature.ToString());
					if (_dZones.Count >= 3) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone3/temperature", _strUnit), _airConditionerData.dblZone3Temperature.ToString());
					if (_dZones.Count >= 4) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone4/temperature", _strUnit), _airConditionerData.dblZone4Temperature.ToString());
					if (_dZones.Count >= 5) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone5/temperature", _strUnit), _airConditionerData.dblZone5Temperature.ToString());
					if (_dZones.Count >= 6) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone6/temperature", _strUnit), _airConditionerData.dblZone6Temperature.ToString());
					if (_dZones.Count >= 7) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone7/temperature", _strUnit), _airConditionerData.dblZone7Temperature.ToString());
					if (_dZones.Count >= 8) MQTT.SendMessage(string.Format("actron/aircon/{0}/zone8/temperature", _strUnit), _airConditionerData.dblZone8Temperature.ToString());
				}

				switch (_airConditionerData.iCompressorActivity)
				{
					case 0:
						MQTT.SendMessage(string.Format("actron/aircon/{0}/compressor", _strUnit), "heating");
						break;

					case 1:
						MQTT.SendMessage(string.Format("actron/aircon/{0}/compressor", _strUnit), "cooling");
						break;

					case 2:
						if (_airConditionerData.bOn)
							MQTT.SendMessage(string.Format("actron/aircon/{0}/compressor", _strUnit), "idle");
						else
							MQTT.SendMessage(string.Format("actron/aircon/{0}/compressor", _strUnit), "off");

						break;

					default:
						MQTT.SendMessage(string.Format("actron/aircon/{0}/compressor", _strUnit), "off");
						break;
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

		public static AirConditionerCommand GetCommand(string strUnit, out string strCommandType)
		{
			Logging.WriteDebugLog("AirConditioner.GetCommand() Unit: {0}", strUnit);

			if (_dUnits.ContainsKey(strUnit))
				return _dUnits[strUnit].GetCommand(out strCommandType);
			else
				return _dUnits.First().Value.GetCommand(out strCommandType);
		}

		public AirConditionerCommand GetCommand(out string strCommandType)
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

		public void PostCommand(long lRequestId, string strUser, AirConditionerCommand command)
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

		public static void ChangeMode(string strUnit, long lRequestId, AirConditionerMode mode)
		{
			Logging.WriteDebugLog("AirConditioner.ChangeMode() Unit: {0}", strUnit);

			if (_dUnits.ContainsKey(strUnit))
				_dUnits[strUnit].ChangeMode(lRequestId, mode);
			else
				_dUnits.First().Value.ChangeMode(lRequestId, mode);
		}

		public void ChangeMode(long lRequestId, AirConditionerMode mode)
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

			MQTT.SendMessage(string.Format("actron/aircon/{0}/mode", _strUnit), (mode != AirConditionerMode.None ? Enum.GetName(typeof(ModeMQTT), mode).ToLower() : "off"));

			PostCommand(lRequestId, "System", command);
		}

		public static void ChangeFanSpeed(string strUnit, long lRequestId, FanSpeed speed)
		{
			Logging.WriteDebugLog("AirConditioner.ChangeFanSpeed() Unit: {0}", strUnit);

			if (_dUnits.ContainsKey(strUnit))
				_dUnits[strUnit].ChangeFanSpeed(lRequestId, speed);
			else
				_dUnits.First().Value.ChangeFanSpeed(lRequestId, speed);
		}

		public void ChangeFanSpeed(long lRequestId, FanSpeed speed)
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

			MQTT.SendMessage(string.Format("actron/aircon/{0}/fanmode", _strUnit), Enum.GetName(typeof(FanSpeed), speed).ToLower());

			PostCommand(lRequestId, "System", command);
		}

		public static void ChangeZone(string strUnit, long lRequestId, int iZone, bool bOn)
		{
			Logging.WriteDebugLog("AirConditioner.ChangeZone() Unit: {0}", strUnit);

			if (_dUnits.ContainsKey(strUnit))
				_dUnits[strUnit].ChangeZone(lRequestId, iZone, bOn);
			else
				_dUnits.First().Value.ChangeZone(lRequestId, iZone, bOn);
		}

		public void ChangeZone(long lRequestId, int iZone, bool bOn)
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

					MQTT.SendMessage(string.Format("actron/aircon/{0}/zone{1}", _strUnit, iZone), bOn ? "ON" : "OFF");
				}
			}

			PostCommand(lRequestId, "System", command);
		}

		public static void ChangeTemperature(string strUnit, long lRequestId, double dblTemperature)
		{
			Logging.WriteDebugLog("AirConditioner.ChangeTemperature() Unit: {0}", strUnit);

			if (_dUnits.ContainsKey(strUnit))
				_dUnits[strUnit].ChangeTemperature(lRequestId, dblTemperature);
			else
				_dUnits.First().Value.ChangeTemperature(lRequestId, dblTemperature);
		}

		public void ChangeTemperature(long lRequestId, double dblTemperature)
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

			MQTT.SendMessage(string.Format("actron/aircon/{0}/settemperature", _strUnit), command.tempTarget.ToString());

			PostCommand(lRequestId, "System", command);
		}
		
		public static void UpdateRequestTime(string strUnit)
		{
			if (_dUnits.ContainsKey(strUnit))
				_dUnits[strUnit].UpdateRequestTime();
			else
				_dUnits.First().Value.UpdateRequestTime();
		}

		public void UpdateRequestTime()
		{
			lock (_oLockData)
			{
				_airConditionerData.dtLastRequest = DateTime.Now;
			}
		}

		public static ManualResetEvent GetEventCommand(string strUnit)
		{
			if (_dUnits.ContainsKey(strUnit))
				return _dUnits[strUnit].EventCommand;
			else
				return _dUnits.First().Value.EventCommand;
		}
	}
}
