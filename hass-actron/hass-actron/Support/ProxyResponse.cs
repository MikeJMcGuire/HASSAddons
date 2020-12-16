using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace HMX.HASSActron
{
	internal class ProxyResponse
	{
		public string Response { get; set; }
		public string ContentType { get; set; }
		public HttpStatusCode ResponseCode { get; set; }
		public bool ProxySuccessful { get; set; }
		public Dictionary<string, string> Headers { get; set; }
	}
}
