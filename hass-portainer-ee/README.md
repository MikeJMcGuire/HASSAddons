# hass-portainer-ee
Portainer BE v2.x Add-On for Home Assistant (https://blog.mikejmcguire.com/2023/11/16/portainer-be-add-on-for-home-assistant-portainer-be-2-x/)

This add-on provides a 2.x version of Portainer-BE/EE, as the standard add-on is based on Portainer 1.x.

The initial credentials for the add-on are admin/portainer - strongly suggest changing the password upon first login.

Port options are available for exposing ports 8000 and 9000/9443 as required.

Portainer requires Home Assistant protection mode to be disabled, as it requires administrative access to the docker platform. As a result, care must be taken when using the portainer tool.

Note: The HA ingress feature is currently incompatible with the new versions of Portainer. You can either stay on the older version, or configure the add-on ports 9000/9443 (HTTP/HTTPS) and connect to the add-on directly.

Home Assistant AddOn Repository: https://github.com/MikeJMcGuire/HASSAddons.
