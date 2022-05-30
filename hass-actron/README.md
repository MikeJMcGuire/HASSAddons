# hass-actron
Actron Air Conditioner Add-On for Home Assistant (https://blog.mikejmcguire.com/2018/12/19/actronconnect-and-home-assistant/)

This add-on for Home Assistant enables you to control an Actron Air Conditioner equipped with the Actron Connect wireless module. 

The add-on requires you to use the Mosquitto MQTT broker on your Home Assistant device, with authentication enabled and a valid credential supplied. You'll also need to ensure that MQTT discovery is enabled with the default prefix 'homeassistant' for HA to discover the climate device and zone switches.

Using this add-on will prevent the Actron Connect application from working on your mobile device, as the communications from the Air Conditioner to the cloud service need to be intercepted and routed to this add-on.

You will need to ensure (through a local DNS configuration on your router or home DNS service), that the following host names resolve to the IP address of your Home Assistant server.
- actron-connect.actronair.com.au (usage reporting)
- actron.ninja.is (older firmware versions - data/command traffic)
- que.actronair.com.au (more recent firmware versions - data/command traffic)
- updates.lx-cloud.com (more recent firmware versions - firmware updates)

At this stage, you will also need to ensure that you've used the Actron Connect application to configure your Air Conditioner before making these changes.

The add-on will need to maintain the TCP port 80 binding, as the air conditioner will only attempt to connect to the system on port 80.

If you add the local DNS entry for updates.lx-cloud.com, it will prevent the ActronConnect module from auto-updating. Feel free to use auto-updating, but there is a risk that an update will prevent the ActronConnect from working with Home Assistant. If this happens though, let me know and we can investigate the changes.

If you have any issues, powering off the AC (generally at the circuit breaker/mains) and back on again will reboot the Actron Connect module. Sometimes it caches old DNS entries.

Home Assistant AddOn Repository: https://github.com/MikeJMcGuire/HASSAddons.

## Configuration
### MQTTBroker: string
Set this field to core-mosquitto to use the HA Mosquitto MQTT add-on. Otherwise, specify a host or host:port for an alternative MQTT server.

### MQTTTLS: true/false
Setting this option to true will force the MQTT client to attempt a TLS connection to the MQTT broker.

### RegisterZoneTemperatures: true/false
If you Actron has temperature sensors per zone, setting this option to true will create a sensor in HA for each zone.

### ForwardToOriginalWebService: true/false
If you set this option to true, any data/status received from the Actron will be forwarded to the original cloud service used by the phone app. This will let the phone see (but not control) the state of the Actron.

### MultipleUnits: string list
If you have multiple Actron Connect units, you can use this option to specify the Unit/Block Id of each of the Actron Connect units. You can determine the Id by browsing to the IP address of each of the units. You will then need to update your zone configuration to specify which zones belong to each unit. Zone numbers start at 1 for each unit. 

Example:
```
Zones:
  - Name: Bedrooms
    Id: 1
    Unit: ACONNECT12345
  - Name: Living Room
    Id: 2
    Unit: ACONNECT12345
  - Name: Lounge Room
    Id: 1
    Unit: ACONNECT67890
MultipleUnits:
  - ACONNECT12345
  - ACONNECT67890
```