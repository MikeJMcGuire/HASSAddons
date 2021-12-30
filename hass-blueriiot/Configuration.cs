using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSBlueriiot
{
	public class Configuration
	{
		// String
		public static bool GetOptionalConfiguration(string strVariable, out string strConfiguration)
		{
			return GetConfiguration(strVariable, out strConfiguration, false, true);
		}

		public static bool GetPrivateConfiguration(string strVariable, out string strConfiguration)
		{
			return GetConfiguration(strVariable, out strConfiguration, true, false);
		}

		private static bool GetConfiguration(string strVariable, out string strConfiguration, bool bPrivate, bool bOptional)
		{
			Logging.WriteLog("Configuration.GetConfiguration() Read {0}", strVariable);

			strConfiguration = "";

			if ((Environment.GetEnvironmentVariable(strVariable) ?? "") != "")
			{
				strConfiguration = Environment.GetEnvironmentVariable(strVariable) ?? "";

				if (bPrivate)
					Logging.WriteLog("{0}: *******", strVariable);
				else
					Logging.WriteLog("{0}: {1}", strVariable, strConfiguration);

				return true;
			}
			else if (bOptional)
			{
				return true;
			}
			else
			{
				Logging.WriteLog("Configuration.GetConfiguration()", "Missing configuration: {0}.", strVariable);

				return false;
			}
		}
		
		// String
		public static bool GetConfiguration(IConfigurationRoot configuration, string strVariable, out string strConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out strConfiguration, false, false);
		}

		public static bool GetPrivateConfiguration(IConfigurationRoot configuration, string strVariable, out string strConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out strConfiguration, true, false);
		}

		private static bool GetConfiguration(IConfigurationRoot configuration, string strVariable, out string strConfiguration, bool bPrivate, bool bOptional)
		{
			Logging.WriteLog("Configuration.GetConfiguration() Read {0}", strVariable);

			strConfiguration = "";

			if ((configuration[strVariable] ?? "") != "")
			{
				strConfiguration = configuration[strVariable];

				if (bPrivate)
					Logging.WriteLog("{0}: *******", strVariable);
				else
					Logging.WriteLog("{0}: {1}", strVariable, strConfiguration);

				return true;
			}
			else if (bOptional)
			{
				return true;
			}
			else
			{
				Logging.WriteLog("Configuration.GetConfiguration()", "Missing configuration: {0}.", strVariable);

				return false;
			}
		}		
	}
}