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

namespace ungrain_tool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public class ConfigItem
        {
            public int Id { get; set; }
            public string Name { get; set; }   
            public string Value { get; set; }
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
            List<ConfigItem> lc = new List<ConfigItem>();
            lc.Add(new ConfigItem() { Id = 1, Name = "延时1", Value = "120" });
            lc.Add(new ConfigItem() { Id = 2, Name = "延时2", Value = "920" });
            config_grid.ItemsSource = lc;
        }
    }
}
