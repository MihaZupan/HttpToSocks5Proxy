[![Stand With Ukraine](https://raw.githubusercontent.com/vshymanskyy/StandWithUkraine/main/banner2-direct.svg)](https://stand-with-ukraine.pp.ua)

# HttpToSocks5Proxy

As of .NET 6, `SocketsHttpHandler` [supports connecting to Socks4, Socks4a and Socks5 proxies](https://devblogs.microsoft.com/dotnet/dotnet-6-networking-improvements/#socks-proxy-support)!

This project is now archived and no longer maintained. You can use this library on older versions of .NET. See the [archived branch](https://github.com/MihaZupan/HttpToSocks5Proxy/tree/archived).

```c#
var client = new HttpClient(new SocketsHttpHandler()
{
    Proxy = new WebProxy("socks5://127.0.0.1:9050")
});

var content = await client.GetStringAsync("https://check.torproject.org/");
Console.WriteLine(content);
```