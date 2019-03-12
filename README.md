# hass-actron
Actron Air Conditioner Add-On for Home Assistant

This add-on for Home Assistant enables you to control an Actron Air Conditioner equipped with the Actron Connect wireless module. 

For this initial release, the add-on requires you to use the Mosquitto MQTT broker on your Home Assistant device, with authentication enabled and a valid credential supplied. You'll also need to ensure that MQTT discovery is enabled with the default prefix 'homeassistant' for HA to discover the climate device and zone switches.

Using this add-on will prevent the Actron Connect application from working on your mobile device, as the communications from the Air Conditioner to the cloud service need to be intercepted and routed to this add-on.

You will need to ensure (through a local DNS configuration on your router or home DNS service), that the following two host names resolve to the IP address of your Home Assistant server.
- actron.ninja.is
- actron-connect.actronair.com.au

At this stage, you will also need to ensure that you've used the Actron Connect application to configure your Air Conditioner before making these changes.

The add-on will need to maintain the TCP port 80 binding, as the air conditioner will only attempt to connect to the system on port 80.
