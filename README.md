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

New Features (v0.9)
- Command changes are reflected back to HA immediately to prevent the appearance of changes not taking effect.
- Compressor state reflected in HA climate entity.

New Features (v0.7)
- Introduced a short delay to suppress incoming data from the actron connect after sending it new commands, as the actron connect has some delay between accepting a command, and reflecting that in updates that it sends back to HA.

New Features (v0.6)
- Supports newer firmware versions of the Actron Connect that use HTTP long polling (still in testing).

New Features (v0.5)
- For air conditioners with per-zone temperatures, set 'RegisterZoneTemperatures' to true in the options.json (add-on options). This will create per-zone sensor elements in Home Assistant.

