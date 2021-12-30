using System.Globalization;

namespace HMX.HASSBlueriiot
{
	public class Program
	{
		public static void Main(string[] strArguments)
		{
			//CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-AU");
			//CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-AU");

			ServiceCore.Start();
		}
	}
}
