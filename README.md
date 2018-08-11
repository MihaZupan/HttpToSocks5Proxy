# HttpToSocks5Proxy
C# Http to Socks5 proxy implementation

HttpToSocks5Proxy implements the IWebProxy interface and can therefore be used with all libraries that support HTTP/HTTPS proxies

## Usage with an HttpClient
Example use with the .NET HttpClient

```c#
using MihaZupan;

var proxy = new HttpToSocks5Proxy("127.0.0.1", 1080);
var handler = new HttpClientHandler();
handler.Proxy = proxy;
handler.UseProxy = true;
HttpClient hc = new HttpClient(handler, true);
var httpsGet = hc.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://httpbin.org/ip"));
Console.WriteLine("HTTPS GET: " + httpsGet.Result.Content.ReadAsStringAsync().Result);
```

## Usage with Telegram.Bot library
Or with its original use-case with the [Telegram Bot Library](https://github.com/TelegramBots/Telegram.Bot)

```c#
using MihaZupan;

var proxy = new HttpToSocks5Proxy(Socks5ServerAddress, Socks5ServerPort);

// Or if you need credentials for your proxy server:
var proxy = new HttpToSocks5Proxy(Socks5ServerAddress, Socks5ServerPort, "username", "password");

// Allows you to use proxies that are only allowing connections to Telegram
// Needed for some proxies
proxy.ResolveHostnamesLocally = true;

TelegramBotClient Bot = new TelegramBotClient("API Token", proxy);
```

## Installation

Install as [NuGet package](https://www.nuget.org/packages/HttpToSocks5Proxy/):

Package manager:

```powershell
Install-Package HttpToSocks5Proxy
```
