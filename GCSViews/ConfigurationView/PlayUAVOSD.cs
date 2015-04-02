using System;
using System.Collections;
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
using MissionPlanner.Comms;
using MissionPlanner.Utilities;
using System.Collections.Generic;
using BrightIdeasSoftware;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    public partial class PlayUAVOSD : UserControl, IActivate
    {
        static internal ICommsSerial comPort;
        byte[] eeprom = new byte[1024];
        byte[] paramdefault = new byte[1024];

        PlayUAVOSD self;

        // Changes made to the params between writing to the copter
        readonly Hashtable _changes = new Hashtable();

        readonly Hashtable _paramsAddr = new Hashtable();


        public int osd_rev;

        public enum Code : byte
        {
            // response codes
            NOP = 0x00,
            OK = 0x10,
            FAILED = 0x11,
            INSYNC = 0x12,
            INVALID = 0x13,

            // protocol commands
            EOC = 0x20,
            GET_SYNC = 0x21,
            GET_DEVICE = 0x22,
            CHIP_ERASE = 0x23,
            START_TRANSFER = 0x24,      //tell the osd we will start send params
            SET_PARAMS= 0x25,           //actually send params
            GET_PARAMS = 0x26,          //recv params from osd
            INFO_OSD_REV = 0x27,        //get the firmware revision
            END_TRANSFER = 0x28,        

            PROG_MULTI_MAX = 60,        //# protocol max is 255, must be multiple of 4
            READ_MULTI_MAX = 60,        //# protocol max is 255, something overflows with >= 64

        }

        /* *********************************************** */
        // Version number, incrementing this will erase/upload factory settings.
        // Only devs should increment this
        const int VER = 1;

        public class data
        {
            public string root;
            public string paramname;
            public string Value;
            public string unit;
            public string range;
            public string desc;
            public List<data> children = new List<PlayUAVOSD.data>();
        }

        public void u16toEPPROM(byte[] buf, int addr, short val)
        {
            buf[addr] = (byte)(val & 0xFF);
            buf[addr + 1] = (byte)((val >> 8) & 0xFF);
        }

        internal string getU16ParamString(byte[] buf, int paramAddr)
        {
            short stmp = Convert.ToInt16(buf[paramAddr]);
            short stmp1 = Convert.ToInt16(buf[paramAddr + 1]);
            string strRet = Convert.ToString(stmp+(stmp1<<8));
            return strRet;
        }

        

        public void Activate()
        {
            //this.SuspendLayout();

            //processToScreen();

            //this.ResumeLayout();
           
        }

        

        public PlayUAVOSD()
        {
            self = this;
            InitializeComponent();
            comPort = new MissionPlanner.Comms.SerialPort();

            setDefaultParams();

        }

        public void __send(byte c)
        {
            comPort.Write(new byte[] { c }, 0, 1);
        }

        public void __send(byte[] c)
        {
            comPort.Write(c, 0, c.Length);
        }

        public byte[] __recv(int count = 1)
        {
            // this will auto timeout
            // Console.WriteLine("recv "+count);
            byte[] c = new byte[count];
            int pos = 0;
            while (pos < count)
                pos += comPort.Read(c, pos, count - pos);

            return c;
        }

        public int __recv_int()
        {
            byte[] raw = __recv(4);
            //raw.Reverse();
            int val = BitConverter.ToInt32(raw, 0);
            return val;
        }

        public void __getSync()
        {
            comPort.BaseStream.Flush();
            byte c = __recv()[0];
            if (c != (byte)Code.INSYNC)
                throw new Exception(string.Format("unexpected {0:X} instead of INSYNC", (byte)c));
            c = __recv()[0];
            if (c == (byte)Code.INVALID)
                throw new Exception(string.Format("playuavosd reports INVALID OPERATION", (byte)c));
            if (c == (byte)Code.FAILED)
                throw new Exception(string.Format("playuavosd reports OPERATION FAILED", (byte)c));
            if (c != (byte)Code.OK)
                throw new Exception(string.Format("unexpected {0:X} instead of OK", (byte)c));
        }

        public void __sync()
        {
            comPort.BaseStream.Flush();
            __send(new byte[] { (byte)Code.GET_SYNC, (byte)Code.EOC });
            __getSync();
        }

        public bool __trySync()
        {
            comPort.BaseStream.Flush();
            byte c = __recv()[0];
            if (c != (byte)Code.INSYNC)
                return false;
            if (c != (byte)Code.OK)
                return false;

            return true;
        }

        public int __getInfo(Code param)
        {
            __send(new byte[] { (byte)Code.GET_DEVICE, (byte)param, (byte)Code.EOC });
            byte c = __recv()[0];
            int info = c;
            __getSync();
            //Array.Reverse(raw);
            return info;
        }

        public List<Byte[]> __split_len(byte[] seq, int length)
        {
            List<Byte[]> answer = new List<byte[]>();
            int size = length;
            for (int a = 0; a < seq.Length; )
            {
                byte[] ba = new byte[size];
                // Console.WriteLine(a + " " + seq.Length +" " + size);
                Array.Copy(seq, a, ba, 0, size);
                answer.Add(ba);
                a += size;
                if ((seq.Length - a) < size)
                    size = seq.Length - a;
            }
            return answer;
        }

        public void __set_parameters(byte[] data)
        {
            __send(new byte[] { (byte)Code.SET_PARAMS, (byte)data.Length });
            __send(data);
            __send((byte)Code.EOC);
            __getSync();
        }

        private void btn_Save_To_OSD_Click(object sender, EventArgs e)
        {
            if (comPort.IsOpen)
                comPort.Close();

            try
            {
                comPort.PortName = MainV2.comPortName;
                comPort.BaudRate = int.Parse(MainV2._connectionControl.CMB_baudrate.Text);
                comPort.ReadBufferSize = 1024 * 1024 * 4;
                comPort.Open();
            }
            catch { MessageBox.Show("打开端口错误", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            System.Threading.Thread.Sleep(500);

            // make sure we are in sync before starting
            self.__sync();

            //get the board version first
            self.osd_rev = self.__getInfo(Code.INFO_OSD_REV);

            //not matched, send the default params to EEPROM
            if (self.osd_rev != VER)
            {
                //TODO
            }

            //开始写入参数
            __send(new byte[] { (byte)Code.START_TRANSFER, (byte)Code.EOC });
            __getSync();
            List<byte[]> groups = self.__split_len(eeprom, (byte)Code.PROG_MULTI_MAX);
            foreach (Byte[] bytes in groups)
            {
                self.__set_parameters(bytes);
            }
            __send(new byte[] { (byte)Code.END_TRANSFER, (byte)Code.EOC });
            __getSync();

            //TODO - CRC校验

            comPort.BaseStream.Flush();
            System.Threading.Thread.Sleep(500);
            comPort.Close();

            MessageBox.Show("写入参数成功", "写入", MessageBoxButtons.OK, MessageBoxIcon.Error);

        }

        private void btn_Load_from_OSD_Click(object sender, EventArgs e)
        {
            if (comPort.IsOpen)
                comPort.Close();

            try
            {
                comPort.PortName = MainV2.comPortName;
                comPort.BaudRate = int.Parse(MainV2._connectionControl.CMB_baudrate.Text);
                comPort.ReadBufferSize = 1024 * 1024 * 4;
                comPort.Open();
            }
            catch { MessageBox.Show("打开端口错误", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            System.Threading.Thread.Sleep(500);

            // make sure we are in sync before starting
            self.__sync();

            ////get the board version first
            //self.osd_rev = self.__getInfo(Code.INFO_OSD_REV);

            ////not matched, send the default params to EEPROM
            //if (self.osd_rev != VER)
            //{
            //    //TODO
            //}

            ////开始读入参数
            //__send(new byte[] { (byte)Code.START_TRANSFER, (byte)Code.EOC });
            //__getSync();

            //first clear the recv buf
            comPort.ReadExisting();

            __send(new byte[] { (byte)Code.GET_PARAMS, (byte)Code.EOC });
            byte[] eepromtmp = __recv(1024);
            //byte[] c = new byte[8];
            //int pos = 0;
            //while (pos < 8)
            //    pos += comPort.Read(c, pos, 8 - pos);
            __getSync();


            //CRC校验?

            //comPort.BaseStream.Flush();
            System.Threading.Thread.Sleep(500);
            comPort.Close();

            MessageBox.Show("读出参数成功", "读出", MessageBoxButtons.OK, MessageBoxIcon.Error);

        }

        private void Params_CellEditFinishing(object sender, BrightIdeasSoftware.CellEditEventArgs e)
        {
            if (e.NewValue != e.Value && e.Cancel == false)
            {
                Console.WriteLine(e.NewValue + " " + e.NewValue.GetType());

                //double min = 0;
                //double max = 0;

                float newvalue = 0;

                try
                {
                    newvalue = float.Parse(e.NewValue.ToString());
                }
                catch { CustomMessageBox.Show("Bad number"); e.Cancel = true; return; }

                //if (ParameterMetaDataRepository.GetParameterRange(((data)e.RowObject).paramname, ref min, ref max, MainV2.comPort.MAV.cs.firmware.ToString()))
                //{
                //    if (newvalue > max || newvalue < min)
                //    {
                //        if (CustomMessageBox.Show(((data)e.RowObject).paramname + " value is out of range. Do you want to continue?", "Out of range", MessageBoxButtons.YesNo) == DialogResult.No)
                //        {
                //            return;
                //        }
                //    }
                //}

                _changes[((data)e.RowObject).paramname] = newvalue;
                string paramsfullname = ((data)e.RowObject).root + "_" + ((data)e.RowObject).paramname;
                u16toEPPROM(eeprom, (int)_paramsAddr[paramsfullname], Convert.ToInt16(newvalue));

                ((data)e.RowObject).Value = e.NewValue.ToString();

                var typer = e.RowObject.GetType();

                e.Cancel = true;

                Params.RefreshObject(e.RowObject);

            }

        }

        private void Params_FormatRow(object sender, FormatRowEventArgs e)
        {
            if (e != null && e.ListView != null && e.ListView.Items.Count > 0)
            {
                if (_changes.ContainsKey(((data)e.Model).paramname))
                    e.Item.BackColor = Color.Green;
                else
                    e.Item.BackColor = this.BackColor;
            }
        }

        private void setDefaultParams()
        {
            int address = 0;
            
            _paramsAddr["Arm_State_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Arm_State_Enable"], 1);
            _paramsAddr["Arm_State_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Arm_State_H_Position"], 0);
            _paramsAddr["Arm_State_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Arm_State_V_Position"], 14);
            _paramsAddr["Arm_State_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Arm_State_Font_Size"], 0);
            _paramsAddr["Arm_State_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Arm_State_H_Alignment"], 0);

            _paramsAddr["Battery_Voltage_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Voltage_Enable"], 1);
            _paramsAddr["Battery_Voltage_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Voltage_H_Position"], 350);
            _paramsAddr["Battery_Voltage_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Voltage_V_Position"], 4);
            _paramsAddr["Battery_Voltage_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Voltage_Font_Size"], 0);
            _paramsAddr["Battery_Voltage_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Voltage_H_Alignment"], 2);

            _paramsAddr["Battery_Current_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Current_Enable"], 1);
            _paramsAddr["Battery_Current_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Current_H_Position"], 350);
            _paramsAddr["Battery_Current_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Current_V_Position"], 14);
            _paramsAddr["Battery_Current_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Current_Font_Size"], 0);
            _paramsAddr["Battery_Current_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Current_H_Alignment"], 2);

            _paramsAddr["Battery_Consumed_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Consumed_Enable"], 1);
            _paramsAddr["Battery_Consumed_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Consumed_H_Position"], 350);
            _paramsAddr["Battery_Consumed_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Consumed_V_Position"], 24);
            _paramsAddr["Battery_Consumed_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Consumed_Font_Size"], 0);
            _paramsAddr["Battery_Consumed_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Consumed_H_Alignment"], 2);

            _paramsAddr["Flight_Mode_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Flight_Mode_Enable"], 1);
            _paramsAddr["Flight_Mode_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Flight_Mode_H_Position"], 0);
            _paramsAddr["Flight_Mode_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Flight_Mode_V_Position"], 4);
            _paramsAddr["Flight_Mode_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Flight_Mode_Font_Size"], 0);
            _paramsAddr["Flight_Mode_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Flight_Mode_H_Alignment"], 0);

            _paramsAddr["GPS_Status_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_Enable"], 1);
            _paramsAddr["GPS_Status_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_H_Position"], 0);
            _paramsAddr["GPS_Status_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_V_Position"], 230);
            _paramsAddr["GPS_Status_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_Font_Size"], 0);
            _paramsAddr["GPS_Status_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_H_Alignment"], 0);

            _paramsAddr["GPS_Latitude_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Latitude_Enable"], 1);
            _paramsAddr["GPS_Latitude_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Latitude_H_Position"], 200);
            _paramsAddr["GPS_Latitude_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Latitude_V_Position"], 230);
            _paramsAddr["GPS_Latitude_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Latitude_Font_Size"], 0);
            _paramsAddr["GPS_Latitude_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Latitude_H_Alignment"], 0);

            _paramsAddr["GPS_Longitude_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Longitude_Enable"], 1);
            _paramsAddr["GPS_Longitude_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Longitude_H_Position"], 300);
            _paramsAddr["GPS_Longitude_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Longitude_V_Position"], 230);
            _paramsAddr["GPS_Longitude_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Longitude_Font_Size"], 0);
            _paramsAddr["GPS_Longitude_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Longitude_H_Alignment"], 2);

            _paramsAddr["Time_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_Enable"], 1);
            _paramsAddr["Time_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_H_Position"], 350);
            _paramsAddr["Time_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_V_Position"], 220);
            _paramsAddr["Time_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_Font_Size"], 0);
            _paramsAddr["Time_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_H_Alignment"], 2);

            _paramsAddr["Altitude_Mode"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_Mode"], 1);
            _paramsAddr["Altitude_TALT_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_TALT_H_Position"], 200);
            _paramsAddr["Altitude_TALT_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_TALT_V_Position"], 200);
            _paramsAddr["Altitude_TALT_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_TALT_Font_Size"], 0);
            _paramsAddr["Altitude_TALT_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_TALT_H_Alignment"], 0);
            _paramsAddr["Altitude_Scale_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_Scale_H_Position"], 350);
            _paramsAddr["Altitude_Scale_Align"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_Scale_Align"], 1);
            _paramsAddr["Altitude_Scale_Source"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_Scale_Source"], 0);

            _paramsAddr["Speed_Mode"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_Mode"], 1);
            _paramsAddr["Speed_TSPD_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_TSPD_H_Position"], 350);
            _paramsAddr["Speed_TSPD_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_TSPD_V_Position"], 350);
            _paramsAddr["Speed_TSPD_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_TSPD_Font_Size"], 0);
            _paramsAddr["Speed_TSPD_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_TSPD_H_Alignment"], 0);
            _paramsAddr["Speed_Scale_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_Scale_H_Position"], 10);
            _paramsAddr["Speed_Scale_Align"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_Scale_Align"], 0);
            _paramsAddr["Speed_Scale_Source"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_Scale_Source"], 0);

            _paramsAddr["Throttle_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_Enable"], 1);
            _paramsAddr["Throttle_Scale_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_Scale_Enable"], 1);
            _paramsAddr["Throttle_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_H_Position"], 25);
            _paramsAddr["Throttle_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_V_Position"], 210);
            
            //we combination the direction of compass, way-point, home together.one mode is traditional. another is more intuitively IMO
            _paramsAddr["CWH_Mode"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Mode"], 1);
            _paramsAddr["CWH_Home_Distance_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Home_Distance_Enable"], 1);
            _paramsAddr["CWH_Home_Distance_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Home_Distance_H_Position"], 350);
            _paramsAddr["CWH_Home_Distance_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Home_Distance_V_Position"], 220);
            _paramsAddr["CWH_Home_Distance_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Home_Distance_Font_Size"], 0);
            _paramsAddr["CWH_Home_Distance_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Home_Distance_H_Alignment"], 0);
            _paramsAddr["CWH_WP_Distance_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_WP_Distance_Enable"], 1);
            _paramsAddr["CWH_WP_Distance_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_WP_Distance_H_Position"], 350);
            _paramsAddr["CWH_WP_Distance_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_WP_Distance_V_Position"], 220);
            _paramsAddr["CWH_WP_Distance_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_WP_Distance_Font_Size"], 0);
            _paramsAddr["CWH_WP_Distance_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_WP_Distance_H_Alignment"], 0);
            _paramsAddr["CWH_Tmode_Compass_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Tmode_Compass_Enable"], 1);
            _paramsAddr["CWH_Tmode_Compass_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Tmode_Compass_V_Position"], 1);
            _paramsAddr["CWH_Tmode_Home_Dir_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Tmode_Home_Dir_Enable"], 25);
            _paramsAddr["CWH_Tmode_Home_Dir_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Tmode_Home_Dir_H_Position"], 210);
            _paramsAddr["CWH_Tmode_Home_Dir_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Tmode_Home_Dir_V_Position"], 210);
            _paramsAddr["CWH_Tmode_WP_Dir_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Tmode_WP_Dir_Enable"], 25);
            _paramsAddr["CWH_Tmode_WP_Dir_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Tmode_WP_Dir_H_Position"], 210);
            _paramsAddr["CWH_Tmode_WP_Dir_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Tmode_WP_Dir_V_Position"], 210);
            _paramsAddr["CWH_Nmode_Compass_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Nmode_Compass_Enable"], 1);
            _paramsAddr["CWH_Nmode_Home_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Nmode_Home_Enable"], 1);
            _paramsAddr["CWH_Nmode_WP_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Nmode_WP_Enable"], 1);
            _paramsAddr["CWH_Nmode_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Nmode_H_Position"], 275);
            _paramsAddr["CWH_Nmode_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Nmode_V_Position"], 190);
            _paramsAddr["CWH_Nmode_Radius"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Nmode_Radius"], 20);
            _paramsAddr["CWH_Nmode_Home_Radius"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Nmode_Home_Radius"], 25);
            _paramsAddr["CWH_Nmode_WP_Radius"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CWH_Nmode_WP_Radius"], 25);

            //Attitude
            _paramsAddr["Attitude_Mode"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Attitude_Mode"], 0);

            _paramsAddr["Units_Mode"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Units_Mode"], 0);
       
            //should use bit mask? enable/disable maybe more intuition
            _paramsAddr["Alarm_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_H_Position"], 0);
            _paramsAddr["Alarm_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_V_Position"], 220);
            _paramsAddr["Alarm_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Font_Size"], 2);
            _paramsAddr["Alarm_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_H_Alignment"], 1);
            _paramsAddr["Alarm_Min_Speed_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Min_Speed_Enable"], 1);
            _paramsAddr["Alarm_Min_Speed"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Min_Speed"], 2);
            _paramsAddr["Alarm_Max_Speed_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Max_Speed_Enable"], 1);
            _paramsAddr["Alarm_Max_Speed"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Max_Speed"], 30);
            _paramsAddr["Alarm_Min_Alt_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Min_Alt_Enable"], 1);
            _paramsAddr["Alarm_Min_Alt"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Min_Alt"], 1);
            _paramsAddr["Alarm_Max_Alt_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Max_Alt_Enable"], 1);
            _paramsAddr["Alarm_Max_Alt"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Max_Alt"], 500);
            _paramsAddr["Alarm_Min_BattVol_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Min_BattVol_Enable"], 1);
            _paramsAddr["Alarm_Min_BattVol"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Min_BattVol"], 0);
            _paramsAddr["Alarm_Min_BattPercent_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Min_BattPercent_Enable"], 1);
            _paramsAddr["Alarm_Min_BattPercent"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Min_BattPercent"], 0);
        }

        internal PlayUAVOSD.data genChildData(string root, string name, string value, string unit, string range, string desc)
        {
            data data = new PlayUAVOSD.data();
            data.root = root;
            data.paramname = name;
            data.Value = value;
            data.unit = unit;
            data.range = range;
            data.desc = desc;
            return data;
        }

        internal void processToScreen()
        {
            Params.Items.Clear();

            Params.Objects.ForEach(x => { Params.RemoveObject(x); });

            Params.CellEditActivation = BrightIdeasSoftware.ObjectListView.CellEditActivateMode.SingleClick;

            Params.CanExpandGetter = delegate(object x)
            {
                data y = (data)x;
                if (y.children != null && y.children.Count > 0)
                    return true;
                return false;
            };

            Params.ChildrenGetter = delegate(object x)
            {
                data y = (data)x;
                return new ArrayList(y.children);
            };

            List<data> roots = new List<data>();

            data dataArm = new PlayUAVOSD.data();
            dataArm.paramname = "Arm_State";
            dataArm.children.Add(genChildData(dataArm.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataArm.children.Add(genChildData(dataArm.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataArm.children.Add(genChildData(dataArm.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataArm.children.Add(genChildData(dataArm.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataArm.children.Add(genChildData(dataArm.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            roots.Add(dataArm);

            data dataBattVolt = new PlayUAVOSD.data();
            dataBattVolt.paramname = "Battery_Voltage";
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            roots.Add(dataBattVolt);

            data dataBattCurrent = new PlayUAVOSD.data();
            dataBattCurrent.paramname = "Battery_Current";
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            roots.Add(dataBattCurrent);

            data dataBattConsumed = new PlayUAVOSD.data();
            dataBattConsumed.paramname = "Battery_Consumed";
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            roots.Add(dataBattConsumed);

            data dataFlightMode = new PlayUAVOSD.data();
            dataFlightMode.paramname = "Flight_Mode";
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            roots.Add(dataFlightMode);

            data dataGPSStatus = new PlayUAVOSD.data();
            dataGPSStatus.paramname = "GPS_Status";
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            roots.Add(dataGPSStatus);

            data dataGPSLat = new PlayUAVOSD.data();
            dataGPSLat.paramname = "GPS_Latitude";
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            roots.Add(dataGPSLat);

            data dataGPSLon = new PlayUAVOSD.data();
            dataGPSLon.paramname = "GPS_Longitude";
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            roots.Add(dataGPSLon);

            data dataTime = new PlayUAVOSD.data();
            dataTime.paramname = "Time";
            dataTime.children.Add(genChildData(dataTime.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Time_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataTime.children.Add(genChildData(dataTime.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Time_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataTime.children.Add(genChildData(dataTime.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Time_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataTime.children.Add(genChildData(dataTime.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Time_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataTime.children.Add(genChildData(dataTime.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Time_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            roots.Add(dataTime);

            data dataAlt = new PlayUAVOSD.data();
            dataAlt.paramname = "Altitude";
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Mode", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_Mode"]), "", "0, 1", "0:Traditional, 1:Scale"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Scale_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_Scale_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Scale_Align", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_Scale_Align"]), "", "0, 1", "0:left 1:right"));
            roots.Add(dataAlt);

            data dataSpeed = new PlayUAVOSD.data();
            dataSpeed.paramname = "Speed";
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Mode", getU16ParamString(eeprom, (int)_paramsAddr["Speed_Mode"]), "", "0, 1", "0:Traditional, 1:Scale"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Scale_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Speed_Scale_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Scale_Align", getU16ParamString(eeprom, (int)_paramsAddr["Speed_Scale_Align"]), "", "0, 1", "0:left 1:right"));
            roots.Add(dataAlt);

            data dataThrottle = new PlayUAVOSD.data();
            dataThrottle.paramname = "Throttle";
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "Scale_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_Scale_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_V_Position"]), "", "0 - 230", "Vertical Position"));
            roots.Add(dataThrottle);

            data dataCWH = new PlayUAVOSD.data();
            dataCWH.paramname = "CWH";
            dataCWH.desc = "Compass, Way-point and Home";
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Mode", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Mode"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Home_Distance_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Home_Distance_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Home_Distance_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Home_Distance_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Home_Distance_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Home_Distance_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Home_Distance_Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Home_Distance_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Home_Distance_H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Home_Distance_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "WP_Distance_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CWH_WP_Distance_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "WP_Distance_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_WP_Distance_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "WP_Distance_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_WP_Distance_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "WP_Distance_Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["CWH_WP_Distance_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "WP_Distance_H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["CWH_WP_Distance_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Tmode_Compass_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Tmode_Compass_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Tmode_Compass_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Tmode_Compass_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Tmode_Home_Dir_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Tmode_Home_Dir_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Tmode_Home_Dir_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Tmode_Home_Dir_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Tmode_Home_Dir_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Tmode_Home_Dir_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "_Tmode_WP_Dir_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Tmode_WP_Dir_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Tmode_WP_Dir_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Tmode_WP_Dir_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Tmode_WP_Dir_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Tmode_WP_Dir_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Nmode_Compass_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Nmode_Compass_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Nmode_Home_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Nmode_Home_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Nmode_WP_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Nmode_WP_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Nmode_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Nmode_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Nmode_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Nmode_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Nmode_Radius", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Nmode_Radius"]), "", "0 - 230", "Radius of the circle"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Nmode_Home_Radius", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Nmode_Home_Radius"]), "", "0 - 230", "distance from the center"));
            dataCWH.children.Add(genChildData(dataCWH.paramname, "Nmode_WP_Radius", getU16ParamString(eeprom, (int)_paramsAddr["CWH_Nmode_WP_Radius"]), "", "0 - 230", "distance from the center"));
            roots.Add(dataCWH);

            data dataAtti = new PlayUAVOSD.data();
            dataAtti.paramname = "Attitude";
            dataAtti.desc = "Flight Attitude";
            dataAtti.children.Add(genChildData(dataAtti.paramname, "Mode", getU16ParamString(eeprom, (int)_paramsAddr["Attitude_Mode"]), "", "0, 1", "0:MissionPlanner GUI, 1:3D"));
            roots.Add(dataAtti);

            roots.Add(genChildData("", "Units_Mode", getU16ParamString(eeprom, (int)_paramsAddr["Units_Mode"]), "", "0, 1", "0:metric 1:imperial"));

            data dataAlarm = new PlayUAVOSD.data();
            dataAlarm.paramname = "Alarm";
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_H_Position"]), "", "0 - 350", "Horizontal Position"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_V_Position"]), "", "0 - 230", "Vertical Position"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Font_Size"]), "", "0, 1, 2", "0:small, 1:normal, 2:large"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_H_Alignment"]), "", "0, 1, 2", "0:left,  1:center, 2:right"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Min_Speed_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Min_Speed_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Min_Speed", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Min_Speed"]), "", "", ""));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Max_Speed_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Max_Speed_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Max_Speed", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Max_Speed"]), "", "", ""));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Min_Alt_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Min_Alt_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Min_Alt", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Min_Alt"]), "", "", ""));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Max_Alt_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Max_Alt_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Max_Alt", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Max_Alt"]), "", "", ""));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Min_BattVol_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Min_BattVol_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Min_BattVol", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Min_BattVol"]), "", "", ""));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Min_BattPercent_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Min_BattPercent_Enable"]), "", "0, 1", "0:disabled, 1:enabled"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Min_BattPercent", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Min_BattPercent"]), "", "0 - 99", ""));
            roots.Add(dataAlarm);

            foreach (var item in roots)
            {
                // if the child has no children, we dont need the root.
                if (((List<data>)item.children).Count == 1)
                {
                    Params.AddObject(((List<data>)item.children)[0]);
                    continue;
                }

                Params.AddObject(item);
            }
        }

        private void btn_Load_Default_Click(object sender, EventArgs e)
        {
            eeprom = paramdefault;
            processToScreen();
        }
    }
}