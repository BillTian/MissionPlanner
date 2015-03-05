using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO.Ports;
using System.IO;
using ArdupilotMega;
using System.Xml;
using System.Globalization;
using MissionPlanner.Controls;
using System.IO.Ports;
using MissionPlanner.Comms;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    public partial class PlayUAVOSD : UserControl, IActivate
    {
        static internal ICommsSerial comPort;

        public void Activate()
        {
            

           
        }

        public PlayUAVOSD()
        {
            InitializeComponent();            
           
        }

        private void button1_Click(object sender, EventArgs e)
        {
            comPort = new MissionPlanner.Comms.SerialPort();
            comPort.PortName = MainV2.comPortName;
            comPort.BaudRate = int.Parse(MainV2._connectionControl.CMB_baudrate.Text);
            comPort.ReadBufferSize = 1024 * 1024 * 4;
        }

    }
}