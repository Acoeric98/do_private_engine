using Ow.Chat;
using Ow.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ow.Net
{
    class ChatServer
    {
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        public static int Port = 9338;

        private static IPAddress GetListenAddress()
        {
            var configuredAddress = Environment.GetEnvironmentVariable("CHAT_LISTEN_ADDRESS");

            if (!string.IsNullOrWhiteSpace(configuredAddress))
            {
                if (IPAddress.TryParse(configuredAddress, out var parsedAddress))
                {
                    return parsedAddress;
                }

                try
                {
                    var hostAddress = Dns.GetHostAddresses(configuredAddress).FirstOrDefault();

                    if (hostAddress != null)
                    {
                        return hostAddress;
                    }
                }
                catch (Exception e)
                {
                    Out.WriteLine($"Failed to resolve CHAT_LISTEN_ADDRESS '{configuredAddress}': {e.Message}", "ChatServer");
                }
            }

            return IPAddress.Any;
        }

        public static void StartListening()
        {
            IPAddress listenAddress = GetListenAddress();
            IPEndPoint localEndPoint = new IPEndPoint(listenAddress, Port);

            Socket listener = new Socket(localEndPoint.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp);

            if (listener.AddressFamily == AddressFamily.InterNetworkV6)
            {
                listener.DualMode = true;
            }

            try
            {
                Out.WriteLine($"Binding chat listener to {localEndPoint}", "ChatServer");
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    allDone.Reset();

                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        listener);

                    allDone.WaitOne();
                }

            }
            catch (Exception e)
            {
                Logger.Log("error_log", $"- [ChatServer.cs] StartListening void exception: {e}");
            }
        }

        public static void AcceptCallback(IAsyncResult ar)
        {
            try
            {
                allDone.Set();

                Socket listener = (Socket)ar.AsyncState;
                Socket handler = listener.EndAccept(ar);

                new ChatClient(handler);
            } 
            catch (Exception e)
            {
                Logger.Log("error_log", $"- [ChatServer.cs] AcceptCallback void exception: {e}");
            }
        }
    }
}
