using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Net;
using System.Xml;
using System.IO;
using TerrariaApi.Server;
using TShockAPI;

namespace UPnP
{

    /* Thank a stranger here, the class is not mine
       https://code.google.com/p/sharpupnp/ */

    public static class SharpUPnP
    {
        static TimeSpan _timeout = new TimeSpan(0, 0, 0, 3);
        public static TimeSpan TimeOut
        {
            get { return _timeout; }
            set { _timeout = value; }
        }

        public static string _descUrl, _serviceUrl, _eventUrl;

        public static List<IPAddress> GetGateways()
        {
            List<IPAddress> Gateways = new List<IPAddress>();

            foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.GetIPProperties().GatewayAddresses.Count > 0 && n.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (GatewayIPAddressInformation Addr in n.GetIPProperties().GatewayAddresses)
                    {
                        IPAddress IP = Addr.Address;
                        if (IP != null & IP.AddressFamily == AddressFamily.InterNetwork & IP.ToString() != "0.0.0.0")
                        {
                            Gateways.Add(IP);
                        }
                    }
                }
            }

            if (ServerApi.RunningMono & Environment.OSVersion.Platform == PlatformID.Unix)
            {
                Process proc = new Process();
                proc.EnableRaisingEvents = false;
                proc.StartInfo.FileName = "/bin/sh";
                proc.StartInfo.Arguments = "-c \"echo $(/sbin/ip route | awk '/default/ { print $3 }')\"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.Start();
                string Output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                try
                {
                    Output = Output.Trim(new Char[] { ' ', '\r', '\n' });
                    TShock.Log.Info("(UPnP-Linux) Attempting to parse IP: " + Output);
                    Console.WriteLine("(UPnP-Linux) Attempting to parse IP: " + Output);
                    IPAddress Parsed = IPAddress.Parse(Output);
                    if (Parsed != null & Parsed.AddressFamily == AddressFamily.InterNetwork & Parsed.ToString() != "0.0.0.0")
                    {
                        Gateways.Add(Parsed);
                    }
                    else
                    {
                        TShock.Log.Error("(UPnP-Linux) Invalid Gateway IP.");
                        Console.WriteLine("(UPnP-Linux) Invalid Gateway IP.");
                    }
                }
                catch
                {
                    TShock.Log.Error("(UPnP-Linux) Unable to parse IP.");
                    Console.WriteLine("(UPnP-Linux) Unable to parse IP.");
                }
            }

			Gateways.Add(IPAddress.Parse("239.255.255.250")); // It does work, idk how or why

