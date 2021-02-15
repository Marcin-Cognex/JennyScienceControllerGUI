using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using System.Windows.Threading;
using XenaxModel;

namespace JennyScienceControllerGUI
{
	public class XenaxStageGUIControlVM : INotifyPropertyChanged
	{
		//Upper and lower limits for input values
		public const UInt64 MaxSpeed = 1000000;
		public const UInt64 MaxAcc = 20000000;
		public const UInt64 MinPositionLinear = 0;
		public const UInt64 MaxPositionLinear = 272000;
		public ObservableCollection<XenaxDeviceConnection> xenaxDeviceConnectionsVM;

		#region Xenax properties (StageConnected,CurrentStageType, StagePowerOn, StageSpeed etc.)

		public bool _StageConnected;
		public bool StageConnected
		{
			get { return this._StageConnected; }
			set
			{
				if (value != this._StageConnected)
				{
					this._StageConnected = value;
					this.OnPropertyChanged("StageConnected");
					this.OnPropertyChanged("StageNotConnected");
				}
			}
		}
		public bool StageNotConnected
		{
			get { return !this._StageConnected; }
		}

		private string _StageMotorStatus;
		public string StageMotorStatus
		{
			get { return this._StageMotorStatus; }
			set
			{
				if (value != this._StageMotorStatus)
				{
					this._StageMotorStatus = value;
					this.OnPropertyChanged("StageMotorStatus");
				}
			}
		}

		public int _CurrentStageType;
		public int CurrentStageType
		{
			get
			{
				return this._CurrentStageType;
			}

			set
			{
				if (value != this._CurrentStageType)
				{
					this._CurrentStageType = value;
					this.OnPropertyChanged("CurrentStageType");
				}
			}
		}

		private bool _StagePowerOn;
		public bool StagePowerOn
		{
			get { return this._StagePowerOn; }
			set
			{
				if (value != this._StagePowerOn)
				{
					this._StagePowerOn = value;
					this.OnPropertyChanged("StagePowerOn");
				}
			}
		}

		private UInt64 _StageSpeed;
		public UInt64 StageSpeed
		{
			get
			{
				return this._StageSpeed;
			}

			set
			{
				if (value != this._StageSpeed)
				{
					this._StageSpeed = value;
					this.OnPropertyChanged("StageSpeed");
				}
			}
		}

		private UInt64 _StageSpeedP1P2;
		public UInt64 StageSpeedP1P2
		{
			get
			{
				return this._StageSpeedP1P2;
			}

			set
			{
				if (value != this._StageSpeedP1P2)
				{
					this._StageSpeedP1P2 = value;
					this.OnPropertyChanged("StageSpeedP1P2");
				}
			}
		}

		private UInt64 _StageSpeedP2P1;
		public UInt64 StageSpeedP2P1
		{
			get
			{
				return this._StageSpeedP2P1;
			}

			set
			{
				if (value != this._StageSpeedP2P1)
				{
					this._StageSpeedP2P1 = value;
					this.OnPropertyChanged("StageSpeedP2P1");
				}
			}
		}

		private UInt64 _StageAcc;
		public UInt64 StageAcc
		{
			get
			{
				return this._StageAcc;
			}

			set
			{
				if (value != this._StageAcc)
				{
					this._StageAcc = value;

					this.OnPropertyChanged("StageAcc");
				}
			}
		}

		private Int64 _StagePosition1;
		public Int64 StagePosition1
		{
			get
			{
				return this._StagePosition1;
			}

			set
			{
				if (value != this._StagePosition1)
				{
					this._StagePosition1 = value;

					this.OnPropertyChanged("StagePosition1");
				}
			}
		}

		private Int64 _StagePosition2;
		public Int64 StagePosition2
		{
			get
			{
				return this._StagePosition2;
			}

			set
			{
				if (value != this._StagePosition2)
				{
					this._StagePosition2 = value;

					this.OnPropertyChanged("StagePosition2");
				}
			}
		}

