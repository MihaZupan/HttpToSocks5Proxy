# HttpToSocks5Proxy
C# Http to Socks5 proxy implementation

HttpToSocks5Proxy implements the IWebProxy interface and can therefore be used with all libraries that support HTTP/HTTPS proxies

Example use with the .NET HttpClient

```c#
var proxy = new HttpToSocks5Proxy("127.0.0.1", 1080);
var handler = new HttpClientHandler();
handler.Proxy = proxy;
handler.UseProxy = true;
HttpClient hc = new HttpClient(handler, true);
var httpsPost = hc.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://httpbin.org/post") { Content = new StringContent("Hello") });
var httpsGet = hc.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://httpbin.org/ip"));
var httpPost = hc.SendAsync(new HttpRequestMessage(HttpMethod.Post, "http://httpbin.org/post") { Content = new StringContent("Hello") });
var httpGet = hc.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://httpbin.org/ip"));
Console.WriteLine("HTTPS POST: " + httpsPost.Result.Content.ReadAsStringAsync().Result);
Console.WriteLine("HTTPS GET: " + httpsGet.Result.Content.ReadAsStringAsync().Result);
Console.WriteLine("HTTP POST: " + httpPost.Result.Content.ReadAsStringAsync().Result);
Console.WriteLine("HTTP GET: " + httpGet.Result.Content.ReadAsStringAsync().Result);
```
</br>
</br>

Or with it's original use-case with the Telegram Bot Library (https://github.com/TelegramBots/Telegram.Bot)
```c#
var proxy = new HttpToSocks5Proxy(Socks5ServerAddress, Socks5ServerPort);
proxy.ResolveHostnamesLocally = true; // Allows you to use proxies that are only allowing connections to Telegram
TelegramBotClient Bot = new TelegramBotClient(API_KEY, proxy);
```
