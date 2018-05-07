using System;
using System.Text;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using MihaZupan.Socks5Proxy.Enums;

namespace MihaZupan
{
    /// <summary>
    /// Acts like an HTTP(s) proxy but interfaces with a SOCKS5 proxy behind-the-scenes
    /// </summary>
    public class HttpToSocks5Proxy : IWebProxy
    {
        private Uri ProxyUri;

        #region IWebProxy
        /// <summary>
        /// Ignored by this <see cref="IWebProxy"/> implementation
        /// </summary>
        public ICredentials Credentials { get; set; } = new NetworkCredential("not", "used");
        /// <summary>
        /// Returned <see cref="Uri"/> is constant for a single <see cref="HttpToSocks5Proxy"/> instance
        /// <para>Address is a local address, the port is chosen by the constructor</para>
        /// </summary>
        /// <param name="destination">Ignored by this <see cref="IWebProxy"/> implementation</param>
        /// <returns></returns>
        public Uri GetProxy(Uri destination) => ProxyUri;
        /// <summary>
        /// Always returns false
        /// </summary>
        /// <param name="host">Ignored by this <see cref="IWebProxy"/> implementation</param>
        /// <returns></returns>
        public bool IsBypassed(Uri host) => false;
        #endregion

        #region Internal HTTP proxy fields
        private Socket InternalServerSocket;
        private int InternalServerPort;
        #endregion

        #region Socks5 server info
        private IPAddress Socks5_Address;
        private int Socks5_Port;
        private bool UseUsernamePasswordAuth => Socks5_Username != null && Socks5_Password != null;
        private string Socks5_Username = null;
        private string Socks5_Password = null;
        #endregion

        #region Constructors
        /// <summary>
        /// Create an Http(s) to Socks5 proxy using no authentication
        /// </summary>
        /// <param name="socks5Hostname">IP address or hostname of the Socks5 proxy server</param>
        /// <param name="socks5Port">Port of the Socks5 proxy server</param>
        public HttpToSocks5Proxy(string socks5Hostname, int socks5Port)
        {
            if (string.IsNullOrEmpty(socks5Hostname)) throw new ArgumentNullException("hostname");
            if (socks5Port < 0 || socks5Port > 65535) throw new ArgumentOutOfRangeException("port");
            Socks5_Address = Resolve(socks5Hostname);
            Socks5_Port = socks5Port;
            InternalServerSocket = CreateSocket();
            InternalServerSocket.Bind(new IPEndPoint(IPAddress.Any, 0));
            InternalServerPort = ((IPEndPoint)(InternalServerSocket.LocalEndPoint)).Port;
            ProxyUri = new Uri("http://127.0.0.1:" + InternalServerPort);
            InternalServerSocket.Listen(8);
            InternalServerSocket.BeginAccept(new AsyncCallback(OnAcceptCallback), null);
        }

        /// <summary>
        /// Create an Http(s) to Socks5 proxy using username and password authentication
        /// <para>Will fallback to no authentication if the username and password authentication failed and the server supports it</para>
        /// </summary>
        /// <param name="socks5Hostname">IP address or hostname of the Socks5 proxy server</param>
        /// <param name="socks5Port">Port of the Socks5 proxy server</param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        public HttpToSocks5Proxy(string socks5Hostname, int socks5Port, string username, string password)
            : this(socks5Hostname, socks5Port)
        {
            if (string.IsNullOrEmpty(username)) throw new ArgumentNullException("username");
            if (string.IsNullOrEmpty(username)) throw new ArgumentNullException("password");
            Socks5_Username = username;
            Socks5_Password = password;
        }
        #endregion

        private static readonly HashSet<string> HopByHopHeaders = new HashSet<string>()
        {
            // ref: https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers
            "CONNECTION", "KEEP-ALIVE", "PROXY-AUTHENTICATE", "PROXY-AUTHORIZATION", "TE", "TRAILER", "TRANSFER-ENCODING", "UPGRADE"
        };

