# hass-actronque
Actron Que Air Conditioner Add-On for Home Assistant (https://blog.mikejmcguire.com/2020/02/04/actron-que-and-home-assistant/)

This add-on for Home Assistant enables you to control an Actron Air Conditioner equipped with the Actron Que module. 

The add-on requires you to use the Mosquitto MQTT broker on your Home Assistant device, with authentication enabled and a valid credential supplied. You'll also need to ensure that MQTT discovery is enabled with the default prefix 'homeassistant' for HA to discover the climate device and zone switches.

** In Initial Development (i.e. not ready for use) **