using System;
using System.Collections.Generic;
using System.Text;

namespace HMX.HASSBlueriiot
{
	public class Configuration
	{
		// Boolean
		public static bool GetConfiguration(string strVariable, out bool bConfiguration)
		{
			return GetConfiguration(strVariable, out bConfiguration, false);
		}

		public static bool GetPrivateConfiguration(string strVariable, out bool bConfiguration)
		{
			return GetConfiguration(strVariable, out bConfiguration, true);
		}

		private static bool GetConfiguration(string strVariable, out bool bConfiguration, bool bPrivate)
		{
			string strTemp;

			Logging.WriteLog("Configuration.GetConfiguration() Read {0}", strVariable);

			if ((Environment.GetEnvironmentVariable(strVariable) ?? "") != "")
			{
				strTemp = Environment.GetEnvironmentVariable(strVariable) ?? "";

				if (!bool.TryParse(strTemp, out bConfiguration))
				{
					bConfiguration = false;

					Logging.WriteLog("Configuration.GetConfiguration()", "Missing configuration: {0}.", strVariable);

					return false;
				}

				if (bPrivate)
					Logging.WriteLog("{0}: *******", strVariable);
				else
					Logging.WriteLog("{0}: {1}", strVariable, bConfiguration);

				return true;
			}
			else
			{
				bConfiguration = false;

				Logging.WriteLog("Configuration.GetConfiguration()", "Missing configuration: {0}.", strVariable);

				return false;
			}
		}

		// String
		public static bool GetConfiguration(string strVariable, out string strConfiguration)
		{
			return GetConfiguration(strVariable, out strConfiguration, false, false);
		}

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

		// Integer
		public static bool GetConfiguration(string strVariable, out int iConfiguration)
		{
			return GetConfiguration(strVariable, out iConfiguration, false);
		}

		public static bool GetPrivateConfiguration(string strVariable, out int iConfiguration)
		{
			return GetConfiguration(strVariable, out iConfiguration, true);
		}		

		private static bool GetConfiguration(string strVariable, out int iConfiguration, bool bPrivate)
		{
			string strTemp;

			Logging.WriteLog("Configuration.GetConfiguration() Read {0}", strVariable);

			if ((Environment.GetEnvironmentVariable(strVariable) ?? "") != "")
			{
				strTemp = Environment.GetEnvironmentVariable(strVariable) ?? "";

				if (!int.TryParse(strTemp, out iConfiguration))
				{
					iConfiguration = 0;

					Logging.WriteLog("Configuration.GetConfiguration()", "Missing configuration: {0}.", strVariable);

					return false;
				}

				if (bPrivate)
					Logging.WriteLog("{0}: *******", strVariable);
				else
					Logging.WriteLog("{0}: {1}", strVariable, iConfiguration);

				return true;
			}
			else
			{
				iConfiguration = 0;

				Logging.WriteLog("Configuration.GetConfiguration()", "Missing configuration: {0}.", strVariable);

				return false;
			}
		}
	}
}
