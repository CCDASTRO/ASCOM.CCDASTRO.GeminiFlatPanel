using System;
using System.IO.Ports;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("GeminiFlatPanel.Tests")]
namespace GeminiFlatPanel.Server
{
    internal interface ISerialChannel : IDisposable
    {
        bool IsOpen { get; }
        void Open();
        void Close();
        int ReadChar();
        void Write(string text);
    }
    internal sealed class SerialChannel : ISerialChannel
    {
        private readonly SerialPort port;
        public SerialChannel(string name) { port = new SerialPort(name, 9600, Parity.None, 8, StopBits.One) { Handshake = Handshake.None, DtrEnable = true, RtsEnable = true, ReadTimeout = 200, WriteTimeout = 3000 }; }
        public bool IsOpen => port.IsOpen;
        public void Open() => port.Open();
        public void Close() => port.Close();
        public int ReadChar() => port.ReadChar();
        public void Write(string text) => port.Write(text);
        public void Dispose() => port.Dispose();
    }
}