        private void OnAcceptCallback(IAsyncResult AR)
        {
            Socket clientSocket = InternalServerSocket.EndAccept(AR);
            InternalServerSocket.BeginAccept(new AsyncCallback(OnAcceptCallback), null);
            try
            {
                HandleConnection(clientSocket);
            }
            catch
            {
                TryDisposeSocket(clientSocket);
            }
        }
        private void HandleConnection(Socket clientSocket)
        {
            // According to https://stackoverflow.com/a/686243/6845657 even Apache gives up after 8KB
            int offset = 0;
            int received = 0;
            int left = 8192;
            int endOfHeader;
            byte[] headerBuffer = new byte[8192];
            do
            {
                if (left == 0)
                {
                    throw new Exception("Header too long");
                }
                offset = received;
                int read = clientSocket.Receive(headerBuffer, received, left, SocketFlags.None);
                received += read;
                left -= read;
            }
            // received - 3 is used because we could have read the start of the double new line in the previous read
            while (!ContainsDoubleNewLine(headerBuffer, Math.Max(0, received - 3), out endOfHeader));

            // If we reach this point we can be sure that we have read the entire header

            // We could have over-read in case of an HTTP request with a body
            int overRead = received - endOfHeader;

            string headerString = Encoding.ASCII.GetString(headerBuffer, 0, endOfHeader);
            string[] headerLines = headerString.Split('\n').Select(i => i.TrimEnd('\r')).Where(i => i.Length > 0).ToArray();
            string[] methodLine = headerLines[0].Split(' ');
            if (methodLine.Length != 3) // METHOD URI HTTP/X.Y
            {
                throw new Exception("Invalid request method");
            }
            string method = methodLine[0];
            string httpVersion = methodLine[2].Trim() + " ";
            bool connect = method.ToUpper() == "CONNECT";
            string request = null;
            string hostHeader = null;

            if (connect)
            {
                foreach (var headerLine in headerLines)
                {
                    int colon = headerLine.IndexOf(':');
                    if (colon == -1)
                    {
                        throw new Exception("Invalid header");
                    }
                    string headerName = headerLine.Substring(0, colon).Trim();
                    if (headerName.ToUpper() == "HOST")
                    {
                        hostHeader = headerLine.Substring(colon + 1).Trim();
                        break;
                    }
                }
            }
            else
            {
                StringBuilder requestBuilder = new StringBuilder();
                requestBuilder.Append(headerLines[0]);
                for (int i = 1; i < headerLines.Length; i++)
                {
                    int colon = headerLines[i].IndexOf(':');
                    if (colon == -1)
                    {
                        throw new Exception("Invalid header");
                    }
                    string headerName = headerLines[i].Substring(0, colon).Trim();
                    string headerValue = headerLines[i].Substring(colon + 1).Trim();
                    string headerNameUpper = headerName.ToUpper();
                    if (!HopByHopHeaders.Contains(headerNameUpper))
                    {
                        requestBuilder.Append("\r\n");
                        requestBuilder.Append(headerName);
                        requestBuilder.Append(": ");
                        requestBuilder.Append(headerValue);
                    }
                    if (headerNameUpper == "HOST")
                    {
                        hostHeader = headerValue;
                    }
                }
                requestBuilder.Append("\r\n\r\n");
                request = requestBuilder.ToString();
            }

            if (hostHeader == null)
            {
                throw new Exception("Missing host header");
            }
            if (hostHeader == string.Empty)
            {
                throw new Exception("Invalid host header");
            }

            string hostname;
            int port;
            {
                int colon = hostHeader.IndexOf(':');
                if (colon == -1)
                {
                    hostname = hostHeader;
                    port = connect ? 443 : 80;
                }
                else
                {
                    hostname = hostHeader.Substring(0, colon);
                    port = int.Parse(hostHeader.Substring(colon + 1));
                }
            }

            var result = TryEstablishSocks5Connection(hostname, port, UseUsernamePasswordAuth, out Socket socks5Socket);
            if (result != SocketConnectionResult.OK)
            {
                TryDisposeSocket(socks5Socket);

                if (result == SocketConnectionResult.HostUnreachable || result == SocketConnectionResult.ConnectionRefused || result == SocketConnectionResult.ConnectionReset)
                {
                    SendString(clientSocket, httpVersion + "502 Bad Gateway\r\n\r\n");
                }
                else if (result == SocketConnectionResult.AuthenticationError)
                {
                    SendString(clientSocket, httpVersion + "401 Unauthorized\r\n\r\n");
                }
                else
                {
                    SendString(clientSocket, httpVersion + "500 Internal Server Error\r\nX-Proxy-Error-Type: " + result.ToString() + "\r\n\r\n");
                }
                throw new Exception("Failed to establish Socks5 connection: " + result.ToString());
            }

            if (!connect)
            {
                try
                {
                    SendString(socks5Socket, request);
                    if (overRead > 0)
                    {
                        socks5Socket.Send(headerBuffer, endOfHeader, overRead, SocketFlags.None);
                    }
                }
                catch
                {
                    TryDisposeSocket(clientSocket);
                    TryDisposeSocket(socks5Socket);
                    return;
                }
            }
            else
            {
                SendString(clientSocket, httpVersion + "200 Connection established\r\nProxy-Agent: MihaZupan-HttpToSocks5Proxy\r\n\r\n");
            }
            Task.Run(() => { RelayData(socks5Socket, clientSocket); });
            RelayData(clientSocket, socks5Socket);
        }
        private static bool ContainsDoubleNewLine(byte[] buffer, int offset, out int endOfHeader)
        {
            // TRUE for \r\n\r\n or \n\n or \r\n\n or \n\r\n
            // Also for \r\r\r\r\n\n because \r are ignored => we only need 2x \n

            byte R = (byte)'\r';
            byte N = (byte)'\n';

            bool foundOne = false;
            for (endOfHeader = offset; endOfHeader < buffer.Length; endOfHeader++)
            {
                if (buffer[endOfHeader] == N)
                {
                    if (foundOne)
                    {
                        endOfHeader++;
                        return true;
                    }
                    foundOne = true;
                }
                else if (buffer[endOfHeader] == R) continue;
                else foundOne = false;
            }

            return false;
        }

