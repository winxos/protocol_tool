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
using System.Reflection;
using System.Diagnostics;
using System.IO;

namespace ungrain_tool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        List<byte> rx_buf = new List<byte>();
        SerialPort _sp = new SerialPort();
        AutoResetEvent _sp_flag = new AutoResetEvent(false);
        Queue<byte[]> _frames = new Queue<byte[]>();
        public class ArgItem
        {
            public string Key { get; set; }
            public int Value { get; set; }
        }
        public class RawData
        {
            public DateTime tim { get; set; }
            public byte[] data { get; set; }
            public RawData(DateTime tim, byte[] data)
            {
                this.tim = tim;
                this.data = data;
            }
        }
        List<RawData> lrd = new List<RawData>();
        List<ArgItem> _args = new List<ArgItem>();
        Dictionary<string, int> args = new Dictionary<string, int>();
        List<string> verson_info_db = new List<string>();
        List<ActionItem> control_items = new List<ActionItem>();
        List<string> _com_args = new List<string>();
        byte add8(byte[]b,int len)
        {
            byte s = 0;
            for(int i = 0; i < len; i++)
            {
                s += b[i];
            }
            return s;
        }
        void serial_received()
        {
            int last_received_timeout = 0;
            List<byte> frame = new List<byte>();
            bool is_ticking = false;
            const int idle_tick = 5;
            while (true)
            {
                if (_sp.IsOpen)
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
                            _sp_flag.Set();
                            is_ticking = false;
                        }
                    }
                }
                Thread.Sleep(5);
            }
        }
        void analyse(byte[] bs)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                lrd.Add(new RawData(DateTime.Now, bs));
                update_history();
            }));
            if (bs[0] == '!') //read config
            {
                if (bs.Length<3 || (bs[1]!='v' && bs[1]!='a' && bs[1] !='c'))
                {
                    return;
                }
                byte[] b = new byte[bs.Length - 3];
                Array.Copy(bs, 2, b, 0, bs.Length - 3);
                string s = Encoding.Default.GetString(b);
                if (bs[1] == 'v') //ver
                {
                    var tmp = JsonConvert.DeserializeObject<Dictionary<string, string>>(s);
                    Dispatcher.Invoke(new Action(() =>
                    {
                        verson_info_db.Clear();
                        version_info.ItemsSource = null;
                        foreach (var item in tmp)
                        {
                            verson_info_db.Add(item.Key + ":\t" + item.Value);
                        }
                        version_info.ItemsSource = verson_info_db;
                    }));
                }
                else if (bs[1] == 'c') //config
                {
                    args = JsonConvert.DeserializeObject<Dictionary<string, int>>(s);
                    Dispatcher.Invoke(new Action(() =>
                    {
                        config_grid.ItemsSource = null;
                        _args.Clear();
                        foreach (var item in args)
                        {
                            ArgItem item2 = new ArgItem();
                            item2.Key = item.Key;
                            item2.Value = item.Value;
                            _args.Add(item2);
                        }
                        config_grid.ItemsSource = _args;
                    }));
                }
                else if (bs[1] =='a') //action
                {
                    Dictionary<string, int> control_args = JsonConvert.DeserializeObject<Dictionary<string, int>>(s);
                    Dispatcher.Invoke(new Action(() =>
                    {
                        control_items.Clear();
                        foreach (var item in control_args)
                        {
                            ArgItem item2 = new ArgItem();
                            item2.Key = item.Key;
                            item2.Value = item.Value;
                            control_items.Add(new ActionItem() { Val = 0, Ena = item2.Key});
                        }
                        icTodoList.ItemsSource = control_items;
                    }));
                }
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
            com_port.SelectedIndex = SerialPort.GetPortNames().Length - 1;
            version_info.ItemsSource = verson_info_db;
            com_data.ItemsSource = _com_args;
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location);
            Title = fileVersion.FileVersion;
            new Thread(serial_received).Start();
            new Thread(callback).Start();
        }
        public class ActionItem
        {
            public int Val { get; set; }
            public string Ena { get; set; }
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
            byte[] ds = Encoding.Default.GetBytes("~cfg.");
            ds[ds.Length - 1] =(byte)( add8(ds, ds.Length - 1)%128);
            send_bytes(ds);
        }
        private void get_version()
        {
            byte[] ds = Encoding.Default.GetBytes("~ver.");
            ds[ds.Length-1] = (byte)(add8(ds, ds.Length - 1) % 128);
            send_bytes(ds);
        }
        private void get_control()
        {
            byte[] ds = Encoding.Default.GetBytes("~act.");
            ds[ds.Length - 1] = (byte)(add8(ds, ds.Length - 1) % 128);
            send_bytes(ds);
        }
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (button1.Content.ToString() == "连接设备")
            {
                _sp.PortName = com_port.SelectedItem.ToString();
                _sp.BaudRate = int.Parse(com_baud.SelectedValue.ToString());
                _sp.Encoding = Encoding.UTF8;
                _sp.Open();
                button1.Content = "断开设备";
                Task.Run(async () => {
                    await Task.Delay(100);
                    get_version();
                });
            }
            else
            {
                _sp.Close();
                button1.Content = "连接设备";
            }
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            foreach(ArgItem i in _args)
            {
                if (args.ContainsKey(i.Key))
                {
                    args[i.Key] = i.Value;
                }
            }
            string s = JsonConvert.SerializeObject(args);
            string tmp = string.Format("~s{0}.",s);
            byte[] ds = Encoding.ASCII.GetBytes(tmp);
            ds[ds.Length - 1] = (byte)(add8(ds, ds.Length - 1) % 128);
            send_bytes(ds);
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            get_control();
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {

        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;
            ActionItem obj = (ActionItem)btn.DataContext;
            string s = string.Format("~r{0}={1}.", obj.Ena, obj.Val);
            byte[] ds = Encoding.Default.GetBytes(s);
            ds[ds.Length - 1] = (byte)(add8(ds, ds.Length - 1) % 128);
            send_bytes(ds);
        }

        private void Button_Click_6(object sender, RoutedEventArgs e)
        {

        }

        private void Button_Click_7(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            ActionItem obj = (ActionItem)cb.DataContext;
            string s = string.Format("~r{0}={1}.", obj.Ena,cb.IsChecked==true?1:0);
            byte[] ds = Encoding.Default.GetBytes(s);
            ds[ds.Length - 1] = (byte)(add8(ds, ds.Length - 1) % 128);
            send_bytes(ds);
        }
        private void update_history()
        {
            com_data.ItemsSource = null;
            _com_args.Clear();
            foreach (RawData r in lrd)
            {
                string s = $"[{r.tim.ToLongTimeString()}.{r.tim.Millisecond:000}]";
                if (history_hex.IsChecked == true)
                {
                    foreach (byte b in r.data)
                    {
                        s += $"{b:X2} ";
                    }
                }
                else
                {
                    s += Encoding.ASCII.GetString(r.data);
                }
                _com_args.Add(s);
            }
            com_data.ItemsSource = _com_args;
            com_data.SelectedIndex = com_data.Items.Count - 1;
            com_data.ScrollIntoView(com_data.SelectedItem);
        }
        private void Button_Click_8(object sender, RoutedEventArgs e)
        {
            com_data.ItemsSource = null;
            lrd.Clear();
            _com_args.Clear();
            com_data.ItemsSource = _com_args;
        }

        private void history_hex_Click(object sender, RoutedEventArgs e)
        {
            update_history();
        }

        private void Button_Click_9(object sender, RoutedEventArgs e)
        {
            string s = JsonConvert.SerializeObject(args);
            File.WriteAllText("config.json", s);
        }

        private void Button_Click_10(object sender, RoutedEventArgs e)
        {
            string s = File.ReadAllText("config.json");
            try
            {
                args = JsonConvert.DeserializeObject<Dictionary<string, int>>(s);
                config_grid.ItemsSource = null;
                _args.Clear();
                foreach (var item in args)
                {
                    ArgItem item2 = new ArgItem();
                    item2.Key = item.Key;
                    item2.Value = item.Value;
                    _args.Add(item2);
                }
                config_grid.ItemsSource = _args;
            }
            catch (Exception aa)
            {
                MessageBox.Show(aa.Message);
            }
        }
    }
}
