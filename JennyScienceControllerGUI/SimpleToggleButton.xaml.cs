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

namespace JennyScienceControllerGUI
{
    /// <summary>
    /// Interaction logic for SimpleToggleButton.xaml
    /// </summary>
    public partial class SimpleToggleButton : UserControl
    {
        Thickness leftSidePosition = new Thickness(-39, 0, 0, 0);
        Thickness rightSidePosition = new Thickness(0, 0, -39, 0);
        SolidColorBrush backColorOFF = new SolidColorBrush(Color.FromRgb(160, 160, 160));
        //SolidColorBrush backColorON = new SolidColorBrush(Color.FromRgb(130, 190, 125));
        SolidColorBrush backColorON = new SolidColorBrush(Color.FromRgb(111, 204, 13));

        private bool toggled = false;

        public SimpleToggleButton()
        {
            InitializeComponent();
            toggled = false;
            updateAppearance();
        }

        

        private void Dot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            toggled = !toggled;
            updateAppearance();
        }

        //public bool Toggled { get => toggled; set => toggled = value; }
        public bool Toggled
        {
            get
            {
                return toggled;
            }
            set
            {
                if (value != toggled)
                {
                    toggled = value;
                    updateAppearance();
                }
            }
        }

        private void updateAppearance()
        {
            buttonBackgroud.Fill = (toggled) ? backColorON : backColorOFF;
            buttonDot.Margin = (toggled) ? rightSidePosition : leftSidePosition;
            buttonDotBorder.Margin = (toggled) ? rightSidePosition : leftSidePosition;
            backgroundLabel.HorizontalAlignment = (toggled) ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            backgroundLabel.Content= (toggled) ? "ON" : "OFF";
        }

        private void UserControl_MouseUp(object sender, MouseButtonEventArgs e)
        {

        }
    }
}