        private void SendString(Socket socket, string text)
        {
            socket.Send(Encoding.UTF8.GetBytes(text));
        }
        private void RelayData(Socket source, Socket target)
        {
            try
            {
                int read;
                byte[] buffer = new byte[8192];
                while ((read = source.Receive(buffer, 0, buffer.Length, SocketFlags.None)) > 0)
                {
                    target.Send(buffer, 0, read, SocketFlags.None);
                }
            }
            catch { }
            finally
            {
                TryDisposeSocket(source);
                TryDisposeSocket(target);
            }
        }

        #region Sockets
        private Socket CreateSocket()
        {
            Socket socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, 0);
            return socket;
        }
        private void TryDisposeSocket(Socket socket)
        {
            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch { }
            try
            {
                socket.Close();
            }
            catch { }
        }
        #endregion

        #region Addresses
        private AddressType GetAddressType(string hostname)
        {
            if (IPAddress.TryParse(hostname, out IPAddress hostIP))
            {
                if (hostIP.AddressFamily == AddressFamily.InterNetwork)
                {
                    return AddressType.IPv4;
                }
                else
                {
                    return AddressType.IPv6;
                }
            }
            return AddressType.DomainName;
        }
        private IPAddress Resolve(string hostname)
        {
            if (IPAddress.TryParse(hostname, out IPAddress hostIP)) return hostIP;
            return Dns.GetHostAddresses(hostname)[0];
        }
        #endregion

