using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace dhcp_test_server
{
    public class DhcpLeases
    {
        public List<DhcpLeaseItem> items_;

        // 共通情報
        public static IPAddress SERVER_IDENTIFIER_ = IPAddress.Parse("192.168.12.1"); // 初期値
        public static IPAddress SUBNET_MASK_ = IPAddress.Parse("255.255.127.0"); // 初期値
        public static IPAddress DOMAIN_NAME_SERVER_ = IPAddress.Parse("192.168.13.1"); // 初期値
        public static IPAddress ROUTER_ = IPAddress.Parse("192.168.14.1"); // 初期値

        // コンストラクタ
        public DhcpLeases()
        {
            items_ = new List<DhcpLeaseItem>();

            // 仮
            this.items_.Add(new DhcpLeaseItem());
            this.items_.Add(new DhcpLeaseItem());
            this.items_.Add(new DhcpLeaseItem());
        }

        // 現在のネットワーク状況に依存する情報の更新
        public void UpdateNetworkInformation(IPAddress ipaddress)
        {
            // 全てのネットワークインタフェースの情報を取得し、
            // 引数で指定されたIPアドレスを持つインタフェースの情報を採用する
            IPInterfaceProperties ipproperties = null;
            UnicastIPAddressInformation information = null;

            // 全てのネットワークインタフェース
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface ni in interfaces)
            {
                // ネットワーク接続している場合
                if (
                    (ni.OperationalStatus == OperationalStatus.Up)
                    && (ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    && (ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                )
                {
                    IPInterfaceProperties ipip = ni.GetIPProperties();
                    if (ipip != null)
                    {
                        foreach (UnicastIPAddressInformation inf in ipip.UnicastAddresses)
                        {
                            if (inf.Address.Equals(ipaddress))
                            {
                                ipproperties = ipip;
                                information = inf;
                            }
                        }
                    }
                }
            }

            // 情報取得
            if ((ipproperties != null) && (information != null))
            {
                // 自身のIPアドレス
                DhcpLeases.SERVER_IDENTIFIER_ = ipaddress;

                // サブネットマスク
                DhcpLeases.SUBNET_MASK_ = information.IPv4Mask;

                // GW
                DhcpLeases.ROUTER_ = new IPAddress(0);
                if (ipproperties.GatewayAddresses != null)
                {
                    DhcpLeases.ROUTER_ = ipproperties.GatewayAddresses[0].Address;
                }

                // DNS
                DhcpLeases.DOMAIN_NAME_SERVER_ = DhcpLeases.ROUTER_; // 取得できない場合はGWと同じとする
                if (ipproperties.DnsAddresses != null)
                {
                    DhcpLeases.DOMAIN_NAME_SERVER_ = ipproperties.DnsAddresses[0];
                }
            }
        }

        // MACアドレスから取得対象のレコードを返却
        public DhcpLeaseItem GetItem(PhysicalAddress chaddr)
        {
            foreach (DhcpLeaseItem item in this.items_)
            {
                if (item.chaddr_.Equals(chaddr))
                {
                    return item;
                }
            }
            DhcpLeaseItem newitem = new DhcpLeaseItem(chaddr);
            this.items_.Add(newitem);
            return newitem;
        }

    }

    public class DhcpLeaseItem
    {
        // 以下全て、試験用に任意にユーザが編集可能とするために private set; ではなく public set; としている
        public PhysicalAddress chaddr_ { get; set; }
        public IPAddress yiaddr_ { get; set; }
        public UInt32 lease_time_ { get; set; }
        public UInt32 renewal_time_ { get; set; }
        public UInt32 rebinding_time_ { get; set; }
        public IPAddress server_identifier_ { get; set; }
        public IPAddress subnet_mask_ { get; set; }
        public IPAddress router_ { get; set; }
        public IPAddress domain_name_server_ { get; set; } 

        // コンストラクタ
        public DhcpLeaseItem()
        {
            // 仮
            this.chaddr_ = new PhysicalAddress(new byte[] { 1, 2, 3, 4, 5, 6 });
            this.yiaddr_ = IPAddress.Parse("192.168.0.123");
            this.lease_time_ = 3600;

            // 現在の接続状態に依存して決定
            this.server_identifier_ = DhcpLeases.SERVER_IDENTIFIER_;
            this.subnet_mask_ = DhcpLeases.SUBNET_MASK_;
            this.router_ = DhcpLeases.ROUTER_;
            this.domain_name_server_ = DhcpLeases.DOMAIN_NAME_SERVER_;

            // 決め打ち
            this.renewal_time_ = this.lease_time_ / 2; // 50%
            this.rebinding_time_ = this.lease_time_ * 875 / 1000; // 87.5%
        }
        public DhcpLeaseItem(PhysicalAddress chaddr)
            : this()
        {
            this.chaddr_ = chaddr;
        }
    }
}
