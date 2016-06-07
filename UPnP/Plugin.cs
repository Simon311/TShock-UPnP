using System;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using System.Net.Sockets;
using System.Reflection;

namespace UPnP
{
    [ApiVersion(1, 23)]
    public class Plugin : TerrariaPlugin
    {
        public override Version Version
        {
			get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public override string Name
        {
            get { return "UPnP"; }
        }

        public override string Author
        {
			get { return "Simon311"; }
        }

        public override string Description
        {
            get { return "Adds UPnP to TShock."; }
        }

        public Plugin(Main game)
            : base(game)
        {
            Order = -1;
        }

        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, Start, -1);
            Commands.ChatCommands.Add(new Command("upnp", UReInit, "ureload"));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, Start);
                Stop();
            }
        }

        public static bool Success = false;
        public static bool Discovered = false;
		public static int RestPort = 0;

        private void Start(EventArgs args)
        {
            Discovered = SharpUPnP.Discover(SharpUPnP.GetGateways());
            if (Discovered)
            {
				RestPort = TShock.Config.RestApiPort;
                //SharpUPnP.DeleteForwardingRule(Netplay.ListenPort, ProtocolType.Udp);
                SharpUPnP.DeleteForwardingRule(Netplay.ListenPort, ProtocolType.Tcp);
				//SharpUPnP.DeleteForwardingRule(RestPort, ProtocolType.Udp);
				SharpUPnP.DeleteForwardingRule(RestPort, ProtocolType.Tcp);
                //bool Udp = SharpUPnP.ForwardPort(Netplay.ListenPort, ProtocolType.Udp, "TShock @ Port: " + Netplay.ListenPort);
                bool Tcp = SharpUPnP.ForwardPort(Netplay.ListenPort, ProtocolType.Tcp, "TShock @ Port: " + Netplay.ListenPort);
				if (TShock.Config.RestApiEnabled)
					//Udp &= SharpUPnP.ForwardPort(RestPort, ProtocolType.Udp, "TShock REST @ Port: " + RestPort);
					Tcp &= SharpUPnP.ForwardPort(RestPort, ProtocolType.Tcp, "TShock REST @ Port: " + RestPort);
                Success =  Tcp;
                if (Success)
                {
                    Console.WriteLine("(UPnP) Port Forward succesful.");
					try
					{
						string IP = SharpUPnP.GetExternalIP().ToString();
						if (!String.IsNullOrEmpty(IP)) Console.WriteLine("(UPnP) Your IP: " + SharpUPnP.GetExternalIP().ToString());
					}
					catch { }
                    TShock.Log.Info("(UPnP) Port Forward succesful.");
                }
                else
                {
                    Console.WriteLine("(UPnP) Port Forward failed. (Port already taken?)");
                    TShock.Log.Error("(UPnP) Port Forward failed. (Port already taken?)");
                }
            }
            else
            {
                Console.WriteLine("(UPnP) Failed to discover UPnP service.");
                TShock.Log.Error("(UPnP) Failed to discover UPnP service.");
            }
        }

        private void Stop()
        {
            if (Discovered)
            {
                Console.WriteLine("(UPnP) Disposing port forward.");
                //SharpUPnP.DeleteForwardingRule(Netplay.ListenPort, ProtocolType.Udp);
                SharpUPnP.DeleteForwardingRule(Netplay.ListenPort, ProtocolType.Tcp);
				//SharpUPnP.DeleteForwardingRule(RestPort, ProtocolType.Udp);
				SharpUPnP.DeleteForwardingRule(RestPort, ProtocolType.Tcp);
            }
            else
            {
                Console.WriteLine("(UPnP) Service was not discovered, nothing to dispose.");
            }
        }

        private void UReInit(CommandArgs args)
        {
            if (Discovered)
            {
				RestPort = TShock.Config.RestApiPort;
                bool dTcp = SharpUPnP.DeleteForwardingRule(Netplay.ListenPort, ProtocolType.Tcp);
                //bool dUdp = SharpUPnP.DeleteForwardingRule(Netplay.ListenPort, ProtocolType.Udp);
				if (TShock.Config.RestApiEnabled)
				{
					dTcp &= SharpUPnP.DeleteForwardingRule(RestPort, ProtocolType.Tcp);
					//dUdp &= SharpUPnP.DeleteForwardingRule(RestPort, ProtocolType.Udp);
				}
                if (dTcp)// & dUdp)
                {
                    //bool iUdp = SharpUPnP.ForwardPort(Netplay.ListenPort, ProtocolType.Udp, "TShock @ Port: " + Netplay.ListenPort);
                    bool iTcp = SharpUPnP.ForwardPort(Netplay.ListenPort, ProtocolType.Tcp, "TShock @ Port: " + Netplay.ListenPort);
                    if (iTcp)//(iUdp & iTcp)
                    {
                        Console.WriteLine("(UPnP) Port Forward on request succesful.");
                        TShock.Log.Info("(UPnP) Port Forward on request succesful.");
                        args.Player.SendMessage("(UPnP) Port Forward succesful.", Color.PaleGoldenrod);
                    }
                    else
                    {
                        Console.WriteLine("(UPnP) Port Forward on request failed. (Port already taken?)");
                        TShock.Log.Error("(UPnP) Port Forward on request failed. (Port already taken?)");
                        args.Player.SendMessage("(UPnP) Port Forward failed. (Port already taken?)", Color.PaleGoldenrod);
                    }
                }
                else
                {
                    Console.WriteLine("(UPnP) Failed to dispose port.");
                    TShock.Log.Error("(UPnP) Failed to dispose port.");
                    args.Player.SendMessage("(UPnP) Failed to dispose port.", Color.PaleGoldenrod);
                }
                return;
            }
        }
    }
}