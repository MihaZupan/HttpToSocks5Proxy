using MihaZupan;
using System;
using System.Diagnostics;
using System.Threading;

namespace HttpProxyOverSocks5
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: {0} socks5Hostname socks5Port httpPort",
                    Process.GetCurrentProcess().ProcessName);
            }

            string socks5Hostname = "127.0.0.1";
            int socks5Port = 1080;
            int httpPort = 8118;
            try
            {
                socks5Hostname = args[0];
                socks5Port = int.Parse(args[1]);
                httpPort = int.Parse(args[2]);
            }
            catch { }

            Console.WriteLine($"Using socks5 Proxy at {socks5Hostname}:{socks5Port}");
            Console.WriteLine($"Start Http Server at Port {httpPort}");

            var proxy = new HttpToSocks5Proxy(socks5Hostname, socks5Port, httpPort);
            Thread.Sleep(Timeout.Infinite);

        }
    }
}