            return Gateways;
        }

        public static bool Discover(List<IPAddress> Gateways)
        {
            
            if (Gateways.Count < 1)  
            {
                TShock.Log.Error("(UPnP) No Gateway IP address!");
                Console.WriteLine("(UPnP) No Gateway IP address!");
                return false;
            };

            foreach (IPAddress gwInfo in Gateways)
            {
                bool flag = false;
                TShock.Log.Info("(UPnP) Attemting Gateway IP: " + gwInfo.ToString());
                Console.WriteLine("(UPnP) Attemting Gateway IP: " + gwInfo.ToString());

                Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
                string req = "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: " + gwInfo.ToString() + ":1900\r\n" +
                "ST:upnp:rootdevice\r\n" +
                "MAN:\"ssdp:discover\"\r\n" +
                "MX:3\r\n\r\n";
                Socket client = new Socket(AddressFamily.InterNetwork,
                    SocketType.Dgram, ProtocolType.Udp);
                IPEndPoint endPoint = new
                IPEndPoint(IPAddress.Parse(gwInfo.ToString()), 1900);

                client.SetSocketOption(SocketOptionLevel.Socket,
                    SocketOptionName.ReceiveTimeout, 5000);

                byte[] q = Encoding.ASCII.GetBytes(req);
                client.SendTo(q, q.Length, SocketFlags.None, endPoint);
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                EndPoint senderEP = (EndPoint)sender;

                byte[] data = new byte[1024];
                try { int recv = client.ReceiveFrom(data, ref senderEP); }
                catch
                {
                    Console.WriteLine("(UPnP) Gateway " + gwInfo.ToString() + " timeout");
                    TShock.Log.Info("(UPnP) Gateway " + gwInfo.ToString() + " timeout");
                    flag = false;
                }
                string queryResponse = "";
                queryResponse = Encoding.ASCII.GetString(data);

                DateTime start = DateTime.Now;

                string resp = queryResponse;
                if (resp.Contains("upnp:rootdevice"))
                {
                    resp = resp.Substring(resp.ToLower().IndexOf("location:") + 9);
                    resp = resp.Substring(0, resp.IndexOf("\r")).Trim();
                    if (!string.IsNullOrEmpty(_serviceUrl = GetServiceUrl(resp)))
                    {
                        _descUrl = resp;
                        flag = true;
                    }
                }
                if (flag)
                {
                    return flag;
                }
            }
            return false;
        }

        private static string GetServiceUrl(string resp)
        {
            XmlDocument desc = new XmlDocument();
            try
            {
                desc.Load(WebRequest.Create(resp).GetResponse().GetResponseStream());
            }
            catch (Exception)
            {
                return null;
            }
            XmlNamespaceManager nsMgr = new XmlNamespaceManager(desc.NameTable);
            nsMgr.AddNamespace("tns", "urn:schemas-upnp-org:device-1-0");
            XmlNode typen = desc.SelectSingleNode("//tns:device/tns:deviceType/text()", nsMgr);
            if (!typen.Value.Contains("InternetGatewayDevice"))
                return null;
            XmlNode node = desc.SelectSingleNode("//tns:service[tns:serviceType=\"urn:schemas-upnp-org:service:WANIPConnection:1\"]/tns:controlURL/text()", nsMgr);
            if (node == null)
                return null;
            XmlNode eventnode = desc.SelectSingleNode("//tns:service[tns:serviceType=\"urn:schemas-upnp-org:service:WANIPConnection:1\"]/tns:eventSubURL/text()", nsMgr);
            _eventUrl = CombineUrls(resp, eventnode.Value);
            return CombineUrls(resp, node.Value);
        }

        private static string CombineUrls(string resp, string p)
        {
            int n = resp.IndexOf("://");
            n = resp.IndexOf('/', n + 3);
            return resp.Substring(0, n) + p;
        }

        public static bool ForwardPort(int port, ProtocolType protocol, string description)
        {
            if (string.IsNullOrEmpty(_serviceUrl))  return false;

            IPHostEntry ipEntry = Dns.GetHostEntry(Dns.GetHostName()); // Dns.GetHostByName(Dns.GetHostName());
            IPAddress addr = ipEntry.AddressList[0];

            XmlDocument xdoc = SOAPRequest(_serviceUrl,
                "<m:AddPortMapping xmlns:m=\"urn:schemas-upnp-org:service:WANIPConnection:1\"><NewRemoteHost xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"string\"></NewRemoteHost><NewExternalPort xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"ui2\">" +
                port.ToString() + "</NewExternalPort><NewProtocol xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"string\">" +
                protocol.ToString().ToUpper() + "</NewProtocol><NewInternalPort xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"ui2\">" +
                port.ToString() + "</NewInternalPort><NewInternalClient xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"string\">" +
                addr + "</NewInternalClient><NewEnabled xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"boolean\">1</NewEnabled><NewPortMappingDescription xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"string\">" +
                description + "</NewPortMappingDescription><NewLeaseDuration xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"ui4\">0</NewLeaseDuration></m:AddPortMapping>",
                "AddPortMapping");
            return (xdoc != null);
        }

        public static bool DeleteForwardingRule(int port, ProtocolType protocol)
        {
            if (string.IsNullOrEmpty(_serviceUrl))  return false;

            XmlDocument xdoc = SOAPRequest(_serviceUrl,
            "<u:DeletePortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" +
            "<NewRemoteHost></NewRemoteHost>" +
            "<NewExternalPort>" + port.ToString() + "</NewExternalPort>" +
            "<NewProtocol>" + protocol.ToString().ToUpper() + "</NewProtocol>" +
            "</u:DeletePortMapping>", "DeletePortMapping");
            return (xdoc != null);
        }

        public static IPAddress GetExternalIP()
        {
			if (string.IsNullOrEmpty(_serviceUrl)) return null;
            XmlDocument xdoc = SOAPRequest(_serviceUrl, "<u:GetExternalIPAddress xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" +
            "</u:GetExternalIPAddress>", "GetExternalIPAddress");
            if (xdoc == null)
            {
                //Console.WriteLine("(UPnP) Are you a developer?");
                return null;
            }
            XmlNamespaceManager nsMgr = new XmlNamespaceManager(xdoc.NameTable);
            nsMgr.AddNamespace("tns", "urn:schemas-upnp-org:device-1-0");
            string IP = xdoc.SelectSingleNode("//NewExternalIPAddress/text()", nsMgr).Value;
            return IPAddress.Parse(IP);
        }

        public static XmlDocument SOAPRequest(string url, string soap, string function)
        {
            string req = "<?xml version=\"1.0\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            "<s:Body>" +
            soap +
            "</s:Body>" +
            "</s:Envelope>";
            try
            {
                WebRequest r = HttpWebRequest.Create(url);
                r.Timeout = 10000;
                r.Method = "POST";
                byte[] b = Encoding.UTF8.GetBytes(req);
                r.Headers.Add("SOAPACTION", "\"urn:schemas-upnp-org:service:WANIPConnection:1#" + function + "\"");
                r.ContentType = "text/xml; charset=\"utf-8\"";
                r.ContentLength = b.Length;
                r.GetRequestStream().Write(b, 0, b.Length);
                XmlDocument resp = new XmlDocument();
                WebResponse wres = r.GetResponse();
                Stream ress = wres.GetResponseStream();
                resp.Load(ress);
                return resp;
            }
            catch
            {
                return null;
            }
        }
    }
}