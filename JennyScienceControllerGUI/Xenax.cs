using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.ComponentModel;

namespace XenaxModel
{
	public enum StageTypeEn
	{
		Rotation,
		Linear
	}

	public enum ConnectionStatusEn
	{
		NotAvailable,
		Available,
		Connected
	}
	public enum MotorStatusEn
	{
		PowerOFF,
		PowerON,
		InMotion,
		Error
	}

	public class StateObject
	{
		// Client socket.  
		public Socket workSocket = null;
		// Size of receive buffer.  
		public const int BufferSize = 256;
		// Receive buffer.  
		public byte[] buffer = new byte[BufferSize];
		// Received data string.  
		public StringBuilder sb = new StringBuilder();
	}

	//Connection parameters class
	public class XenaxDeviceConnection : INotifyPropertyChanged
	{
		//Constructors
		public XenaxDeviceConnection() : this("DefaultName", StageTypeEn.Rotation, "0.0.0.0") { }

		public XenaxDeviceConnection(string Name, StageTypeEn StageType, string inputIPAddress)
		{
			_stageName = Name;
			_stageType = StageType;
			IPAddress.TryParse(inputIPAddress, out _stageIPAddress);
			_stageConnectionStatus = ConnectionStatusEn.NotAvailable;
		}

		public XenaxDeviceConnection(string text): this()
		{
			string[] lines = text.Split('|');

            if (lines.Length < 3) { return; }

			_stageName = lines[0];

			if (lines[1] == "Rotation") { _stageType = StageTypeEn.Rotation; }
			else if (lines[1] == "Linear") { _stageType = StageTypeEn.Linear; }

			IPAddress.TryParse(lines[2], out _stageIPAddress);
			_stageConnectionStatus = ConnectionStatusEn.NotAvailable;
		}

		//Connection IP adress
		private IPAddress _stageIPAddress;
		public IPAddress stageIPAddress
		{
			get
			{
				return _stageIPAddress;
			}

			set
			{
				if (value != _stageIPAddress)
				{
					_stageIPAddress = value;

					OnPropertyChanged("stageIPAddress");
				}
			}
		}

		//Type of stage
		private StageTypeEn _stageType;
		public StageTypeEn stageType
		{
			get { return _stageType; }
			set
			{
				if (value != _stageType)
				{
					_stageType = value;
					OnPropertyChanged("stageType");
				}
			}
		}

		//Stage status
		private ConnectionStatusEn _stageConnectionStatus;
		public ConnectionStatusEn stageConnectionStatus
		{
			get
			{
				return _stageConnectionStatus;
			}

			set
			{
				if (value != _stageConnectionStatus)
				{
					_stageConnectionStatus = value;

					OnPropertyChanged("stageConnectionStatus");
				}
			}
		}

		//User defined named
		private string _stageName;
		public string stageName
		{
			get { return _stageName; }
			set
			{
				if (value != _stageName)
				{
					_stageName = value;
					OnPropertyChanged("stageName");
				}
			}
		}

		//Pinged
		public Boolean pingPending = false;

		public event PropertyChangedEventHandler PropertyChanged;

		public void OnPropertyChanged(string propertyName)
		{
			PropertyChangedEventHandler handler = PropertyChanged;

			handler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		public override string ToString()
		{
			string s = _stageName;

			if (_stageType == StageTypeEn.Linear) { s += "|Linear"; }
			else if (_stageType == StageTypeEn.Rotation) { s += "|Rotation"; }
			s += "|" + _stageIPAddress.ToString();

			return s;
		}
	}

	//Main model Class
	public class Xenax
	{
		//port number for TCP/IP communication
		public const int port = 10001;
		//port number for UDP discovery communication
		public const int portDiscovery = 30718;
		//Connection list, recoverable after application restart
		public ObservableCollection<XenaxDeviceConnection> xenaxDeviceConnections = new ObservableCollection<XenaxDeviceConnection>();

		public XenaxDeviceConnection connection;
		
		private static Socket client;

		//Asynchronous client connection
		// ManualResetEvent instances signal completion.  
		private static ManualResetEvent connectDone = new ManualResetEvent(false);
		private static ManualResetEvent sendDone = new ManualResetEvent(false);
		private static ManualResetEvent receiveDone = new ManualResetEvent(false);

		// The response from the Xenax device.  
		private static String response = String.Empty;

