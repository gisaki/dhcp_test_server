using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace dhcp_test_server
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        // 
        // フォームの表示関連
        // 

        private void Form1_Load(object sender, EventArgs e)
        {
            {
                //  DHCPの設定とリース情報
                this.dhcplease_ = new DhcpLeases();
                this.dataGridViewDhcpLeases.DataSource = this.dhcplease_.items_;
                this.dataGridViewDhcpLeases.MultiSelect = false; // セル、行、列が複数選択されないように
                this.dataGridViewDhcpLeases.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells; // 列の幅を自動調整
            }
            {
                // IPアドレス一覧の更新
                UpdateIPAddressList();
            }
        }

        // IPアドレス一覧の更新
        private void UpdateIPAddressList()
        {
            // IPアドレスを取得
            String hostName = System.Net.Dns.GetHostName();    // 自身のホスト名を取得
            System.Net.IPAddress[] addresses = System.Net.Dns.GetHostAddresses(hostName);
            comboBoxSourceIPs.Items.Clear();
            foreach (System.Net.IPAddress address in addresses)
            {
                // IPv4のみ
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    comboBoxSourceIPs.Items.Add(address);
                }
            }
            comboBoxSourceIPs.SelectedIndex = 0;
        }

        // IPアドレス一覧のcomboBoxをDropDownする直前に、一覧の内容を最新化する
        private void comboBoxSourceIPs_DropDown(object sender, EventArgs e)
        {
            // IPアドレス一覧の更新
            UpdateIPAddressList();
        }


        // 
        // DHCPの設定とリース情報関連
        // 

        private DhcpLeases dhcplease_ = null;

        private void comboBoxSourceIPs_SelectedIndexChanged(object sender, EventArgs e)
        {
            // DHCPの設定のうち、現在のネットワーク状況に依存する情報の更新
            UpdateNetworkInformation();
        }

        // DHCPの設定のうち、現在のネットワーク状況に依存する情報の更新
        private void UpdateNetworkInformation()
        {
            IPAddress ipaddress = IPAddress.Parse(comboBoxSourceIPs.Text);
            this.dhcplease_.UpdateNetworkInformation(ipaddress);
        }
        
        private System.Net.Sockets.UdpClient udpClient_ = null;
        private IPAddress sourceIPAddress_;
        private int sourcePort_;

        private byte[] lastRcvBytes_;
        private System.Net.IPEndPoint lastRemoteEP_ = null;

        // 
        // UDP送受信関連
        // 

        // UDP受信
        private void buttonStart_Click(object sender, EventArgs e)
        {
            // 
            // 実施中→終了
            // 
            if (udpClient_ != null)
            {
                udpClient_.Close();
                udpClient_ = null;
                // ボタン等
                change_ui(false);
                return;
            }

            // 
            // 未実施→実施
            // 

            // 送信元
            try
            {
                sourcePort_ = Int32.Parse(textBoxBindPort.Text);
                sourceIPAddress_ = IPAddress.Parse(comboBoxSourceIPs.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return; // 開始せず中断
            }

            try
            {
                //UdpClientを作成し、指定したポート番号にバインドする
                System.Net.IPEndPoint localEP =
                    new System.Net.IPEndPoint(
                        System.Net.IPAddress.Any, //sourceIPAddress_, 
                        Int32.Parse(textBoxBindPort.Text)
                    );
                udpClient_ = new System.Net.Sockets.UdpClient(localEP);
                //非同期的なデータ受信を開始する
                udpClient_.BeginReceive(ReceiveCallback, udpClient_);
                // ボタン等
                change_ui(true);
            }
            catch (Exception ex)
            {
                if (udpClient_ != null)
                {
                    udpClient_.Close();
                }
                udpClient_ = null;

                MessageBox.Show(ex.Message, "error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }

        //データを受信した時
        private void ReceiveCallback(IAsyncResult ar)
        {
            System.Net.Sockets.UdpClient udp =
                (System.Net.Sockets.UdpClient)ar.AsyncState;

            //非同期受信を終了する
            System.Net.IPEndPoint remoteEP = null;
            byte[] rcvBytes;
            try
            {
                rcvBytes = udp.EndReceive(ar, ref remoteEP);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                Console.WriteLine("受信エラー({0}/{1})",
                    ex.Message, ex.ErrorCode);
                return;
            }
            catch (ObjectDisposedException ex)
            {
                //すでに閉じている時は終了
                Console.WriteLine("Socketは閉じられています。");
                return;
            }

            // 処理対象？
            DhcpPacket rcv_packet = new DhcpPacket(rcvBytes);
            Boolean reply = true;
            Boolean match = true;
            if (match)
            {
                // 受信情報を控えておく（送信データに載せるため）
                // 受信データ
                lastRcvBytes_ = new byte[rcvBytes.Length];
                Array.Copy(rcvBytes, 0, lastRcvBytes_, 0, rcvBytes.Length);
                // 受信元
                lastRemoteEP_ = remoteEP;
            }

            //データを文字列に変換する
            string rcvMsg = "0x" + rcv_packet.xid_.ToString("X4") + ", " + rcv_packet.message_type_;

            //受信したデータと送信者の情報をRichTextBoxに表示する
            string displayMsg = string.Format("{0} [{1} ({2})] < {3}",
                DateTime.Now.ToString("HH:mm:ss.fff"), remoteEP.Address, remoteEP.Port, rcvMsg);
            textBoxRcvData.BeginInvoke(
                new Action<string>(ShowReceivedString), displayMsg);

            // 応答する
            if (reply)
            {
                // 送信先のIPアドレスは remoteEP.Address ではなく、ブロードキャスト
                SendUDP(sourceIPAddress_, sourcePort_, IPAddress.Broadcast, remoteEP.Port);
            }

            //再びデータ受信を開始する
            udp.BeginReceive(ReceiveCallback, udp);
        }

        // UDP送信
        private void SendUDP(IPAddress sourceIPAddress, int sourcePort, IPAddress remoteIPAddress, int remotePort)
        {
            // 送信元
            var localEP = new IPEndPoint(sourceIPAddress, sourcePort);
            // 送信先
            var remoteEP = new IPEndPoint(remoteIPAddress, remotePort);
            // 送信データ
            DhcpPacket rcv_packet = new DhcpPacket(this.lastRcvBytes_);
            DhcpPacket.MessageType snd_message_type = 
                (rcv_packet.message_type_ == DhcpPacket.MessageType.DHCPDISCOVER)? DhcpPacket.MessageType.DHCPOFFER:
                (rcv_packet.message_type_ == DhcpPacket.MessageType.DHCPREQUEST)? DhcpPacket.MessageType.DHCPACK: 
                DhcpPacket.MessageType.unknown;
            DhcpPacket snd_packet = new DhcpPacket(rcv_packet, snd_message_type, this.dhcplease_);
            byte[] msg = snd_packet.GetPacketBytes();

            //データを文字列に変換する
            string rcvMsg = "0x" + snd_packet.xid_.ToString("X4") + ", " + snd_packet.message_type_;

            //送信したデータの情報をRichTextBoxに表示する
            string displayMsg = string.Format("{0} [{1} ({2})] > {3}",
                DateTime.Now.ToString("HH:mm:ss.fff"), remoteEP.Address, remoteEP.Port, rcvMsg);
            textBoxRcvData.BeginInvoke(
                new Action<string>(ShowReceivedString), displayMsg);

            // 送信イベントデータ
            var ev = new SocketAsyncEventArgs();
            ev.RemoteEndPoint = remoteEP;
            ev.SetBuffer(msg, 0, msg.Length);
            // UDP送信
            var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(localEP); // bindしたIP、ポートと異なる送信元としたい（場合がある）のでudpClient_は使用しない
            socket.SendToAsync(ev);                  // 非同期で送信
            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }

        public void change_ui(Boolean start)
        {
            if (start)
            {
                buttonStart.Text = "Abort UDP Listen";
                comboBoxSourceIPs.Enabled = false;
                textBoxBindPort.Enabled = false;
            }
            else
            {
                buttonStart.Text = "Start UDP Listen";
                comboBoxSourceIPs.Enabled = true;
                textBoxBindPort.Enabled = true;
            }
        }


        //RichTextBox1にメッセージを表示する
        private void ShowReceivedString(string str)
        {
            //// textBoxRcvData.Text = str;
            textBoxRcvData.AppendText(str + "\r\n");
            this.dataGridViewDhcpLeases.DataSource = null; // 暫定
            this.dataGridViewDhcpLeases.DataSource = this.dhcplease_.items_; // 暫定
        }

        //RichTextBox1のメッセージをクリアする
        private void labelReceivedPacket_DoubleClick(object sender, EventArgs e)
        {
            textBoxRcvData.Clear();
        }

    }
}
