using System;
using System.Net.Http;
using System.Threading.Tasks;
using MihaZupan;

namespace Socks5Proxy.Tests
{
    class Program
    {
        static async Task Main()
        {
            var proxy = new HttpToSocks5Proxy(new[] {
                new ProxyInfo("proxy-server.com", 1080),
            });
            var handler = new HttpClientHandler { Proxy = proxy };
            HttpClient httpClient = new HttpClient(handler, true);

            var result = await httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://httpbin.org/ip"));

            result.EnsureSuccessStatusCode();

            Console.WriteLine("HTTPS GET: " + await result.Content.ReadAsStringAsync());

            Console.ReadLine();
        }
    }
}
