using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Crypto;
using ProtoBuf;

namespace Avalon.Client.Tester
{
    public partial class UdpForm : Form
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly Socket socket;


        public UdpForm()
        {
            InitializeComponent();

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }

        private async void UdpForm_Load(object sender, EventArgs e)
        {
        }

        private void UdpForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            cts.Cancel();
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            var endpoint = IPEndPoint.Parse("127.0.0.1");
            endpoint.Port = 21500;
            
            var reqPKeyPacket = new CRequestCryptoKeyPacket();
            using var buffer = new MemoryStream();
            using var ms = new MemoryStream();

            Serializer.Serialize(ms, reqPKeyPacket);

            var packet = new NetworkPacket
            {
                Header = new NetworkPacketHeader
                {
                    Type = NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY,
                    Flags = NetworkPacketFlags.None,
                    Version = 0
                },
                Payload = ms.ToArray()
            };

            Serializer.SerializeWithLengthPrefix(buffer, packet, PrefixStyle.Base128);

            await socket.SendToAsync(buffer.ToArray(), endpoint);
        }

        private void OnDataReceived(EndPoint arg1, byte[] arg2)
        {
            Console.WriteLine($"Received {arg2.Length} bytes from {arg1}");
        }
    }
}