		private long targetPosition = 0;

		public Xenax()
		{
			SetPingTimer();
		}

		//Start client connection
		public void StartClient(XenaxDeviceConnection xenaxDeviceConnection)
		{
			try
			{
				//Connection attempt timeout timer 2seconds
				connectionTimeoutTimer = new DispatcherTimer();
				connectionTimeoutTimer.Interval = new TimeSpan(0, 0, 5);
				connectionTimeoutTimer.Tick += new EventHandler(OnConnectionTimeoutTimerTick);
				connectionTimeoutTimer.Start();

				connection = xenaxDeviceConnection;

				//Establish the remote endpoint for the socket.  
				//IPEndPoint remoteEP = new IPEndPoint(xenaxDeviceConnection.stageIPAddress, port);
				IPEndPoint remoteEP = new IPEndPoint(connection.stageIPAddress, port);

				//Create a TCP/IP socket.  
				//client = new Socket(xenaxDeviceConnection.stageIPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
				client = new Socket(connection.stageIPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

				//Connect to the remote endpoint.  
				client.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), client);
			}
			catch (Exception e)
			{
				MessageBox.Show(e.Message, "Xenax controller - Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void ConnectCallback(IAsyncResult ar)
		{
			try
			{
				// Retrieve the socket from the state object.  
				Socket client = (Socket)ar.AsyncState;

				// Complete the connection.  
				client.EndConnect(ar);

				// Signal that the connection has been made.  
				connectDone.Set();

				//Stop connection timeout timer
				connectionTimeoutTimer.Stop();

				if (client.RemoteEndPoint is null)
				{
					//something went wrong
					StopClient();
					return;
				}

				connection.stageConnectionStatus = ConnectionStatusEn.Connected;
				OnConnectionStatusUpdate(EventArgs.Empty);
				Receive();

				//Update values
				MotorGetPosition();
				MotorGetSpeed();
				MotorGetAcceleration();
				MotorSetEvents();

			}
			catch (Exception e)
			{
				MessageBox.Show(e.Message, "Xenax controller - Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		//Stop client connection
		public void StopClient()
		{
			try
			{
				if (client is null) { return; }

				if (client.Connected)
				{
					//Power off the motor
					MotorOFF();

					//Release the socket. 
					client.Shutdown(SocketShutdown.Both);
					client.Close();
				}

				connection.stageConnectionStatus = ConnectionStatusEn.NotAvailable;
				OnConnectionStatusUpdate(EventArgs.Empty);
			}
			catch (Exception e)
			{
				MessageBox.Show(e.Message, "Xenax controller - Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void Receive()
		{
			try
			{
				// Create the state object.
				StateObject state = new StateObject();
				state.workSocket = client;

				// Begin receiving the data from the remote device.  
				client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
			}
			catch (Exception e)
			{
				connection.stageConnectionStatus = ConnectionStatusEn.NotAvailable;
				OnConnectionStatusUpdate(EventArgs.Empty);
			}
		}

		private void ReceiveCallback(IAsyncResult ar)
		{
			try
			{
				//not sure if this is correct
				//this functions is trowing error "cannot acces disposed object" on app exit and disconnect


				// Retrieve the state object and the client socket
				// from the asynchronous state object.  
				StateObject state = (StateObject)ar.AsyncState;
				Socket sclient = state.workSocket;

				if (sclient is null) { return; }
				if (!sclient.Connected) { return; }

				// Read data from the remote device.  
				int bytesRead = sclient.EndReceive(ar);

				if (bytesRead > 0)
				{
					// There might be more data, so store the data received so far.  
					state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
				}
				//Avoid firing BeginReceive, when all data arrived, got stuck													
				int bytesRemain = state.workSocket.Available;
				if (bytesRemain > 0)
				{
					// Get the rest of the data.  
					sclient.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
				}
				else
				{
					// All the data has arrived; put it in response.  
					if (state.sb.Length > 1)
					{
						response = state.sb.ToString();
					}
					state.sb.Clear();
					// Signal that all bytes have been received.  
					receiveDone.Set();
					ProcessResponse(response);

					sclient.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
				}
			}
			catch //(Exception e)
			{
				//TODO:
				//there is an exception trown on exit or disconnect
				//MessageBox.Show(e.Message, "Xenax controller - Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		/*
		 * 
		 * No. Do not do that. The best way to handle a socket is encapsulation. Do not expose it to anything but the class declaring it. By doing so it's easy to make sure that only one receive is pending at a time. No need to use a lock for it.

		As for the sending. do something like this:

		public class MyClient
		{
			private readonly Queue<byte[]> _sendBuffers = new Queue<byte[]>();
			private bool _sending;
			private Socket _socket;

			public void Send(string cmdName, object data)
			{
				lock (_sendBuffers)
				{
					_sendBuffers.Enqueue(serializedCommand);
					if (_sending) 
						return;

					_sending = true;
					ThreadPool.QueueUserWorkItem(SendFirstBuffer);
				}
			}

			private void SendFirstBuffer(object state)
			{
				while (true)
				{
					byte[] buffer;
					lock (_sendBuffers)
					{
						if (_sendBuffers.Count == 0)
						{
							_sending = false;
							return;
						}

						buffer = _sendBuffers.Dequeue();
					}

					_socket.Send(buffer);
				}
			}
		}
		This approach do not block any of the callers and all send requests are processed in turn.
		 * 
		 * 
		 */

		public void Send(String data)
		{
			if (client.Connected)
			{
				// Convert the string data to byte data using ASCII encoding.  
				byte[] byteData = Encoding.ASCII.GetBytes(data);

				// Begin sending the data to the remote device.  
				client.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), client);

				OnDataSent(new xenaxCommunicationEventArgs(data.Replace("\r", "")));
			}
		}

		private void SendCallback(IAsyncResult ar)
		{
			try
			{
				// Retrieve the socket from the state object.  
				Socket client = (Socket)ar.AsyncState;

				// Complete sending the data to the remote device.  
				int bytesSent = client.EndSend(ar);

				// Signal that all bytes have been sent.  
				sendDone.Set();
			}
			catch (Exception e)
			{
				MessageBox.Show(e.Message, "Xenax controller - Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void OnConnectionTimeoutTimerTick(object sender, EventArgs e)
		{
			StopClient();
			connectionTimeoutTimer.Stop();
		}

		//Connection asynchronous ping
		private async Task PingConnectionAsync(XenaxDeviceConnection xenaxDevice)
		{
			if (xenaxDevice.pingPending)
				return;
			else
			{
				xenaxDevice.pingPending = true;
				Ping ping = new Ping();
				var reply = await ping.SendPingAsync(xenaxDevice.stageIPAddress);
				xenaxDevice.pingPending = false;

				if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
				{
					if (xenaxDevice.stageConnectionStatus != ConnectionStatusEn.Connected)
						xenaxDevice.stageConnectionStatus = ConnectionStatusEn.Available;
				}
				else
				{
					if (xenaxDevice.stageConnectionStatus == ConnectionStatusEn.Connected)
						StopClient();

					xenaxDevice.stageConnectionStatus = ConnectionStatusEn.NotAvailable;
				}
			}
		}

		DispatcherTimer PingConnectionTimer;

		private void SetPingTimer()
		{
			// Create a timer with a two second interval.
			PingConnectionTimer = new DispatcherTimer();
			PingConnectionTimer.Interval = new TimeSpan(0, 0, 2);
			// Hook up the Elapsed event for the timer. 
			PingConnectionTimer.Tick += new EventHandler(OnTimedEvent);
			PingConnectionTimer.Start();		
		}

		private void OnTimedEvent(Object sender, EventArgs e)
		{
			foreach (XenaxDeviceConnection con in xenaxDeviceConnections)
			{
				if (!con.pingPending)
					PingList.Add(PingConnectionAsync(con));
			}
		}

		List<Task> PingList = new List<Task>();
	
		//Connection attemtp timeout
		DispatcherTimer connectionTimeoutTimer;


		public int stageType { get { return _stageType; } }
		private int _stageType { get; set; }

		public int MotorAcceleration { get { return _motorAcceleration; } }
		private int _motorAcceleration{ get; set; }
		public int MotorSpeed { get { return _motorSpeed; } }
		private int _motorSpeed { get; set; }
		public int MotorPosition { get { return _motorPosition; } }
		private int _motorPosition { get; set; }
		
		int motorOverride = 0;

		//Stage status
		private MotorStatusEn _motorStatus;

		public event EventHandler ConnectionUpdate;
        public event EventHandler MotorStatusUpdate;
        public event EventHandler PositionReached;
        public event EventHandler PositionVelocityUpdated;
		public event EventHandler OverrideChanged;

		public event EventHandler<xenaxCommunicationEventArgs> DataSent;
		public event EventHandler<xenaxCommunicationEventArgs> DataReceived;

		//-----------------------------------------------------------------------------------------------------------------------------
		private void ProcessResponse(string data)
        {
			//Postiton read
            Regex regex = new Regex("TP\r\n(-?[0-9]+)\r\n>");
            Match p = regex.Match(data);
            if (p.Success)
            {
                try
                {
                    string s = p.Groups[1].ToString();
					_motorPosition = Convert.ToInt32(s);

                }
                catch { }
            }

			//Speed read
			regex = new Regex(@"SP\?\r\n([0-9]+)\r\n>");
			Match sp = regex.Match(data);
			if (sp.Success)
			{
				try
				{
					string s = sp.Groups[1].ToString();
					_motorSpeed = Convert.ToInt32(s);

				}
				catch { }
			}
			//Speed set
			regex = new Regex(@"SP([0-9]+)\r\n>");
			sp = regex.Match(data);
			if (sp.Success)
			{
				try
				{
					string s = sp.Groups[1].ToString();
					_motorSpeed = Convert.ToInt32(s);

				}
				catch { }
			}


			//Acc read
			regex = new Regex(@"AC\?\r\n([0-9]+)\r\n>");
			Match a = regex.Match(data);
			if (a.Success)
			{
				try
				{
					string s = a.Groups[1].ToString();
					_motorAcceleration = Convert.ToInt32(s);

				}
				catch { }
			}
			//Acc set
			regex = new Regex(@"AC([0-9]+)\r\n>");
			a = regex.Match(data);
			if (a.Success)
			{
				try
				{
					string s = a.Groups[1].ToString();
					_motorAcceleration = Convert.ToInt32(s);

				}
				catch { }
			}

			//regex = new Regex("TV\r\n(.*?)\r\n");
            //v = regex.Match(data);
            //if (v.Success)
            //{
            //    try
            //    {
            //        string s = v.Groups[1].ToString();
            //        _motorSpeed = Convert.ToInt32(s);
            //    }
            //    catch { }
            //}

			//if (isPositionOrVelocityUpdated) { OnPositionOrVelocityUpdated(EventArgs.Empty); }
			OnPositionOrVelocityUpdated(EventArgs.Empty);

			regex = new Regex("OVRD.\r\n(.*?)\r\n");
            Match v = regex.Match(data);
            if (v.Success)
            {
                try
                {
                    string s = v.Groups[1].ToString();
                    MotorOverride = Convert.ToInt32(s);
                }
                catch { }
            }

            regex = new Regex("@S2\r\n>");
            v = regex.Match(data);
            if (v.Success)
            {
				_motorStatus = MotorStatusEn.InMotion;
                OnMotorStatusUpdate(EventArgs.Empty);
            }

            regex = new Regex("@S1\r\n>");
            v = regex.Match(data);
            if (v.Success)
            {
				_motorStatus = MotorStatusEn.PowerON;
				OnMotorStatusUpdate(EventArgs.Empty);

				OnPositionReached(EventArgs.Empty);
			}

			regex = new Regex("@S0\r\n>");
			v = regex.Match(data);
			if (v.Success)
			{
				_motorStatus = MotorStatusEn.PowerOFF;
				OnMotorStatusUpdate(EventArgs.Empty);
			}

			regex = new Regex("@S9\r\n>");
			v = regex.Match(data);
			if (v.Success)
			{
				_motorStatus = MotorStatusEn.Error;
				OnMotorStatusUpdate(EventArgs.Empty);
			}

			//data = data.Replace("\r\n", "<CRLF>");
			OnDataReceived(new xenaxCommunicationEventArgs(data));
        }
       
		//Sends TP command, call periodically to keep watch dog alive------------------------------------------------------------------
		private void WatchDog()
		{
			Send("TP\r");
		}
		//-----------------------------------------------------------------------------------------------------------------------------
		protected virtual void OnPositionOrVelocityUpdated(EventArgs e)
        {
            EventHandler handler = PositionVelocityUpdated;
            handler?.Invoke(this, e);
        }
        //-----------------------------------------------------------------------------------------------------------------------------
        protected virtual void OnConnectionStatusUpdate(EventArgs e)
        {
			EventHandler handler = ConnectionUpdate;
            handler?.Invoke(this, e);
        }
        //-----------------------------------------------------------------------------------------------------------------------------
        protected virtual void OnMotorStatusUpdate(EventArgs e)
        {
            EventHandler handler = MotorStatusUpdate;
            handler?.Invoke(this, e);
        }
        //-----------------------------------------------------------------------------------------------------------------------------
        protected virtual void OnPositionReached(EventArgs e)
        {
            EventHandler handler = PositionReached;
            handler?.Invoke(this, e);
        }
        //-----------------------------------------------------------------------------------------------------------------------------
        protected virtual void OnOverrideChanged(EventArgs e)
        {
            EventHandler handler = OverrideChanged;
            handler?.Invoke(this, e);
        }
		//-----------------------------------------------------------------------------------------------------------------------------
		protected virtual void OnDataSent(xenaxCommunicationEventArgs e)
		{
			EventHandler<xenaxCommunicationEventArgs> handler = DataSent;
			handler?.Invoke(this, e);
		}
		//-----------------------------------------------------------------------------------------------------------------------------
		protected virtual void OnDataReceived(xenaxCommunicationEventArgs e)
		{
			EventHandler<xenaxCommunicationEventArgs> handler = DataReceived;
			handler?.Invoke(this, e);
		}
		//-----------------------------------------------------------------------------------------------------------------------------
		public void MotorON()
        {
            Send("PWC\r");
        }
        //-----------------------------------------------------------------------------------------------------------------------------
        public void MotorHoming()
        {
            Send("REF\r");
        }
        //-----------------------------------------------------------------------------------------------------------------------------
        public void MotorOFF()
        {
            Send("PQ\r");
        }
        //-----------------------------------------------------------------------------------------------------------------------------
        public void MotorGoToPositionAbsolute(long position)
        {
			Send("G" + position + "\r");
        }
		//-----------------------------------------------------------------------------------------------------------------------------
		public void MotorGoInfiniteCounterClockwise()
		{
			Send("JN\r");
		}
		//-----------------------------------------------------------------------------------------------------------------------------
		public void MotorGoInfiniteClockwise()
		{
			Send("JP\r");
		}
		//-----------------------------------------------------------------------------------------------------------------------------		
		public void MotorStopMotion()
        {
            Send("SM\r");
        }
		//-----------------------------------------------------------------------------------------------------------------------------
		public void MotorSetSpeed(UInt64 speed)
		{
			Send("SP"+speed.ToString()+"\r");
		}
		//-----------------------------------------------------------------------------------------------------------------------------
		public void MotorGetSpeed()
		{
			Send("SP?\r");
		}
		//-----------------------------------------------------------------------------------------------------------------------------
		public void MotorSetAcceleration(UInt64 acc)
		{
			Send("AC" + acc.ToString() + "\r");
		}
		//-----------------------------------------------------------------------------------------------------------------------------
		public void MotorGetAcceleration()
		{
			Send("AC?\r");
		}
		//-----------------------------------------------------------------------------------------------------------------------------
		public void MotorSetEvents()
		{
			Send("EVT1\r");
		}
		//-----------------------------------------------------------------------------------------------------------------------------
		public void MotorGetPosition()
        {
			Send("TP\r");
        }
		//ROTARY STAGE ONLY------------------------------------------------------------------------------------------------------------
		public void MotorClearPosition()
		{
			Send("CLPO\r");
		}
		//-----------------------------------------------------------------------------------------------------------------------------
		public long MotorVelocity
        {
            get { return MotorSpeed; }
        }
        //-----------------------------------------------------------------------------------------------------------------------------
        public int MotorOverride
        {
            get { return motorOverride; }
            set
            {
                motorOverride = value;
                Send("OVRD" + motorOverride.ToString() + "\r");
                OnOverrideChanged(EventArgs.Empty);
            }
        }
		//-----------------------------------------------------------------------------------------------------------------------------
		public MotorStatusEn MotorStatus
		{
			get { return _motorStatus; }
		}
		//-----------------------------------------------------------------------------------------------------------------------------
	}

	public class xenaxCommunicationEventArgs : EventArgs
	{
		private readonly string _data;
		public xenaxCommunicationEventArgs(string data) { _data = data; }
		public string Data { get { return _data; } }
	}
}
