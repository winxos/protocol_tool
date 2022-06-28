using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO.Ports;
using System.Threading;
using Newtonsoft.Json;
using System.Windows.Threading;

namespace ungrain_tool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        List<byte> rx_buf = new List<byte>();
        SerialPort _sp = new SerialPort();
        ManualResetEvent _sp_flag = new ManualResetEvent(false);
        Queue<byte[]> _frames = new Queue<byte[]>();
        public class ArgItem
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
        }
        List<ArgItem> _args = new List<ArgItem>();
        enum PPP_STATE
        {
            S_IDLE,
            S_LEN,
            S_CMD,
            S_DATA,
            S_CRC
        }
        class PPP_Frame
        {
            public int len;
            public int cmd;
            public int count;
            public int crc8;
            public byte[] data;
        }
        private byte crc8_calc(byte[] b, int len)
        {
            byte[] buffer = new byte[] {
                0, 0x5e, 0xbc, 0xe2, 0x61, 0x3f, 0xdd, 0x83, 0xc2, 0x9c, 0x7e, 0x20, 0xa3, 0xfd, 0x1f, 0x41,
                0x9d, 0xc3, 0x21, 0x7f, 0xfc, 0xa2, 0x40, 30, 0x5f, 1, 0xe3, 0xbd, 0x3e, 0x60, 130, 220,
                0x23, 0x7d, 0x9f, 0xc1, 0x42, 0x1c, 0xfe, 160, 0xe1, 0xbf, 0x5d, 3, 0x80, 0xde, 60, 0x62,
                190, 0xe0, 2, 0x5c, 0xdf, 0x81, 0x63, 0x3d, 0x7c, 0x22, 0xc0, 0x9e, 0x1d, 0x43, 0xa1, 0xff,
                70, 0x18, 250, 0xa4, 0x27, 0x79, 0x9b, 0xc5, 0x84, 0xda, 0x38, 0x66, 0xe5, 0xbb, 0x59, 7,
                0xdb, 0x85, 0x67, 0x39, 0xba, 0xe4, 6, 0x58, 0x19, 0x47, 0xa5, 0xfb, 120, 0x26, 0xc4, 0x9a,
                0x65, 0x3b, 0xd9, 0x87, 4, 90, 0xb8, 230, 0xa7, 0xf9, 0x1b, 0x45, 0xc6, 0x98, 0x7a, 0x24,
                0xf8, 0xa6, 0x44, 0x1a, 0x99, 0xc7, 0x25, 0x7b, 0x3a, 100, 0x86, 0xd8, 0x5b, 5, 0xe7, 0xb9,
                140, 210, 0x30, 110, 0xed, 0xb3, 0x51, 15, 0x4e, 0x10, 0xf2, 0xac, 0x2f, 0x71, 0x93, 0xcd,
                0x11, 0x4f, 0xad, 0xf3, 0x70, 0x2e, 0xcc, 0x92, 0xd3, 0x8d, 0x6f, 0x31, 0xb2, 0xec, 14, 80,
                0xaf, 0xf1, 0x13, 0x4d, 0xce, 0x90, 0x72, 0x2c, 0x6d, 0x33, 0xd1, 0x8f, 12, 0x52, 0xb0, 0xee,
                50, 0x6c, 0x8e, 0xd0, 0x53, 13, 0xef, 0xb1, 240, 0xae, 0x4c, 0x12, 0x91, 0xcf, 0x2d, 0x73,
                0xca, 0x94, 0x76, 40, 0xab, 0xf5, 0x17, 0x49, 8, 0x56, 180, 0xea, 0x69, 0x37, 0xd5, 0x8b,
                0x57, 9, 0xeb, 0xb5, 0x36, 0x68, 0x8a, 0xd4, 0x95, 0xcb, 0x29, 0x77, 0xf4, 170, 0x48, 0x16,
                0xe9, 0xb7, 0x55, 11, 0x88, 0xd6, 0x34, 0x6a, 0x2b, 0x75, 0x97, 0xc9, 0x4a, 20, 0xf6, 0xa8,
                0x74, 0x2a, 200, 150, 0x15, 0x4b, 0xa9, 0xf7, 0xb6, 0xe8, 10, 0x54, 0xd7, 0x89, 0x6b, 0x35
            };
            byte num = 0;
            for (int i = 0; i < len; i++)
            {
                num = buffer[num ^ b[i]];
            }
            return num;
        }
        private byte[] translate(byte[] s)
        {
            List<byte> bs = new List<byte>();
            bs.Add(0x7e);
            for (int i = 1; i < s.Length - 1; i++)
            {
                if (s[i] == 0x7e)
                {
                    bs.Add(0x7d);
                    bs.Add(0x5e);
                }
                else if (s[i] == 0x7d)
                {
                    bs.Add(0x7d);
                    bs.Add(0x5d);
                }
                else
                {
                    bs.Add(s[i]);
                }
            }
            bs.Add(0x7e);
            return bs.ToArray();
        }
        void serial_received()
        {
            int last_received_timeout = 0;
            List<byte> frame = new List<byte>();
            bool is_ticking = false;
            const int idle_tick = 2;
            while (true)
            {
                if (_sp_flag.WaitOne())
                {
                    while (_sp.BytesToRead > 0)
                    {
                        if (is_ticking == false)
                        {
                            is_ticking = true;
                            frame.Clear();//数据上升沿
                        }
                        frame.Add((byte)_sp.ReadByte());
                        last_received_timeout = 0;
                    }
                    if (is_ticking)
                    {
                        last_received_timeout++;

                        if (last_received_timeout >= idle_tick)
                        {
                            //idle callback
                            _frames.Enqueue(frame.ToArray());
                            is_ticking = false;
                        }
                    }
                }
                Thread.Sleep(2);
            }
        }
        void analyse(byte[] bs)
        {
            if (bs[0] == 0xf0) //read config
            {
                byte[] b = new byte[bs[1] + 2];
                Array.Copy(bs, 2, b, 0, bs[1]);
                string s = Encoding.Default.GetString(b);
                Dictionary<string, string>  args = JsonConvert.DeserializeObject<Dictionary<string, string>>(s);
                Dispatcher.Invoke(new Action(() => {
                    _args.Clear();
                    foreach (var item in args)
                    {
                        ArgItem item2 = new ArgItem();
                        item2.Id = _args.Count;
                        item2.Name = item.Key;
                        item2.Value = item.Value;
                        _args.Add(item2);
                    }
                    config_grid.ItemsSource = _args;
                }));
                //config.delay_cam1 = (bs[3] << 8) + bs[2];
                //config.delay_cam2 = (bs[5] << 8) + bs[4];
                //config.run_times = (bs[7] << 8) + bs[6];
                //config.out1_freq = (bs[9] << 8) + bs[8];
                //config.out1_mag = (bs[11] << 8) + bs[10];
                //config.out2_freq = (bs[13] << 8) + bs[12];
                //config.out2_mag = (bs[15] << 8) + bs[14];
                //config.out4_freq = (bs[17] << 8) + bs[16];
                //config.out4_mag = (bs[19] << 8) + bs[18];
                //config.target_speed = (bs[21] << 8) + bs[20];
                //config.auto_stop_time = (bs[23] << 8) + bs[22];
            }
            
        }
        void action(byte[] bs)
        {
            analyse(bs);
        }
        void callback()
        {
            while (true)
            {
                if (_sp_flag.WaitOne())
                {
                    if (_frames.Count > 0)
                    {
                        action(_frames.Dequeue());
                    }
                }
                Thread.Sleep(10);
            }
        }
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            List<string> lb = new List<string>() { "9600","115200"};
            com_baud.ItemsSource=lb;
            com_baud.SelectedIndex = 1;
            com_port.ItemsSource = SerialPort.GetPortNames();
            com_port.SelectedIndex = 0;
            new Thread(serial_received).Start();
            new Thread(callback).Start();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            get_config();
        }
        private void send_bytes(byte[] ds)
        {
            if (this._sp.IsOpen)
            {
                this._sp.Write(ds, 0, ds.Length);
            }
            else
            {
                MessageBox.Show("请先连接串口");
            }
        }
        private void get_config()
        {
            byte[] ds = new byte[] { 0x7e, 1, 0xf0, 0, 0x7e };
            byte[] tp = new byte[2];
            Array.Copy(ds, 1, tp, 0, 2);
            ds[3] = crc8_calc(tp, 2);
            send_bytes(translate(ds));
        }
        private void get_version()
        {
            byte[] ds = new byte[] { 0x7e, 1, 0xf2, 0, 0x7e };
            byte[] tp = new byte[2];
            Array.Copy(ds, 1, tp, 0, 2);
            ds[3] = crc8_calc(tp, 2);
            send_bytes(translate(ds));
        }
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (button1.Content.ToString() == "连接设备")
            {
                _sp.PortName = com_port.SelectedItem.ToString();
                _sp.BaudRate = int.Parse(com_baud.SelectedValue.ToString());
                _sp.Encoding = Encoding.UTF8;
                _sp.Open();
                _sp_flag.Set();
                button1.Content = "断开设备";
                Task.Run(async () => {
                    await Task.Delay(500);
                    get_version();
                });
            }
            else
            {
                _sp_flag.Reset();
                _sp.Close();
                button1.Content = "连接设备";
            }
        }
    }
}
