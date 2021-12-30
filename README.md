# hass-blueriiot
Blueriiot Blue Connect Add-On for Home Assistant

This add-on will periodically poll the Blueriiot API to retrieve sensor data from your Blue Connect unit.

## Configuration
### BlueriiotUser: string
Set this field to your Blueriiot user name.

### BlueriiotPassword: string
Set this field to your Blueriiot password.

### HAKey: string
Set this field to a long lived access token for a Home Assistant user with Administrator permissions (https://www.home-assistant.io/docs/authentication/). This account will be used to register and update the entities for the Blue Connect.
