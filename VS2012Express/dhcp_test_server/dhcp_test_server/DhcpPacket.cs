using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace dhcp_test_server
{
    // 
    // RFC 2131
    // RFC 2132
    // の一部に対応
    // 
    public class DhcpPacket
    {
        public enum OP
        {
            BOOTREQUEST = 1,
            BOOTREPLY = 2,
        }

        public enum MessageType
        {
            unknown = 0, 
            DHCPDISCOVER = 1,
            DHCPOFFER = 2,
            DHCPREQUEST = 3,
            DHCPDECLINE = 4,
            DHCPACK = 5,
            DHCPNAK = 6,
            DHCPRELEASE = 7,
            DHCPINFORM = 8,
        }

        protected OP op_ { get; private set; }
        protected byte htype_ { get; private set; }
        protected byte hlen_ { get; private set; }
        public UInt32 xid_ { get; private set; }
        protected IPAddress ciaddr_ { get; private set; }
        protected IPAddress yiaddr_ { get; private set; }
        protected IPAddress siaddr_ { get; private set; }
        protected IPAddress giaddr_ { get; private set; }
        protected PhysicalAddress chaddr_ { get; private set; }
        protected DhcpOptions options_ { get; private set; }

        // その他情報
        public MessageType message_type_
        {
            get { return this.options_.message_type_; }
        }
        public DhcpOption55 dhcpoption55_
        {
            get { return this.options_.dhcpoption55_; }
        }

        // 定数
        private const int BEFORE_OPTION_SIZE = 236; // opからfileまで

        // コンストラクタ
        private DhcpPacket()
        {
            this.options_ = null;
        }

        // 送信パケットを作成
        public byte[] GetPacketBytes()
        {
            List<byte> list = new List<byte>();

            DhcpPacket.addBigEndian(ref list, (byte)(this.op_));
            DhcpPacket.addBigEndian(ref list, this.htype_);
            DhcpPacket.addBigEndian(ref list, this.hlen_);
            DhcpPacket.addBigEndian(ref list, (byte)0); // hops
            DhcpPacket.addBigEndian(ref list, this.xid_);
            DhcpPacket.addBigEndian(ref list, (UInt16)0); // secs
            DhcpPacket.addBigEndian(ref list, (UInt16)0x8000); // flags // 決め打ち
            DhcpPacket.addBigEndian(ref list, this.ciaddr_);
            DhcpPacket.addBigEndian(ref list, this.yiaddr_);
            DhcpPacket.addBigEndian(ref list, this.siaddr_);
            DhcpPacket.addBigEndian(ref list, this.giaddr_);
            DhcpPacket.addBigEndian(ref list, this.chaddr_);
            list.AddRange(new byte[64 + 128]);// sname, file 

            list.AddRange(this.options_.GetPacketBytes()); // options

            return list.ToArray();
        }
        public static void addBigEndian(ref List<byte> list, byte v)
        {
            list.Add(v);
        }
        public static void addBigEndian(ref List<byte> list, UInt16 v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            if (BitConverter.IsLittleEndian) { Array.Reverse(bytes); }
            list.AddRange(bytes);
        }
        public static void addBigEndian(ref List<byte> list, UInt32 v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            if (BitConverter.IsLittleEndian) { Array.Reverse(bytes); }
            list.AddRange(bytes);
        }
        public static void addBigEndian(ref List<byte> list,IPAddress v)
        {
            list.AddRange(v.GetAddressBytes());
        }
        public static void addBigEndian(ref List<byte> list, PhysicalAddress v)
        {
            byte[] bytes = new byte[16];
            int len= v.GetAddressBytes().Length;
            if (len > bytes.Length)
            {
                len = bytes.Length;
            }
            Array.Copy(v.GetAddressBytes(), bytes, len);
            list.AddRange(bytes);
        }

        // コンストラクタ
        // 受信パケットから作成
        public DhcpPacket(byte[] rcvBytes)
            : this()
        {
            if (rcvBytes.Length < BEFORE_OPTION_SIZE)
            {
                // 
                return;
            }

            // 受信パケットを解析する
            this.op_ = (OP)rcvBytes[0];
            this.htype_ = rcvBytes[1];
            this.hlen_ = rcvBytes[2];
            {
                int xid_tmp = (rcvBytes[4] << 24) | (rcvBytes[5] << 16) | (rcvBytes[6] << 8) | rcvBytes[7];
                this.xid_ = (UInt32)xid_tmp;
            }
            this.ciaddr_ = new IPAddress(rcvBytes.Skip(12).Take(4).ToArray());
            this.yiaddr_ = new IPAddress(rcvBytes.Skip(16).Take(4).ToArray());
            this.siaddr_ = new IPAddress(rcvBytes.Skip(20).Take(4).ToArray());
            this.giaddr_ = new IPAddress(rcvBytes.Skip(24).Take(4).ToArray());
            this.chaddr_ = new PhysicalAddress(rcvBytes.Skip(28).Take(16).ToArray());

            // 受信パケットからoptionsに変換する
            {
                int optionsize = rcvBytes.Length - BEFORE_OPTION_SIZE;
                byte[] optionBytes = rcvBytes.Skip(BEFORE_OPTION_SIZE).Take(optionsize).ToArray();
                this.options_ = new DhcpOptions(optionBytes);
            }
        }

        // コンストラクタ
        // その他のパケットをベースに作成
        public DhcpPacket(DhcpPacket basePacket, DhcpPacket.MessageType message_type, DhcpLeases dhcplease)
            : this()
        {
            if (message_type == MessageType.DHCPOFFER)
            {
                BuildDHCPOFFER(basePacket, dhcplease);
            }
            if (message_type == MessageType.DHCPACK)
            {
                BuildDHCPACK(basePacket, dhcplease);
            }
        }
        private void BuildDHCPOFFER(DhcpPacket basePacket, DhcpLeases dhcplease)
        {
            DhcpLeaseItem item = dhcplease.GetItem(basePacket.chaddr_);

            this.op_ = OP.BOOTREPLY;
            this.htype_ = basePacket.htype_;
            this.hlen_ = basePacket.hlen_;
            this.xid_ = basePacket.xid_;
            this.ciaddr_ = new IPAddress(0);
            this.yiaddr_ = item.yiaddr_;
            this.siaddr_ = new IPAddress(0); // 仮
            this.giaddr_ = basePacket.giaddr_;
            this.chaddr_ = basePacket.chaddr_;

            this.options_ = new DhcpOptions(
                DhcpPacket.MessageType.DHCPOFFER,
                basePacket.chaddr_,
                basePacket.dhcpoption55_,
                dhcplease
            );
        }
        private void BuildDHCPACK(DhcpPacket basePacket, DhcpLeases dhcplease)
        {
            DhcpLeaseItem item = dhcplease.GetItem(basePacket.chaddr_);

            this.op_ = OP.BOOTREPLY;
            this.htype_ = basePacket.htype_;
            this.hlen_ = basePacket.hlen_;
            this.xid_ = basePacket.xid_;
            this.ciaddr_ = basePacket.ciaddr_;
            this.yiaddr_ = item.yiaddr_;
            this.siaddr_ = new IPAddress(0); // 仮
            this.giaddr_ = basePacket.giaddr_;
            this.chaddr_ = basePacket.chaddr_;

            this.options_ = new DhcpOptions(
                DhcpPacket.MessageType.DHCPACK,
                basePacket.chaddr_,
                basePacket.dhcpoption55_,
                dhcplease
            );
        }

    }

    public class DhcpOptions
    {
        private UInt32 magic_cookie_ { get; set; }
        private List<DhcpOption> dhcpoptions_ { get; set; }

        // その他情報
        public DhcpPacket.MessageType message_type_
        {
            get {
                DhcpPacket.MessageType ret = DhcpPacket.MessageType.unknown;
                foreach (DhcpOption option in this.dhcpoptions_)
                {
                    if (option is DhcpOption53)
                    {
                        ret = (option as DhcpOption53).message_type_;
                        break;
                    }
                }
                return ret;
            }
        }
        public DhcpOption55 dhcpoption55_
        {
            get
            {
                foreach (DhcpOption option in this.dhcpoptions_)
                {
                    if (option is DhcpOption55)
                    {
                        return (option as DhcpOption55);
                    }
                }

                // 見つからなかった
                return new DhcpOption55();
            }
        }

        // コンストラクタ
        private DhcpOptions() {
            this.dhcpoptions_ = new List<DhcpOption>();
        }
        // 送信パケットを作成
        public byte[] GetPacketBytes()
        {
            List<byte> list = new List<byte>();
            list.AddRange(new byte[4] { 99, 130, 83, 99 });
            foreach (DhcpOption option in this.dhcpoptions_)
            {
                list.AddRange(option.GetPacketBytes());
            }
            return list.ToArray();
        }

        // コンストラクタ
        // 受信パケットベース
        public DhcpOptions(byte[] optionsBytes)
            : this()
        {
            int pos = 0;
            // 受信パケットを解析する

            // magic cookie (decimal 99.130.83.99 (or hexadecimal number 63.82.53.63))
            {
                int magic_cookie_tmp = (optionsBytes[0] << 24) | (optionsBytes[1] << 16) | (optionsBytes[2] << 8) | optionsBytes[3];
                this.magic_cookie_ = (UInt32)magic_cookie_tmp;
                pos += 4;
            }

            // 続くoptionsを、生成が失敗する or End Optionで終了するまで繰り返し生成する
            while (true)
            {
                DhcpOption dhcpoption = DhcpOption.Parse(optionsBytes, ref pos);
                if (dhcpoption == null)
                {
                    break;
                }
                this.dhcpoptions_.Add(dhcpoption);
            }
        }

        // コンストラクタ
        // 受信パケットの Parameter Request List ベース
        public DhcpOptions(DhcpPacket.MessageType message_type, PhysicalAddress chaddr, DhcpOption55 dhcpoption55, DhcpLeases dhcplease)
            : this()
        {
            if (message_type == DhcpPacket.MessageType.DHCPOFFER)
            {
                BuildDHCPOFFER(chaddr, dhcpoption55, dhcplease);
            }
            if (message_type == DhcpPacket.MessageType.DHCPACK)
            {
                BuildDHCPACK(chaddr, dhcpoption55, dhcplease);
            }
        }
        private void BuildDHCPOFFER(PhysicalAddress chaddr, DhcpOption55 dhcpoption55, DhcpLeases dhcplease)
        {
            DhcpLeaseItem item = dhcplease.GetItem(chaddr);

            // 必須
            this.dhcpoptions_.Add(new DhcpOption53(DhcpPacket.MessageType.DHCPOFFER));
            this.dhcpoptions_.Add(new DhcpOption(54, item.server_identifier_));
            this.dhcpoptions_.Add(new DhcpOption(51, item.lease_time_));

            // 任意
            if (dhcpoption55.parameter_request_list_ != null)
            {
                foreach (byte b in dhcpoption55.parameter_request_list_)
                {
                    switch (b)
                    {
                        case 1: this.dhcpoptions_.Add(new DhcpOption(1, item.subnet_mask_)); break;
                        case 3: this.dhcpoptions_.Add(new DhcpOption(3, item.router_)); break;
                        case 6: this.dhcpoptions_.Add(new DhcpOption(6, item.server_identifier_)); break;
                        case 58: this.dhcpoptions_.Add(new DhcpOption(58, item.renewal_time_)); break;
                        case 59: this.dhcpoptions_.Add(new DhcpOption(59, item.rebinding_time_)); break;
                        default: break;
                    }
                }
            }

            // 必須（最後必ず）
            this.dhcpoptions_.Add(new DhcpOption(255));
        }
        private void BuildDHCPACK(PhysicalAddress chaddr, DhcpOption55 dhcpoption55, DhcpLeases dhcplease)
        {
            DhcpLeaseItem item = dhcplease.GetItem(chaddr);

            // 必須
            this.dhcpoptions_.Add(new DhcpOption53(DhcpPacket.MessageType.DHCPACK));
            this.dhcpoptions_.Add(new DhcpOption(54, item.server_identifier_));
            this.dhcpoptions_.Add(new DhcpOption(51, item.lease_time_));

            // 任意
            if (dhcpoption55.parameter_request_list_ != null)
            {
                foreach (byte b in dhcpoption55.parameter_request_list_)
                {
                    switch (b)
                    {
                        case 1: this.dhcpoptions_.Add(new DhcpOption(1, item.subnet_mask_)); break;
                        case 3: this.dhcpoptions_.Add(new DhcpOption(3, item.router_)); break;
                        case 6: this.dhcpoptions_.Add(new DhcpOption(6, item.domain_name_server_)); break;
                        case 58: this.dhcpoptions_.Add(new DhcpOption(58, item.renewal_time_)); break;
                        case 59: this.dhcpoptions_.Add(new DhcpOption(59, item.rebinding_time_)); break;
                        default: break;
                    }
                }
            }

            // 必須（最後必ず）
            this.dhcpoptions_.Add(new DhcpOption(255));
        }
    }

    public class DhcpOption
    {
        protected int _Code { get; set; }
        private int _Len { get; set; }
        protected byte[] _bytes { get; set; }

        private const byte CODE_PAD_OPTION = 0x00;
        private const byte CODE_END_OPTION = 0xFF;

        // コンストラクタ
        public DhcpOption() { }
        // コンストラクタ（構築）（簡易版）
        public DhcpOption(int code)
            : this()
        {
            this._Code = code;
        }
        public DhcpOption(int code, UInt32 uint32)
            : this()
        {
            this._Code = code;
            this._bytes = BitConverter.GetBytes(uint32);
            if (BitConverter.IsLittleEndian) { Array.Reverse(this._bytes); }
        }
        public DhcpOption(int code, IPAddress ipaddress)
            : this()
        {
            this._Code = code;
            this._bytes = ipaddress.GetAddressBytes();
        }

        // 送信パケットを作成
        public byte[] GetPacketBytes()
        {
            if ((this._bytes == null) || (this._bytes.Length == 0)){
                // End Option として扱う
                return new byte[1] { CODE_END_OPTION };
            }

            byte[] bytes = new byte[this._bytes.Length + 2];
            bytes[0] = (byte)this._Code;
            bytes[1] = (byte)this._bytes.Length;
            Array.Copy(this._bytes, 0, bytes, 2, this._bytes.Length);

            return bytes;
        }

        // 解析
        public virtual void Analyze() {
            // 基底クラスはnop
        }

        // 解析
        public static DhcpOption Parse(byte[] optionBytes, ref int pos)
        {
            // Pad Optionをスキップ
            while ((pos < optionBytes.Length) && (optionBytes[pos] == CODE_PAD_OPTION))
            {
                pos++;
            }
            if (pos >= optionBytes.Length)
            {
                return null;
            }

            // 生成
            int code = optionBytes[pos++];
            DhcpOption dhcpoption = null;
            switch (code)
            {
                case 53: dhcpoption = new DhcpOption53(); break;
                case 55: dhcpoption = new DhcpOption55(); break;
                default: dhcpoption = new DhcpOption(); break;
            }
            dhcpoption._Code = code;

            // End Option
            if (dhcpoption._Code == CODE_END_OPTION)
            {
                dhcpoption._Len = 0;
                dhcpoption._bytes = null;
                return dhcpoption;
            }

            dhcpoption._Len = optionBytes[pos ++];
            if (pos + dhcpoption._Len > optionBytes.Length)
            {
                return null;
            }
            dhcpoption._bytes = optionBytes.Skip(pos).Take(dhcpoption._Len).ToArray();
            pos += dhcpoption._Len;

            // 解析
            dhcpoption.Analyze();

            return dhcpoption;
        }
    }


    public class DhcpOption53 : DhcpOption
    {
        public DhcpPacket.MessageType message_type_ { get; private set; }

        // コンストラクタ
        public DhcpOption53() : base() { }
        // コンストラクタ（構築）
        public DhcpOption53(DhcpPacket.MessageType message_type)
            : this()
        {
            this._Code = 53;
            this.message_type_ = message_type;
            this._bytes = new byte[1] { (byte)message_type };
        }

        // 解析
        public override void Analyze()
        {
            this.message_type_ = (DhcpPacket.MessageType)(this._bytes[0]);
            if (
                (DhcpPacket.MessageType.DHCPDISCOVER <= this.message_type_)
                && (this.message_type_ <= DhcpPacket.MessageType.DHCPINFORM)
             )
            {
                // OK
            }
            else
            {
                this.message_type_ = DhcpPacket.MessageType.unknown;
            }
        }
    }

    public class DhcpOption55 : DhcpOption
    {
        public byte[] parameter_request_list_ { get; private set; }
        // コンストラクタ
        public DhcpOption55() : base() {
            this.parameter_request_list_ = null;
        }

        // 解析
        public override void Analyze()
        {
            // this._bytesそのまま
            int len = this._bytes.Length;
            this.parameter_request_list_ = new byte[len];
            Array.Copy(this._bytes, this.parameter_request_list_, len);
        }
    }

}
