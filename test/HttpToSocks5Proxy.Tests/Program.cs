using System;
using System.Net.Http;
using MihaZupan;

namespace Socks5Proxy.Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            var proxy = new HttpToSocks5Proxy(new[] {
                new ProxyInfo("proxy-server.com", 1080),
            });
            var handler = new HttpClientHandler { Proxy = proxy };
            HttpClient httpClient = new HttpClient(handler, true);
            var httpsGet = httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://httpbin.org/ip"));
            var result = httpsGet.Result;
            result.EnsureSuccessStatusCode();

            Console.WriteLine("HTTPS GET: " + result.Content.ReadAsStringAsync().Result);

            Console.ReadLine();
        }
    }
}