        #region Socks5
        public bool ResolveHostnamesLocally = false;
        private SocketConnectionResult TryEstablishSocks5Connection(string destAddress, int destPort, bool doUsernamePasswordAuth, out Socket socks5Socket)
        {
            socks5Socket = CreateSocket();
            try
            {
                // CONNECT
                try
                {
                    socks5Socket.Connect(new IPEndPoint(Socks5_Address, Socks5_Port));
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.ConnectionRefused)
                        return SocketConnectionResult.ConnectionRefused;
                    else if (ex.SocketErrorCode == SocketError.HostUnreachable)
                        return SocketConnectionResult.HostUnreachable;
                    return SocketConnectionResult.ConnectionError;
                }


                // SEND HELLO
                socks5Socket.Send(BuildHelloMessage(doUsernamePasswordAuth));

                // RECEIVE HELLO RESPONSE - HANDLE AUTHENTICATION
                byte[] buffer = new byte[255];
                if (socks5Socket.Receive(buffer) != 2)
                    return SocketConnectionResult.InvalidProxyResponse;
                if (buffer[0] != SocksVersion)
                    return SocketConnectionResult.InvalidProxyResponse;
                if (buffer[1] == (byte)Authentication.UsernamePassword)
                {
                    if (!doUsernamePasswordAuth)
                    {
                        // Proxy server is requesting UserPass auth even tho we did not allow it
                        return SocketConnectionResult.InvalidProxyResponse;
                    }
                    else
                    {
                        // We have to try and authenticate using the Username and Password
                        // https://tools.ietf.org/html/rfc1929
                        socks5Socket.Send(BuildAuthenticationMessage());
                        if (socks5Socket.Receive(buffer) != 2)
                            return SocketConnectionResult.InvalidProxyResponse;
                        if (buffer[0] != SubnegotiationVersion)
                            return SocketConnectionResult.InvalidProxyResponse;
                        if (buffer[1] != 0)
                        {
                            // Try falling back to NoAuth
                            TryDisposeSocket(socks5Socket);
                            return TryEstablishSocks5Connection(destAddress, destPort, false, out socks5Socket);
                        }
                    }
                }
                else if (buffer[1] != (byte)Authentication.NoAuthentication)
                    return SocketConnectionResult.AuthenticationError;

                if (ResolveHostnamesLocally && GetAddressType(destAddress) == AddressType.DomainName)
                {
                    destAddress = Resolve(destAddress).ToString();
                }

                // SEND REQUEST
                socks5Socket.Send(BuildRequestMessage(Command.Connect, GetAddressType(destAddress), destAddress, destPort));

                // RECEIVE RESPONSE
                int received = socks5Socket.Receive(buffer);
                if (received < 8)
                    return SocketConnectionResult.InvalidProxyResponse;
                if (buffer[0] != SocksVersion)
                    return SocketConnectionResult.InvalidProxyResponse;
                if (buffer[1] > 8)
                    return SocketConnectionResult.InvalidProxyResponse;
                if (buffer[1] != 0)
                {
                    return (SocketConnectionResult)buffer[1];
                }
                if (buffer[2] != 0)
                    return SocketConnectionResult.InvalidProxyResponse;
                if (buffer[3] != 1 && buffer[3] != 3 && buffer[3] != 4)
                    return SocketConnectionResult.InvalidProxyResponse;

                AddressType boundAddress = (AddressType)buffer[3];
                if (boundAddress == AddressType.IPv4)
                {
                    if (received != 10)
                        return SocketConnectionResult.InvalidProxyResponse;
                }
                else if (boundAddress == AddressType.IPv6)
                {
                    if (received != 22)
                        return SocketConnectionResult.InvalidProxyResponse;
                }
                else
                {
                    int domainLength = buffer[4];
                    if (received != 7 + domainLength)
                        return SocketConnectionResult.InvalidProxyResponse;
                }

                return SocketConnectionResult.OK;
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.ConnectionReset)
                    return SocketConnectionResult.ConnectionReset;
                return SocketConnectionResult.ConnectionError;
            }
            catch
            {
                return SocketConnectionResult.UnknownError;
            }
        }

        private const byte SubnegotiationVersion = 0x01;
        private const byte SocksVersion = 0x05;

        private static byte[] BuildHelloMessage(bool doUsernamePasswordAuth)
        {
            byte[] hello = new byte[doUsernamePasswordAuth ? 4 : 3];
            hello[0] = SocksVersion;
            hello[1] = (byte)(doUsernamePasswordAuth ? 2 : 1);
            hello[2] = (byte)Authentication.NoAuthentication;
            if (doUsernamePasswordAuth)
            {
                hello[3] = (byte)Authentication.UsernamePassword;
            }
            return hello;
        }
        private static byte[] BuildRequestMessage(Command command, AddressType addressType, string address, int port)
        {
            int addressLength;
            byte[] addressBytes;
            switch (addressType)
            {
                case AddressType.IPv4:
                case AddressType.IPv6:
                    addressBytes = IPAddress.Parse(address).GetAddressBytes();
                    addressLength = addressBytes.Length;
                    break;

                case AddressType.DomainName:
                    byte[] domainBytes = Encoding.UTF8.GetBytes(address);
                    addressLength = 1 + domainBytes.Length;
                    addressBytes = new byte[addressLength];
                    addressBytes[0] = (byte)address.Length;
                    Array.Copy(domainBytes, 0, addressBytes, 1, domainBytes.Length);
                    break;

                default:
                    throw new ArgumentException("Unknown address type");
            }

            byte[] request = new byte[6 + addressLength];
            request[0] = SocksVersion;
            request[1] = (byte)command;
            request[2] = 0x00;
            request[3] = (byte)addressType;
            for (int i = 0; i < addressLength; i++)
            {
                request[4 + i] = addressBytes[i];
            }
            request[request.Length - 2] = (byte)(port / 256);
            request[request.Length - 1] = (byte)(port % 256);
            return request;
        }
        private byte[] BuildAuthenticationMessage()
        {
            byte[] usernameBytes = Encoding.ASCII.GetBytes(Socks5_Username);
            byte[] passwordBytes = Encoding.ASCII.GetBytes(Socks5_Password);

            byte[] authMessage = new byte[3 + usernameBytes.Length + passwordBytes.Length];
            authMessage[0] = SubnegotiationVersion;
            authMessage[1] = (byte)usernameBytes.Length;
            Array.Copy(usernameBytes, 0, authMessage, 2, usernameBytes.Length);
            authMessage[2 + usernameBytes.Length] = (byte)passwordBytes.Length;
            Array.Copy(passwordBytes, 0, authMessage, 3 + usernameBytes.Length, passwordBytes.Length);

            return authMessage;
        }
        #endregion
    }
}