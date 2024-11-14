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
using System.Windows.Shapes;

namespace XenaxControllerGUI
{
    /// <summary>
    /// Interaction logic for Crosshair.xaml
    /// </summary>
    public partial class Crosshair : Window
    {
        public XenaxStageGUIControlViewModel handle;

        public Crosshair()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) { this.DragMove(); }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                handle.StageCycleClick = false;
                this.Hide();
            }
        }
    }
}
