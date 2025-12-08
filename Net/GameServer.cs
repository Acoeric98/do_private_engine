using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ow.Utils;

namespace Ow.Net
{
    class GameServer
    {
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        public static int Port = 8080;

        public static void StartListening()
        {
            var bindAddress = ServerSettings.ResolveBindAddress();
            var localEndPoint = new IPEndPoint(bindAddress, Port);

            Socket listener = ServerSettings.CreateTcpSocket(bindAddress);

            try
            {
                Out.WriteLine($"Binding GameServer to {localEndPoint}", "GameServer");
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
                Out.WriteLine($"Failed to start GameServer listener on {localEndPoint}: {e.Message}", "GameServer");
                Logger.Log("error_log", $"- [GameServer.cs] StartListening void exception: {e}");
            }
        }

        public static void AcceptCallback(IAsyncResult ar)
        {
            try
            {
                allDone.Set();

                Socket listener = (Socket)ar.AsyncState;
                Socket handler = listener.EndAccept(ar);

                Out.WriteLine($"Accepted client from {handler.RemoteEndPoint}", "GameServer");
                new GameClient(handler);
            }
            catch (Exception e)
            {
                Logger.Log("error_log", $"- [GameServer.cs] AcceptCallback void exception: {e}");
            }
        }
    }
}