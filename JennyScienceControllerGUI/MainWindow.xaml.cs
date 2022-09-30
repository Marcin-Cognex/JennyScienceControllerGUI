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
using System.Text.RegularExpressions;
using System.Net;
using System.Globalization;
using JennyScienceControllerGUI.Resources;
using XenaxModel;
using Microsoft.Win32;
using System.Xml.Serialization;
using System.IO;
using System.Windows.Threading;
using System.Diagnostics;

namespace JennyScienceControllerGUI
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	///

	public partial class MainWindow : Window
	{
		Xenax xenax1 = new Xenax();
		XenaxStageGUIControlVM handle;
		DispatcherTimer DebounceTimer = new DispatcherTimer();
		Crosshair crosshairWindow = new Crosshair();
		DispatcherTimer currentPositionTimer = new System.Windows.Threading.DispatcherTimer();

		public MainWindow()
		{
			InitializeComponent();

			//this.Title += string.Format(" v{0}.{1}.{2}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Major, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Minor, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Build);

			if (System.Deployment.Application.ApplicationDeployment.IsNetworkDeployed)
			{
				this.Title += "ver " +
					System.Deployment.Application.ApplicationDeployment.CurrentDeployment.CurrentVersion.Major.ToString() + "." +
					System.Deployment.Application.ApplicationDeployment.CurrentDeployment.CurrentVersion.Revision.ToString();
			}

			handle = this.DataContext as XenaxStageGUIControlVM;
			DebounceTimer.Interval = TimeSpan.FromMilliseconds(50);
			crosshairWindow.handle = handle;

			handle.xenaxDeviceConnectionsVM = xenax1.xenaxDeviceConnections;
			this.dgConnections.DataContext = xenax1.xenaxDeviceConnections;

			//Add additional handler to sliders
			object wantedNode = this.FindName("slManualRotation");
			if (wantedNode is Slider)
			{
				// Following executed if Text element was found.
				Slider wantedChild = wantedNode as Slider;

				wantedChild.AddHandler(PreviewMouseUpEvent,
						new RoutedEventHandler(SlManualRotation_MouseUp),
						true);

				wantedChild.AddHandler(PreviewMouseDownEvent,
						new RoutedEventHandler(SlManualRotation_MouseDown),
						true);

				wantedChild.AddHandler(MouseLeaveEvent,
						new RoutedEventHandler(SlManualRotation_MouseLeave),
						true);

				wantedChild.AddHandler(Slider.ValueChangedEvent,
						new RoutedEventHandler(SlManualRotation_ValueChanged),
						true);

				wantedChild.AddHandler(Slider.DragLeaveEvent,
						new DragEventHandler(SlManualRotation_DragLeave),
						true);
			}

			object wantedNode2 = this.FindName("slPositionLin");
			if (wantedNode is Slider)
			{
				// Following executed if Text element was found.
				Slider wantedChild = wantedNode2 as Slider;

				wantedChild.AddHandler(PreviewMouseDownEvent,
						new RoutedEventHandler(SlPositionLin_MouseDown),
						true);
			}

			xenax1.ConnectionUpdate += Xenax_ConnectionUpdate;
			xenax1.MotorStatusUpdate += Xenax1_MotorStatusUpdate;
			xenax1.PositionVelocityUpdated += Xenax_PositionSpeedAccUpdated;
			xenax1.PositionReached += Xenax_PositionReached;

			xenax1.DataReceived += Xenax1_DataReceived;
			xenax1.DataSent += Xenax1_DataSent;

			currentPositionTimer.Tick += CurrentPositionTimer_Tick;
			currentPositionTimer.Interval = TimeSpan.FromMilliseconds(250);
		}

		private void CurrentPositionTimer_Tick(object sender, EventArgs e)
		{
			xenax1.MotorGetPosition();
		}

		#region Mouse move/click DLL imports
		//This is a replacement for Cursor.Position in WinForms
		[System.Runtime.InteropServices.DllImport("user32.dll")]
		static extern bool SetCursorPos(int x, int y);

		/// <summary>
		/// Struct representing a point.
		/// </summary>
		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		public struct POINT
		{
			public int X;
			public int Y;

			public static implicit operator Point(POINT point)
			{
				return new Point(point.X, point.Y);
			}
		}

		/// <summary>
		/// Retrieves the cursor's position, in screen coordinates.
		/// </summary>
		/// <see>See MSDN documentation for further information.</see>
		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern bool GetCursorPos(out POINT lpPoint);

		public static void GetCursorPosition(out int xpos, out int ypos)
		{
			POINT lpPoint;
			GetCursorPos(out lpPoint);
			xpos = lpPoint.X;
			ypos = lpPoint.Y;
		}

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

		public const int MOUSEEVENTF_LEFTDOWN = 0x02;
		public const int MOUSEEVENTF_LEFTUP = 0x04;

		//This simulates a left mouse click
		public static void LeftMouseClick(int xpos, int ypos)
		{
			SetCursorPos(xpos, ypos);
			mouse_event(MOUSEEVENTF_LEFTDOWN, xpos, ypos, 0, 0);
			mouse_event(MOUSEEVENTF_LEFTUP, xpos, ypos, 0, 0);
		}

		[System.Runtime.InteropServices.DllImport("User32.dll")]
		static extern IntPtr GetDC(IntPtr hwnd);

		[System.Runtime.InteropServices.DllImport("gdi32.dll")]
		static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

		double screenUnitSize = 1;
		private void getSystemScreenUnitSize()
		{
			// get the system DPI
			IntPtr dDC = GetDC(IntPtr.Zero); // Get desktop DC
			int screenDPI = GetDeviceCaps(dDC, 88);
			bool rv = ReleaseDC(IntPtr.Zero, dDC);

			// WPF's physical unit size is calculated by taking the 
			// "Device-Independant Unit Size" (always 1/96)
			// and scaling it by the system DPI
			screenUnitSize = (1d / 96d) * (double)screenDPI;
		}

		private Point convertPixelsToUnits(int x, int y)
		{
			Point wpfUnits = new Point((double)x / screenUnitSize, (double)y / screenUnitSize);
			return wpfUnits;
		}

		private Point convertUnitsToPixels(double x, double y)
		{
			Point pixels = new Point(x * screenUnitSize, y * screenUnitSize);
			return pixels;
		}
		#endregion

		private void MainWindow1_Loaded(object sender, RoutedEventArgs e)
		{
			getSystemScreenUnitSize();



			//Load connections
			Properties.Settings.Default.Reload();
			if (Properties.Settings.Default.Connections != null)
			{
				foreach (string s in Properties.Settings.Default.Connections)
				{
					if (s.Length < 12) { continue; }
					xenax1.xenaxDeviceConnections.Add(new XenaxDeviceConnection(s));
				}
			}
			MainWindow1.Width = Properties.Settings.Default.MainWindowWidth;
			MainWindow1.Height = Properties.Settings.Default.MainWindowHeight;
			btnTopMost.Toggled = Properties.Settings.Default.MainWindowTopMost;
			crosshairWindow.Left = Properties.Settings.Default.crosshairWindowLeft;
			crosshairWindow.Top = Properties.Settings.Default.crosshairWindowTop;
			//rest of settings loaded on connection
		}

		private void Xenax_PositionSpeedAccUpdated(object sender, EventArgs e)
		{
			Dispatcher.Invoke(() =>
			{
				handle.StagePositionCurrent = (int)xenax1.MotorPosition;
				handle.StageSpeed = (ulong)xenax1.MotorSpeed;
				handle.StageAcc = (ulong)xenax1.MotorAcceleration;
			}
			);
		}

		private void Xenax_StopMotion()
		{
			handle.StageCycleRunOnce = false;
			handle.StageCycle = false;
			xenax1.MotorStopMotion();
		}

		#region ConnectionHistoryTabEvents
		private void BtnConnectConnection_Click(object sender, RoutedEventArgs e)
		{
			if (this.dgConnections.SelectedIndex < 0) { return; }

			XenaxDeviceConnection conn = handle.xenaxDeviceConnectionsVM.ElementAt(this.dgConnections.SelectedIndex);

			if (conn.stageConnectionStatus == ConnectionStatusEn.Available)
			{
				xenax1.StopClient();
				xenax1.StartClient(conn);
			}
		}

		private void BtnDisconnectConnection_Click(object sender, RoutedEventArgs e)
		{
			if (xenax1.connection != null && xenax1.connection.stageConnectionStatus == ConnectionStatusEn.Connected)
			{
				bool isLinear = (xenax1.connection.stageType == StageTypeEn.Linear);

				//save settings
				if (isLinear)
				{
					Properties.Settings.Default.LinearPosition1 = handle.StagePosition1;
					Properties.Settings.Default.LinearPosition2 = handle.StagePosition2;
					Properties.Settings.Default.LinearStageSpeedP2P1 = handle.StageSpeedP2P1;
					Properties.Settings.Default.LinearStageSpeedP1P2 = handle.StageSpeedP1P2;
				}
				else
				{
					Properties.Settings.Default.RotationPosition1 = handle.StagePosition1;
					Properties.Settings.Default.RotationPosition2 = handle.StagePosition2;
					Properties.Settings.Default.RotationStageSpeedP2P1 = handle.StageSpeedP2P1;
					Properties.Settings.Default.RotationStageSpeedP1P2 = handle.StageSpeedP1P2;
				}
			}

			xenax1.StopClient();
		}

		private void Xenax_ConnectionUpdate(object sender, EventArgs e)
		{
			Dispatcher.Invoke(() =>
			{
				ItemCollection tabs = (ItemCollection)this.TabControlAll.Items;
				TabItem tiConnection = (TabItem)tabs.GetItemAt(0);
				TabItem tiLinearMovement = (TabItem)tabs.GetItemAt(1);
				TabItem tiRotationMovement = (TabItem)tabs.GetItemAt(2);

				if (xenax1.connection.stageConnectionStatus == ConnectionStatusEn.Connected)
				{
					handle.StageConnected = true;

					handle.CurrentStageType = (int)xenax1.connection.stageType;
					bool isLinear = (xenax1.connection.stageType == StageTypeEn.Linear);
					tiLinearMovement.IsEnabled = isLinear;
					tiRotationMovement.IsEnabled = !isLinear;

					//load settings
					if (isLinear)
					{
						handle.StagePosition1 = Properties.Settings.Default.LinearPosition1;
						handle.StagePosition2 = Properties.Settings.Default.LinearPosition2;
						handle.StageSpeedP2P1 = Properties.Settings.Default.LinearStageSpeedP2P1;
						handle.StageSpeedP1P2 = Properties.Settings.Default.LinearStageSpeedP1P2;
					}
					else
					{
						handle.StagePosition1 = Properties.Settings.Default.RotationPosition1;
						handle.StagePosition2 = Properties.Settings.Default.RotationPosition2;
						handle.StageSpeedP2P1 = Properties.Settings.Default.RotationStageSpeedP2P1;
						handle.StageSpeedP1P2 = Properties.Settings.Default.RotationStageSpeedP1P2;
					}

					currentPositionTimer.Start();

					this.TabControlAll.SelectedItem = isLinear ? tiLinearMovement : tiRotationMovement;
				}
				else
				{
					handle.StageConnected = false;

					tiLinearMovement.IsEnabled = tiRotationMovement.IsEnabled = false;
					this.TabControlAll.SelectedItem = tiConnection;
				}
			}
			);
		}
		private void Xenax1_MotorStatusUpdate(object sender, EventArgs e)
		{
			switch (xenax1.MotorStatus)
			{
				case MotorStatusEn.PowerOFF:
					handle.StageMotorStatus = "Power OFF";
					break;
				case MotorStatusEn.PowerON:
					handle.StageMotorStatus = "Power ON";
					break;
				case MotorStatusEn.InMotion:
					handle.StageMotorStatus = "Moving...";
					break;
				case MotorStatusEn.Error:
					handle.StageMotorStatus = "Error";
					break;
			}
		}

		private void Add_Click(object sender, RoutedEventArgs e)
		{
			string sn = "Default";
			string sip = "127.0.0.1";
			StageTypeEn st = StageTypeEn.Linear;
			IPAddress ip;

			if (this.StageName.Text.Length != 0) { sn = this.StageName.Text; }
			if (IPAddress.TryParse(this.StageIP.Text, out ip)) { sip = this.StageIP.Text; }
			if (this.StageType.SelectedIndex != -1) { st = (StageTypeEn)this.StageType.SelectedIndex; }

			handle.AddNewConncetion(sn, st, sip);
		}

		private void Remove_Click(object sender, RoutedEventArgs e)
		{
			if (this.dgConnections.SelectedIndex >= 0)
			{
				handle.RemoveConncetion(this.dgConnections.SelectedIndex);
			}
		}

		private void StageIP_KeyUp(object sender, KeyEventArgs e)
		{
			TextBox txtBox = (TextBox)sender;
			int dotsNumber = txtBox.Text.Count(x => x == '.');
			IPAddress ip;

			if ((dotsNumber == 3) && IPAddress.TryParse(txtBox.Text, out ip))
			{
				txtBox.Background = Brushes.White;
			}
			else
			{
				txtBox.Background = Brushes.Salmon;
			}
		}

		private void dgConnections_MouseDoubleClick(object sender, MouseButtonEventArgs e)
		{
			if (dgConnections.SelectedIndex >= 0)
			{
				BtnConnectConnection_Click(sender, new RoutedEventArgs());
			}
			e.Handled = true;
		}

		private void txtCommandHistory_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			//perform scroll to bottom
			//auto scroll is not done when control is not visible
			//TODO: this not working for the first time is shown
			if (txtCommandHistory.IsVisible)
			{
				txtCommandHistory.ScrollToEnd();
			}
		}


		private void Xenax1_DataSent(object sender, xenaxCommunicationEventArgs e)
		{
			Dispatcher.Invoke(() =>
			{
				//TextRange tr = new TextRange(txtCommandHistory.Document.ContentEnd, txtCommandHistory.Document.ContentEnd);
				//tr.Text = "\r\n--> " + e.Data;
				//tr.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Red);

				txtCommandHistory.AppendText("\r\n--> " + e.Data);
			}
			);
		}

		private void Xenax1_DataReceived(object sender, xenaxCommunicationEventArgs e)
		{
			Dispatcher.Invoke(() =>
			{
				txtCommandHistory.AppendText("\r\n" + e.Data);
			}
			);
		}
		#endregion

		#region SpeedAccEvents
		private bool dragStarted = false;

		//Set speed with slider
		private void SlSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			if (!DebounceTimer.IsEnabled)
			{
				EventHandler MyDelegate = null;
				MyDelegate = (senderi, ei) => { xenax1.MotorSetSpeed((uint)((((Slider)sender).Value / 100) * XenaxStageGUIControlVM.MaxSpeed)); DebounceTimer.Stop(); DebounceTimer.Tick -= MyDelegate; };
				DebounceTimer.Tick += MyDelegate;
			}
			DebounceTimer.Start();

		}

		//Set acc with slider
		private void SlAcc_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			if (!DebounceTimer.IsEnabled)
			{
				EventHandler MyDelegate = null;
				MyDelegate = (senderi, ei) =>
				{
					xenax1.MotorSetAcceleration((uint)((((Slider)sender).Value / 100) * XenaxStageGUIControlVM.MaxAcc)); DebounceTimer.Stop(); DebounceTimer.Tick -= MyDelegate;
				};
				DebounceTimer.Tick += MyDelegate;
			}
			DebounceTimer.Start();
		}

		private void Tb_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				BindingExpression be = ((TextBox)sender).GetBindingExpression(TextBox.TextProperty);
				be.UpdateSource();
				((TextBox)sender).Background = Brushes.White;

				if (ulong.TryParse(((TextBox)sender).Text, out ulong x))
				{
					switch (((TextBox)sender).Name)
					{
						case "tbSpeedLin":
							xenax1.MotorSetSpeed(x);
							break;
						case "tbSpeedRot":
							xenax1.MotorSetSpeed(x);
							break;
						case "tbAccLin":
							xenax1.MotorSetAcceleration(x);
							break;
						case "tbAccRot":
							xenax1.MotorSetAcceleration(x);
							break;
						default:
							break;
					}
				}
			}
			else
			{
				((TextBox)sender).Background = Brushes.Salmon;
			}

		}
		#endregion

		#region SliderPositionLinear
		//Set position with slider - click
		private void SlPositionLin_ValueChanged(object sender, RoutedEventArgs e)
		{
			if (!dragStarted)
			{
				if (!DebounceTimer.IsEnabled)
				{
					EventHandler MyDelegate = null;
					MyDelegate = (senderi, ei) =>
					{
						xenax1.MotorGoToPositionAbsolute((long)(((((Slider)sender).Value) / 100) * XenaxStageGUIControlVM.MaxPositionLinear));
						DebounceTimer.Stop();
						DebounceTimer.Tick -= MyDelegate;
					};
					DebounceTimer.Tick += MyDelegate;
				}
				DebounceTimer.Start();
			}
		}

		private void SlPositionLin_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
		{
			Xenax_StopMotion();

			dragStarted = false;
			if (!DebounceTimer.IsEnabled)
			{
				EventHandler MyDelegate = null;
				MyDelegate = (senderi, ei) =>
				{
					xenax1.MotorGoToPositionAbsolute((long)(((((Slider)sender).Value) / 100) * XenaxStageGUIControlVM.MaxPositionLinear));
					DebounceTimer.Stop();
					DebounceTimer.Tick -= MyDelegate;
				};
				DebounceTimer.Tick += MyDelegate;
			}
			DebounceTimer.Start();
		}

		private void SlPositionLin_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
		{
			Xenax_StopMotion();
			dragStarted = true;
		}

		public void SlPositionLin_MouseDown(object sender, RoutedEventArgs e)
		{
			Xenax_StopMotion();
		}

		private void BtnLinStop_Click(object sender, RoutedEventArgs e)
		{
			Xenax_StopMotion();
		}

		private void ReadPosition(object sender, EventArgs e)
		{
			Dispatcher.Invoke(() =>
			{
				this.slPositionLin.Value = ((double)xenax1.MotorPosition / (double)XenaxStageGUIControlVM.MaxPositionLinear) * 100;
				xenax1.PositionVelocityUpdated -= ReadPosition;
			}
			);
		}
		#endregion

		#region SliderManualRotation
		//Go infinitely in direction - click, rotation drum
		private void SlManualRotation_ValueChanged(object sender, RoutedEventArgs e)
		{
			handle.StageCycle = false;

			if (((Slider)sender).Value != 0)
			{
				if (!DebounceTimer.IsEnabled)
				{
					EventHandler MyDelegate = null;
					MyDelegate = (senderi, ei) =>
					{
						long speed = (long)((((Slider)sender).Value / 100) * XenaxStageGUIControlVM.MaxSpeed);
						xenax1.MotorSetSpeed((ulong)(speed >= 0 ? speed : speed * (-1)));
						if (speed >= 0)
							xenax1.MotorGoInfiniteClockwise();
						else
							xenax1.MotorGoInfiniteCounterClockwise();
						DebounceTimer.Stop();
						DebounceTimer.Tick -= MyDelegate;
					};
					DebounceTimer.Tick += MyDelegate;
				}
				DebounceTimer.Start();
			}
			else
			{
				xenax1.MotorStopMotion();
				((Slider)sender).Value = 0;
			}

		}

		private void SlManualRotation_MouseUp(object sender, RoutedEventArgs e)
		{
			//MouseButtonEventArgs args = e as MouseButtonEventArgs;
			xenax1.MotorStopMotion();
			((Slider)sender).Value = 0;
		}

		private void SlManualRotation_MouseDown(object sender, RoutedEventArgs e)
		{
			this.dragStarted = true;
		}

		private void SlManualRotation_MouseLeave(object sender, RoutedEventArgs e)
		{
			//MouseButtonEventArgs args = e as MouseButtonEventArgs;
			if (this.dragStarted)
			{
				xenax1.MotorStopMotion();
				((Slider)sender).Value = 0;
				this.dragStarted = false;
			}
		}

		private void SlManualRotation_DragLeave(object sender, DragEventArgs e)
		{
			if (this.dragStarted)
			{
				xenax1.MotorStopMotion();
				this.dragStarted = false;
				((Slider)sender).Value = 0;
			}
		}
		#endregion

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			crosshairWindow.Close();

			//Save settings
			System.Collections.Specialized.StringCollection conStrings = new System.Collections.Specialized.StringCollection();
			foreach (XenaxDeviceConnection c in xenax1.xenaxDeviceConnections)
			{
				conStrings.Add(c.ToString());
			}

			Properties.Settings.Default.Connections = conStrings;
			if (MainWindow1.WindowState == WindowState.Normal)
			{
				Properties.Settings.Default.MainWindowWidth = MainWindow1.Width;
				Properties.Settings.Default.MainWindowHeight = MainWindow1.Height;
			}
			Properties.Settings.Default.MainWindowTopMost = MainWindow1.Topmost;

			//save only within primary screen to avoid multi-screen issues
			Properties.Settings.Default.crosshairWindowLeft = (crosshairWindow.Left > SystemParameters.PrimaryScreenWidth) ? SystemParameters.PrimaryScreenWidth : crosshairWindow.Left;
			Properties.Settings.Default.crosshairWindowTop = (crosshairWindow.Top > SystemParameters.PrimaryScreenHeight) ? SystemParameters.PrimaryScreenHeight : crosshairWindow.Top;

			//disconnect and save settings there
			BtnDisconnectConnection_Click(sender, new RoutedEventArgs());

			Properties.Settings.Default.Save();
		}

		private void BtnPowerOn_Click(object sender, RoutedEventArgs e)
		{
			xenax1.MotorON();
			handle.StagePowerOn = true;
			xenax1.PositionVelocityUpdated += ReadPosition;
			xenax1.MotorGetPosition();
		}

		private void BtnPowerQuit_Click(object sender, RoutedEventArgs e)
		{
			xenax1.MotorOFF();
			handle.StagePowerOn = false;
		}

		private void BtnHome_Click(object sender, RoutedEventArgs e)
		{
			handle.StageCycle = false;
			xenax1.MotorHoming();
			handle.StagePowerOn = true;
			xenax1.PositionVelocityUpdated += ReadPosition;
			xenax1.MotorGetPosition();
		}

		private void BtnCounterClockwise_Click(object sender, RoutedEventArgs e)
		{
			handle.StageCycle = false;
			xenax1.MotorGoInfiniteCounterClockwise();
		}

		private void BtnClockwise_Click(object sender, RoutedEventArgs e)
		{
			handle.StageCycle = false;
			xenax1.MotorGoInfiniteClockwise();
		}

		private void BtnStopInfinite_Click(object sender, RoutedEventArgs e)
		{
			Xenax_StopMotion();

			xenax1.PositionVelocityUpdated += ReadPosition;
			xenax1.MotorGetPosition();
		}

		private void BtnReadPosition1_Click(object sender, RoutedEventArgs e)
		{
			xenax1.PositionVelocityUpdated += ReadPosition1;
			xenax1.MotorGetPosition();
		}

		private void ReadPosition1(object sender, EventArgs e)
		{
			Dispatcher.Invoke(() =>
			{
				handle.StagePosition1 = xenax1.MotorPosition;
				xenax1.PositionVelocityUpdated -= ReadPosition1;
			}
			);
		}

		private void BtnReadPosition2_Click(object sender, RoutedEventArgs e)
		{
			xenax1.PositionVelocityUpdated += ReadPosition2;
			xenax1.MotorGetPosition();

		}

		private void ReadPosition2(object sender, EventArgs e)
		{
			Dispatcher.Invoke(() =>
			{
				handle.StagePosition2 = xenax1.MotorPosition;
				xenax1.PositionVelocityUpdated -= ReadPosition2;
			}
			);
		}

		private void BtnGoPosition1_Click(object sender, RoutedEventArgs e)
		{
			handle.StageCycleReturning = false;
			xenax1.MotorSetSpeed(handle.StageSpeedP2P1);
			xenax1.MotorGoToPositionAbsolute(handle.StagePosition1);
		}

		private void BtnGoPosition2_Click(object sender, RoutedEventArgs e)
		{
			handle.StageCycleReturning = false;
			xenax1.MotorSetSpeed(handle.StageSpeedP1P2);
			xenax1.MotorGoToPositionAbsolute(handle.StagePosition2);
		}

		private void Xenax_PositionReached(object sender, EventArgs e)
		{
			//this gets fired multiple times
			//when motor is turned ON
			//when position is reached

			Dispatcher.Invoke(() =>
			{
				xenax1.PositionVelocityUpdated += ReadPosition;
				xenax1.MotorGetPosition();

				Trace.WriteLine("Xenax_PositionReached------------- pos=" + handle.StagePositionCurrent);

				if (handle.StageCycleRunOnce)
				{
					Trace.WriteLine("handle.StageCycleRunOnce");

					if (handle.StageCycleReturning == false)
					{
						GoFromP1toP2();
                        if (handle.StageCycleClick) { performClick(); }
                    }
					else
					{
						ReturnFromP2toP1();

						handle.StageCycleRunOnce = false;
					}
				}
				else if (handle.StageCycle)
				{
					Trace.WriteLine("handle.StageCycle");

					if (handle.StageCycleReturning == false)
					{
						GoFromP1toP2();
                        if (handle.StageCycleClick) { performClick(); }
                    }
					else
					{
						ReturnFromP2toP1();
					}
				}
			}
			);
		}

		private void GoFromP1toP2()
		{
			// Going from P1 to P2
			Trace.WriteLine("Going from P1 to P2");
			handle.StageCycleReturning = true;
			xenax1.MotorSetSpeed(handle.StageSpeedP1P2);
			xenax1.MotorGoToPositionAbsolute(handle.StagePosition2);
            System.Threading.Thread.Sleep((int)handle.StageCycleClickDelayStart);
        }

		private void ReturnFromP2toP1()
		{
			//returning from P2 to P1
			Trace.WriteLine("returning from P2 to P1");
			handle.StageCycleReturning = false;
			System.Threading.Thread.Sleep((int)handle.StageCycleClickDelayEnd);
			xenax1.MotorSetSpeed(handle.StageSpeedP2P1);
			xenax1.MotorGoToPositionAbsolute(handle.StagePosition1);
		}

		private void performClick()
		{
			//save current mouse position
			int init_x, init_y;
			GetCursorPosition(out init_x, out init_y);

			//target location
			Point p = convertUnitsToPixels(crosshairWindow.Left + crosshairWindow.Width / 2.0, crosshairWindow.Top + crosshairWindow.Height / 2.0);

			crosshairWindow.Hide();
			System.Threading.Thread.Sleep(100);
			LeftMouseClick(Convert.ToInt32(p.X), Convert.ToInt32(p.Y));
			System.Threading.Thread.Sleep(100);
			crosshairWindow.Show();

			SetCursorPos(init_x, init_y);
		}

		private void BtnStopTransition_Click(object sender, RoutedEventArgs e)
		{
			Xenax_StopMotion();
		}

		private void BtnJogLeft_Click(object sender, RoutedEventArgs e)
		{
			handle.StageCycle = false;
			xenax1.MotorGoToPositionAbsolute(272000);
		}

		private void BtnJogRight_Click(object sender, RoutedEventArgs e)
		{
			handle.StageCycle = false;
			xenax1.MotorGoToPositionAbsolute(0);
		}

		private void BtnClearPosition_Click(object sender, RoutedEventArgs e)
		{
			xenax1.MotorClearPosition();
		}

		private void BtnImport_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				OpenFileDialog openFileDialog = new OpenFileDialog();
				openFileDialog.Multiselect = true;
				openFileDialog.Filter = "Text files (*.xml)|*.txt|All files (*.*)|*.*";
				openFileDialog.Multiselect = true;
				if (openFileDialog.ShowDialog() == true)
				{
					XmlSerializer serializer = new XmlSerializer(typeof(XenaxStageGUIControlVM));
					// A FileStream is needed to read the XML document.
					FileStream fs = new FileStream(openFileDialog.FileName, FileMode.Open);

					/* Use the Deserialize method to restore the object's state with
					data from the XML document. */
					handle = (XenaxStageGUIControlVM)serializer.Deserialize(fs);
				}
			}
			catch
			{ }
		}

		private void BtnExport_Click(object sender, RoutedEventArgs e)
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog();
			saveFileDialog.Filter = "Text files (*.xml)|*.txt|All files (*.*)|*.*";
			saveFileDialog.DefaultExt = "xml";
			saveFileDialog.AddExtension = true;

			if (saveFileDialog.ShowDialog() == true)
			{
				System.IO.FileStream fs = (System.IO.FileStream)saveFileDialog.OpenFile();
				XmlSerializer serializer = new XmlSerializer(typeof(XenaxStageGUIExportable));
				XenaxStageGUIExportable ex = new XenaxStageGUIExportable(handle);
				TextWriter writer = new StreamWriter(fs);
				serializer.Serialize(writer, ex);
				fs.Close();
			}
		}

		private void BtnReadSpeed2_Click(object sender, RoutedEventArgs e)
		{
			handle.StageSpeedP1P2 = handle.StageSpeed;
		}

		private void BtnReadSpeed1_Click(object sender, RoutedEventArgs e)
		{
			handle.StageSpeedP2P1 = handle.StageSpeed;
		}

		private void UnitsMm_Checked(object sender, RoutedEventArgs e)
		{

		}

		private void UnitsInc_Checked(object sender, RoutedEventArgs e)
		{

		}

		private void SendButton_Click(object sender, RoutedEventArgs e)
		{
			xenax1.Send(txtCommand.Text.Trim() + "\r");
		}

		//private void ToggleButton_Click(object sender, RoutedEventArgs e)
		//{
		//	if (((System.Windows.Controls.Primitives.ToggleButton)sender).IsChecked == false) { MainWindow1.Topmost = false; }
		//	else { MainWindow1.Topmost = true; }
		//}

		private void ClickCheckBox_Click(object sender, RoutedEventArgs e)
		{
			if (((CheckBox)sender).IsChecked == true)
			{
				crosshairWindow.Show();
			}
			else
			{
				crosshairWindow.Hide();
			}
		}

		private void cbCycle_Checked(object sender, RoutedEventArgs e)
		{
			BtnGoPosition1_Click(sender, e);
		}

		private void btnTopMost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			if (btnTopMost.Toggled == false) { MainWindow1.Topmost = false; }
			else { MainWindow1.Topmost = true; }
		}

		private void btnCycleRunOnce_Click(object sender, RoutedEventArgs e)
		{
			handle.StageCycleRunOnce = true;
			BtnGoPosition1_Click(sender, e);
		}

		private void MainWindow1_StateChanged(object sender, EventArgs e)
		{
			if (handle.StageCycleClick)
			{
				if (MainWindow1.WindowState == WindowState.Minimized)
				{
					crosshairWindow.Hide();
				}
				else
				{
					crosshairWindow.Show();
				}
			}
		}
	}



	#region ValueConverters
	public class IsConnectedConverter : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			Int32 result;
			Int32.TryParse(parameter.ToString(), out result);
			if (result == 1)
			{
				if ((bool)value)
					return "Stage Connected";
				else
					return "Stage Disconnected";
			}
			else if (result == 2)
			{
				if ((bool)value)
					return "Stage 2 connected";
				else
					return "Stage 2 disconnected";
			}
			else
			{
				return "";
			}
		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (((string)value).Contains("disconnected"))
				return false;
			else
				return true;
		}
	}

	public class IsUseConverter : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if ((UInt16)value == 2)
				return true;
			else
				return false;

		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if ((bool)value)
				return 2;
			else
				return 1;
		}
	}

	public class SlSpeedConverter : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is UInt64)
				return ((UInt64)value / (double)XenaxStageGUIControlVM.MaxSpeed) * 100;
			return 0.0;

		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{

			return ((UInt64)(((double)value / 100) * XenaxStageGUIControlVM.MaxSpeed));
		}
	}

	public class SlPositionLinConverter : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is UInt64)
				return (((UInt64)value / (double)XenaxStageGUIControlVM.MaxPositionLinear) * 100);
			return 0.0;
		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return ((UInt64)(((double)value / 100) * XenaxStageGUIControlVM.MaxPositionLinear));
		}
	}

	public class TbSpeedConverter : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is UInt64)
				return value.ToString();
			return null;
		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			try
			{
				if (value is string)
					return UInt64.Parse((string)value);
				return 0;
			}
			catch
			{
				return 0;
			}
		}
	}


	public class SlAccConverter : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is UInt64)
				return ((UInt64)value / (double)XenaxStageGUIControlVM.MaxAcc) * 100;
			return 0.0;

		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{

			return ((UInt64)(((double)value / 100) * XenaxStageGUIControlVM.MaxAcc));
		}
	}

	public class TbAccConverter : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is UInt64)
				return value.ToString();
			return null;
		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			try
			{
				if (value is string)
					return UInt64.Parse((string)value);
				return 0;
			}
			catch
			{
				return 0;
			}
		}
	}

	public class TbPositionConverter : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is Int64)
				return value.ToString();
			return null;
		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			try
			{
				if (value is string)
					return Int64.Parse((string)value);
				return 0;
			}
			catch
			{
				return 0;
			}
		}
	}

	public class StageTypeConverter : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is StageTypeEn)
			{
				if ((StageTypeEn)value == StageTypeEn.Rotation)
					return "Rotation";
				else
					return "Linear";
			}
			return null;
		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			try
			{
				if (value is string)
				{
					if (String.Compare((string)value, "Rotation") == 0)
						return StageTypeEn.Rotation;
					else
						return StageTypeEn.Linear;
				}
				return 0;
			}
			catch
			{
				return 0;
			}
		}
	}

	public class ConnectStatusConverter : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if ((bool)value)
				return "Disconnect";
			else
				return "Connect";

		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{

			throw new NotImplementedException();

		}
	}

	public class StageConnectionSelected : IValueConverter
	{
		object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if ((bool)value)
				return "Disconnect";
			else
				return "Connect";

		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{

			throw new NotImplementedException();

		}
	}
	#endregion
}