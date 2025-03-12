using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSActronQue
{
	public class Configuration
	{
		// Boolean
		public static bool GetConfiguration(IConfigurationRoot configuration, string strVariable, out bool bConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out bConfiguration, false, false);
		}

		public static bool GetOptionalConfiguration(IConfigurationRoot configuration, string strVariable, out bool bConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out bConfiguration, false, true);
		}

		public static bool GetOptionalConfiguration(IConfigurationRoot configuration, string strVariable, out bool bConfiguration, bool bDefault)
		{
			return GetConfiguration(configuration, strVariable, out bConfiguration, false, true, bDefault);
		}

		public static bool GetPrivateConfiguration(IConfigurationRoot configuration, string strVariable, out bool bConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out bConfiguration, true, false);
		}

		private static bool GetConfiguration(IConfigurationRoot configuration, string strVariable, out bool bConfiguration, bool bPrivate, bool bOptional, bool bDefault = false)
		{
			string strTemp;

			Logging.WriteDebugLog("Configuration.GetConfiguration() Read {0}", strVariable);

			bConfiguration = bDefault;

			if ((configuration[strVariable] ?? "") != "")
			{
				strTemp = configuration[strVariable];

				if (!bool.TryParse(strTemp, out bConfiguration))
				{
					bConfiguration = false;

					Logging.WriteDebugLog("Service.Start()", "Missing configuration: {0}.", strVariable);

					return false;
				}

				if (bPrivate)
					Logging.WriteDebugLog("{0}: *******", strVariable);
				else
					Logging.WriteDebugLog("{0}: {1}", strVariable, bConfiguration);

				return true;
			}
			else if (bOptional)
			{
				return true;
			}
			else
			{
				Logging.WriteDebugLog("Service.Start()", "Missing configuration: {0}.", strVariable);

				return false;
			}
		}

		// String
		public static bool GetConfiguration(IConfigurationRoot configuration, string strVariable, out string strConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out strConfiguration, false, false);
		}

		public static bool GetOptionalConfiguration(IConfigurationRoot configuration, string strVariable, out string strConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out strConfiguration, false, true);
		}

		public static bool GetPrivateConfiguration(IConfigurationRoot configuration, string strVariable, out string strConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out strConfiguration, true, false);
		}

		public static bool GetPrivateOptionalConfiguration(IConfigurationRoot configuration, string strVariable, out string strConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out strConfiguration, true, true);
		}

		private static bool GetConfiguration(IConfigurationRoot configuration, string strVariable, out string strConfiguration, bool bPrivate, bool bOptional)
		{
			Logging.WriteDebugLog("Configuration.GetConfiguration() Read {0}", strVariable);

			strConfiguration = "";

			if ((configuration[strVariable] ?? "") != "")
			{
				strConfiguration = configuration[strVariable];

				if (bPrivate)
					Logging.WriteDebugLog("{0}: *******", strVariable);
				else
					Logging.WriteDebugLog("{0}: {1}", strVariable, strConfiguration);

				return true;
			}
			else if (bOptional)
			{
				return true;
			}
			else
			{
				Logging.WriteDebugLog("Service.Start()", "Missing configuration: {0}.", strVariable);

				return false;
			}
		}

		// Integer
		public static bool GetConfiguration(IConfigurationRoot configuration, string strVariable, out int iConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out iConfiguration, false);
		}

		public static bool GetPrivateConfiguration(IConfigurationRoot configuration, string strVariable, out int iConfiguration)
		{
			return GetConfiguration(configuration, strVariable, out iConfiguration, true);
		}		

		private static bool GetConfiguration(IConfigurationRoot configuration, string strVariable, out int iConfiguration, bool bPrivate)
		{
			string strTemp;

			Logging.WriteDebugLog("Configuration.GetConfiguration() Read {0}", strVariable);

			if ((configuration[strVariable] ?? "") != "")
			{
				strTemp = configuration[strVariable];

				if (!int.TryParse(strTemp, out iConfiguration))
				{
					iConfiguration = 0;

					Logging.WriteDebugLog("Service.Start()", "Missing configuration: {0}.", strVariable);

					return false;
				}

				if (bPrivate)
					Logging.WriteDebugLog("{0}: *******", strVariable);
				else
					Logging.WriteDebugLog("{0}: {1}", strVariable, iConfiguration);

				return true;
			}
			else
			{
				iConfiguration = 0;

				Logging.WriteDebugLog("Service.Start()", "Missing configuration: {0}.", strVariable);

				return false;
			}
		}
	}
}
