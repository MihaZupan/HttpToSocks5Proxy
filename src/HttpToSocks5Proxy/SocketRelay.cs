using System.Net.Sockets;
using System.Threading.Tasks;

namespace MihaZupan
{
    internal class SocketRelay
    {
        private SocketAsyncEventArgs RecSAEA, SendSAEA;
        private Socket Source, Target;
        private byte[] Buffer;
        private int SendingOffset;
        private int Received;
        private int StackDepth;
        private const int StackDepthCutoff = 256;

        public SocketRelay Other;

        public SocketRelay(Socket source, Socket target)
        {
            Source = source;
            Target = target;
            Buffer = new byte[81920];
            RecSAEA = new SocketAsyncEventArgs()
            {
                UserToken = this
            };
            SendSAEA = new SocketAsyncEventArgs()
            {
                UserToken = this
            };
            RecSAEA.SetBuffer(Buffer, 0, Buffer.Length);
            SendSAEA.SetBuffer(Buffer, 0, Buffer.Length);
            RecSAEA.Completed += OnSaeaReceiveCompleted;
            SendSAEA.Completed += OnSaeaSendCompleted;
        }

        private void OnCleanup()
        {
            if (Other is null)
                return;

            Source.TryDispose();
            Target.TryDispose();
            try { RecSAEA?.Dispose(); } catch { }
            try { SendSAEA?.Dispose(); } catch { }

            Source = Target = null;
            RecSAEA = SendSAEA = null;
            Buffer = null;

            Other?.OnCleanup();
            Other = null;
        }

        public void StartRelaying()
        {
            try
            {
                if (!Source.ReceiveAsync(RecSAEA))
                {
                    OnReceived();
                }
            }
            catch
            {
                OnCleanup();
            }
        }
        private void OnReceived()
        {
            try
            {
                SendingOffset = 0;
                Received = RecSAEA.BytesTransferred;

                SendSAEA.SetBuffer(Buffer, 0, Received);

                if (!Target.SendAsync(SendSAEA))
                {
                    OnSent();
                }
            }
            catch
            {
                OnCleanup();
            }
        }
        private void OnSent()
        {
            try
            {
                SendingOffset += SendSAEA.BytesTransferred;

                while (SendingOffset != Received)
                {
                    SendSAEA.SetBuffer(Buffer, SendingOffset, Received - SendingOffset);

                    if (Target.SendAsync(SendSAEA))
                        return;

                    SendingOffset += SendSAEA.BytesTransferred;
                }

                if (++StackDepth == StackDepthCutoff)
                {
                    StackDepth = 0;
                    Task.Run(StartRelaying);
                }
                else
                {
                    StartRelaying();
                }
            }
            catch
            {
                OnCleanup();
            }
        }

        private static void OnSaeaReceiveCompleted(object _, SocketAsyncEventArgs saea)
        {
            var relay = saea.UserToken as SocketRelay;
            relay.StackDepth = 0;
            relay.OnReceived();
        }
        private static void OnSaeaSendCompleted(object _, SocketAsyncEventArgs saea)
        {
            var relay = saea.UserToken as SocketRelay;
            relay.StackDepth = 0;
            relay.OnSent();
        }

        public static void RelayBiDirectionally(Socket s1, Socket s2)
        {
            var relayOne = new SocketRelay(s1, s2);
            var relayTwo = new SocketRelay(s2, s1);

            relayOne.Other = relayTwo;
            relayTwo.Other = relayOne;

            Task.Run(relayOne.StartRelaying);
            Task.Run(relayTwo.StartRelaying);
        }
    }
}