		private Int64 _StagePositionCurrent;
		public Int64 StagePositionCurrent
		{
			get
			{
				return this._StagePositionCurrent;
			}

			set
			{
				if (value != this._StagePositionCurrent)
				{
					this._StagePositionCurrent = value;

					this.OnPropertyChanged("StagePositionCurrent");
				}
			}
		}

		private bool _StageContinuousRotation;
		public bool StageContinuousRotation
		{
			get
			{
				return this._StageContinuousRotation;
			}

			set
			{
				if (value != this._StageContinuousRotation)
				{
					this._StageContinuousRotation = value;

					this.OnPropertyChanged("StageContinuousRotation");
				}
			}
		}
		
		//TODO: find a better name for this.
		private bool _StageCycleStatus;
		public bool StageCycleStatus
		{
			get
			{
				return this._StageCycleStatus;
			}

			set
			{
				if (value != this._StageCycleStatus)
				{
					this._StageCycleStatus = value;

					this.OnPropertyChanged("StageCycleStatus");
				}
			}
		}

		private bool _StageCycle;
		public bool StageCycle
		{
			get
			{
				return this._StageCycle;
			}

			set
			{
				if (value != this._StageCycle)
				{
					this._StageCycle = value;

					this.OnPropertyChanged("StageCycle");
				}
			}
		}

		private bool _StageCycleClick;
		public bool StageCycleClick
		{
			get
			{
				return this._StageCycleClick;
			}

			set
			{
				if (value != this._StageCycleClick)
				{
					this._StageCycleClick = value;

					this.OnPropertyChanged("StageCycleClick");
				}
			}
		}
		#endregion

		public event PropertyChangedEventHandler PropertyChanged;




		public void OnPropertyChanged(string propertyName)
		{
			PropertyChangedEventHandler handler = PropertyChanged;
			handler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		//Default constructor loads values from project settings
		public XenaxStageGUIControlVM()
		{
			StageSpeed = 0;
			StageAcc = 0;
			StagePosition1 = 0;
			StagePosition2 = 0;
			StagePositionCurrent = 0;
			StageContinuousRotation = false;
			StageCycle = false;
			StageCycleStatus = false;
		}

		//Copy constructor
		public XenaxStageGUIControlVM(XenaxStageGUIControlVM xenaxStageGUI)
		{
			StageSpeed = xenaxStageGUI.StageSpeed;
			StageAcc = xenaxStageGUI.StageAcc;
			StagePosition1 = xenaxStageGUI.StagePosition1;
			StagePosition2 = xenaxStageGUI.StagePosition2;
			StagePositionCurrent = xenaxStageGUI.StagePositionCurrent;
			StageContinuousRotation = false;
			StageCycleStatus = false;
			StageCycle = false;
		}

		public void AddNewConncetion(string Name, StageTypeEn StageType, string IPAddress)
		{
			this.xenaxDeviceConnectionsVM.Add(new XenaxDeviceConnection(Name, StageType, IPAddress));
		}

		public void RemoveConncetion(int index)
		{
			this.xenaxDeviceConnectionsVM.RemoveAt(index);
		}
	}

	public class XenaxStageGUIExportable
	{
		public XenaxStageGUIExportable()
		{ }

		public XenaxStageGUIExportable(XenaxStageGUIControlVM toExport)
		{
			StageSpeed = toExport.StageSpeed;
			StageAcc = toExport.StageSpeed;
			StageSpeedP1P2 = toExport.StageSpeed;
			StageSpeedP2P1 = toExport.StageSpeed;
			StagePosition1 = toExport.StagePosition1;
			StagePosition2 = toExport.StagePosition2;
		}

		public UInt64	StageSpeed		{ get; set; }
		public UInt64	StageAcc		{ get; set; }
		public UInt64	StageSpeedP1P2 { get; set; }
		public UInt64	StageSpeedP2P1 { get; set; }
		public Int64	StagePosition1 { get; set; }
		public Int64	StagePosition2 { get; set; }
	}
}