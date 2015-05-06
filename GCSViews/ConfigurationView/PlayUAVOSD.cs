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
            SAVE_TO_EEPROM = 0x29,

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

        string panelValF2Str(int val)
        {
            if (val == 0)
                return "0";

            if (val == 1)
                return "1";

            string strRet = "";
            List<int> valArr = new List<int>();
            for (int i = 1; i < 10; i++)
            {
                int b = val & (int)(System.Math.Pow(2, i - 1));
                if (b != 0)
                {
                    valArr.Add(i);
                }
            }

            valArr.Sort();
            foreach (int i in valArr)
            {
                strRet += Convert.ToString(i) + ",";
            }
            strRet = strRet.Remove(strRet.Length - 1);
            return strRet;
        }

        float panelValStr2F(string str)
        {
            float newvalue = 0;
            try
            {
                string[] strnewarr = str.Split(',');
                double tmpvalue = 0;
                foreach (string bytes in strnewarr)
                {
                    tmpvalue = double.Parse(bytes);
                    newvalue += (float)(System.Math.Pow(2, tmpvalue - 1));
                }

            }
            catch { CustomMessageBox.Show("Bad number"); return 0; }

            return newvalue;
        }

        internal string getU16PanelString(byte[] buf, int paramAddr)
        {
            short stmp = Convert.ToInt16(buf[paramAddr]);
            short stmp1 = Convert.ToInt16(buf[paramAddr + 1]);
            int a = Convert.ToInt32(stmp + (stmp1 << 8));

            return panelValF2Str(a);
            if (a == 1)
                return "1";

            string strRet = "";
            List<int> valArr = new List<int>();
            for (int i = 1; i < 10; i++)
            {
                int b = a & (int)(System.Math.Pow(2, i - 1));
                if (b != 0)
                {
                    valArr.Add(i);
                }
            }

            valArr.Sort();
            foreach (int i in valArr)
            {
                strRet += Convert.ToString(i) + ",";
            }
            strRet = strRet.Remove(strRet.Length - 1);
            return strRet;
        }
        

        public void Activate()
        {
            //this.SuspendLayout();

            //processToScreen();

            //this.ResumeLayout();

            paramdefault.CopyTo(eeprom, 0);
            //for (int i = 0; i < paramdefault.Length; i++)
            //{
            //    eeprom[i] = paramdefault[i];
            //}
            processToScreen();
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
            try
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

            MessageBox.Show("写入参数成功", "写入", MessageBoxButtons.OK, MessageBoxIcon.None);

            }
            catch { MessageBox.Show("写入错误", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        }

        private void Sav_To_EEPROM_Click(object sender, EventArgs e)
        {
            try
            {

                if (comPort.IsOpen)
                    comPort.Close();

                try
                {
                    comPort.PortName = MainV2.comPortName;
                    comPort.BaudRate = int.Parse(MainV2._connectionControl.CMB_baudrate.Text);
                    comPort.ReadBufferSize = 1024;
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

                //send command
                __send(new byte[] { (byte)Code.SAVE_TO_EEPROM, (byte)Code.EOC });
                __getSync();

                comPort.BaseStream.Flush();
                comPort.Close();

                MessageBox.Show("写入参数成功", "写入", MessageBoxButtons.OK, MessageBoxIcon.None);

            }
            catch { MessageBox.Show("写入EEPROM错误", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
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

            //eeprom = eepromtmp;
            eepromtmp.CopyTo(eeprom, 0);

            processToScreen();

            //comPort.BaseStream.Flush();
            System.Threading.Thread.Sleep(500);
            comPort.Close();
            
            MessageBox.Show("读出参数成功", "读出", MessageBoxButtons.OK, MessageBoxIcon.None);

        }

        private void Params_CellEditFinishing(object sender, BrightIdeasSoftware.CellEditEventArgs e)
        {
            bool bPanelValue = false;
            string paramsfullname = ((data)e.RowObject).root + "_" + ((data)e.RowObject).paramname;

            if (paramsfullname.Contains("_Panel") && !(paramsfullname.Contains("PWM")) && !(paramsfullname.Contains("Max_Panels")))
                bPanelValue = true;

            if (e.NewValue != e.Value && e.Cancel == false)
            {
                Console.WriteLine(e.NewValue + " " + e.NewValue.GetType());

                //double min = 0;
                //double max = 0;
                if (((data)e.RowObject).children.Count > 0)
                {
                    e.Cancel = true;
                    return;
                }

                float newvalue = 0;

                if (bPanelValue)
                {
                    newvalue = panelValStr2F(e.NewValue.ToString());
                    if (newvalue == 0)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
                else
                {
                    try
                    {
                        newvalue = float.Parse(e.NewValue.ToString());
                    }
                    catch { CustomMessageBox.Show("Bad number"); e.Cancel = true; return; }
                }

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
            
            _paramsAddr["ArmState_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["ArmState_Enable"], 1);
            _paramsAddr["ArmState_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["ArmState_Panel"], 1);
            _paramsAddr["ArmState_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["ArmState_H_Position"], 350);
            _paramsAddr["ArmState_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["ArmState_V_Position"], 34);
            _paramsAddr["ArmState_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["ArmState_Font_Size"], 0);
            _paramsAddr["ArmState_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["ArmState_H_Alignment"], 2);

            _paramsAddr["BatteryVoltage_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryVoltage_Enable"], 1);
            _paramsAddr["BatteryVoltage_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryVoltage_Panel"], 1);
            _paramsAddr["BatteryVoltage_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryVoltage_H_Position"], 350);
            _paramsAddr["BatteryVoltage_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryVoltage_V_Position"], 4);
            _paramsAddr["BatteryVoltage_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryVoltage_Font_Size"], 0);
            _paramsAddr["BatteryVoltage_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryVoltage_H_Alignment"], 2);

            _paramsAddr["BatteryCurrent_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryCurrent_Enable"], 1);
            _paramsAddr["BatteryCurrent_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryCurrent_Panel"], 1);
            _paramsAddr["BatteryCurrent_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryCurrent_H_Position"], 350);
            _paramsAddr["BatteryCurrent_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryCurrent_V_Position"], 14);
            _paramsAddr["BatteryCurrent_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryCurrent_Font_Size"], 0);
            _paramsAddr["BatteryCurrent_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryCurrent_H_Alignment"], 2);

            _paramsAddr["BatteryConsumed_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryConsumed_Enable"], 1);
            _paramsAddr["BatteryConsumed_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryConsumed_Panel"], 1);
            _paramsAddr["BatteryConsumed_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryConsumed_H_Position"], 350);
            _paramsAddr["BatteryConsumed_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryConsumed_V_Position"], 24);
            _paramsAddr["BatteryConsumed_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryConsumed_Font_Size"], 0);
            _paramsAddr["BatteryConsumed_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["BatteryConsumed_H_Alignment"], 2);

            _paramsAddr["FlightMode_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["FlightMode_Enable"], 1);
            _paramsAddr["FlightMode_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["FlightMode_Panel"], 1);
            _paramsAddr["FlightMode_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["FlightMode_H_Position"], 350);
            _paramsAddr["FlightMode_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["FlightMode_V_Position"], 44);
            _paramsAddr["FlightMode_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["FlightMode_Font_Size"], 1);
            _paramsAddr["FlightMode_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["FlightMode_H_Alignment"], 2);

            _paramsAddr["GPSStatus_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSStatus_Enable"], 1);
            _paramsAddr["GPSStatus_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSStatus_Panel"], 1);
            _paramsAddr["GPSStatus_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSStatus_H_Position"], 0);
            _paramsAddr["GPSStatus_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSStatus_V_Position"], 230);
            _paramsAddr["GPSStatus_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSStatus_Font_Size"], 0);
            _paramsAddr["GPSStatus_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSStatus_H_Alignment"], 0);

            _paramsAddr["GPSHDOP_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSHDOP_Enable"], 1);
            _paramsAddr["GPSHDOP_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSHDOP_Panel"], 1);
            _paramsAddr["GPSHDOP_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSHDOP_H_Position"], 70);
            _paramsAddr["GPSHDOP_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSHDOP_V_Position"], 230);
            _paramsAddr["GPSHDOP_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSHDOP_Font_Size"], 0);
            _paramsAddr["GPSHDOP_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSHDOP_H_Alignment"], 0);

            _paramsAddr["GPSLatitude_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLatitude_Enable"], 1);
            _paramsAddr["GPSLatitude_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLatitude_Panel"], 1);
            _paramsAddr["GPSLatitude_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLatitude_H_Position"], 200);
            _paramsAddr["GPSLatitude_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLatitude_V_Position"], 230);
            _paramsAddr["GPSLatitude_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLatitude_Font_Size"], 0);
            _paramsAddr["GPSLatitude_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLatitude_H_Alignment"], 0);

            _paramsAddr["GPSLongitude_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLongitude_Enable"], 1);
            _paramsAddr["GPSLongitude_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLongitude_Panel"], 1);
            _paramsAddr["GPSLongitude_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLongitude_H_Position"], 280);
            _paramsAddr["GPSLongitude_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLongitude_V_Position"], 230);
            _paramsAddr["GPSLongitude_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLongitude_Font_Size"], 0);
            _paramsAddr["GPSLongitude_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPSLongitude_H_Alignment"], 0);

            _paramsAddr["GPS2Status_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Status_Enable"], 1);
            _paramsAddr["GPS2Status_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Status_Panel"], 2);
            _paramsAddr["GPS2Status_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Status_H_Position"], 0);
            _paramsAddr["GPS2Status_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Status_V_Position"], 230);
            _paramsAddr["GPS2Status_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Status_Font_Size"], 0);
            _paramsAddr["GPS2Status_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Status_H_Alignment"], 0);

            _paramsAddr["GPS2HDOP_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2HDOP_Enable"], 1);
            _paramsAddr["GPS2HDOP_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2HDOP_Panel"], 2);
            _paramsAddr["GPS2HDOP_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2HDOP_H_Position"], 70);
            _paramsAddr["GPS2HDOP_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2HDOP_V_Position"], 230);
            _paramsAddr["GPS2HDOP_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2HDOP_Font_Size"], 0);
            _paramsAddr["GPS2HDOP_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2HDOP_H_Alignment"], 0);

            _paramsAddr["GPS2Latitude_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Latitude_Enable"], 1);
            _paramsAddr["GPS2Latitude_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Latitude_Panel"], 2);
            _paramsAddr["GPS2Latitude_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Latitude_H_Position"], 200);
            _paramsAddr["GPS2Latitude_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Latitude_V_Position"], 230);
            _paramsAddr["GPS2Latitude_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Latitude_Font_Size"], 0);
            _paramsAddr["GPS2Latitude_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Latitude_H_Alignment"], 0);

            _paramsAddr["GPS2Longitude_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Longitude_Enable"], 1);
            _paramsAddr["GPS2Longitude_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Longitude_Panel"], 2);
            _paramsAddr["GPS2Longitude_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Longitude_H_Position"], 280);
            _paramsAddr["GPS2Longitude_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Longitude_V_Position"], 230);
            _paramsAddr["GPS2Longitude_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Longitude_Font_Size"], 0);
            _paramsAddr["GPS2Longitude_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2Longitude_H_Alignment"], 0);

            _paramsAddr["Time_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_Enable"], 1);
            _paramsAddr["Time_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_Panel"], 1);
            _paramsAddr["Time_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_H_Position"], 350);
            _paramsAddr["Time_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_V_Position"], 220);
            _paramsAddr["Time_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_Font_Size"], 0);
            _paramsAddr["Time_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Time_H_Alignment"], 2);

            _paramsAddr["Altitude_TALT_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_TALT_Enable"], 1);
            _paramsAddr["Altitude_TALT_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_TALT_Panel"], 2);
            _paramsAddr["Altitude_TALT_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_TALT_H_Position"], 5);
            _paramsAddr["Altitude_TALT_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_TALT_V_Position"], 10);
            _paramsAddr["Altitude_TALT_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_TALT_Font_Size"], 0);
            _paramsAddr["Altitude_TALT_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_TALT_H_Alignment"], 0);
            _paramsAddr["Altitude_Scale_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_Scale_Enable"], 1);
            _paramsAddr["Altitude_Scale_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_Scale_Panel"], 1);
            _paramsAddr["Altitude_Scale_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_Scale_H_Position"], 350);
            _paramsAddr["Altitude_Scale_Align"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_Scale_Align"], 1);
            _paramsAddr["Altitude_Scale_Source"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Altitude_Scale_Source"], 0);

            _paramsAddr["Speed_TSPD_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_TSPD_Enable"], 1);
            _paramsAddr["Speed_TSPD_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_TSPD_Panel"], 2);
            _paramsAddr["Speed_TSPD_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_TSPD_H_Position"], 5);
            _paramsAddr["Speed_TSPD_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_TSPD_V_Position"], 25);
            _paramsAddr["Speed_TSPD_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_TSPD_Font_Size"], 0);
            _paramsAddr["Speed_TSPD_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_TSPD_H_Alignment"], 0);
            _paramsAddr["Speed_Scale_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_Scale_Enable"], 1);
            _paramsAddr["Speed_Scale_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_Scale_Panel"], 1);
            _paramsAddr["Speed_Scale_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_Scale_H_Position"], 10);
            _paramsAddr["Speed_Scale_Align"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_Scale_Align"], 0);
            _paramsAddr["Speed_Scale_Source"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Speed_Scale_Source"], 0);

            _paramsAddr["Throttle_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_Enable"], 1);
            _paramsAddr["Throttle_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_Panel"], 1);
            _paramsAddr["Throttle_Scale_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_Scale_Enable"], 1);
            _paramsAddr["Throttle_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_H_Position"], 295);
            _paramsAddr["Throttle_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_V_Position"], 202);
            
            //home distance
            _paramsAddr["HomeDistance_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["HomeDistance_Enable"], 1);
            _paramsAddr["HomeDistance_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["HomeDistance_Panel"], 1);
            _paramsAddr["HomeDistance_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["HomeDistance_H_Position"], 70);
            _paramsAddr["HomeDistance_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["HomeDistance_V_Position"], 14);
            _paramsAddr["HomeDistance_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["HomeDistance_Font_Size"], 0);
            _paramsAddr["HomeDistance_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["HomeDistance_H_Alignment"], 0);

            //way-point distance
            _paramsAddr["WPDistance_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WPDistance_Enable"], 1);
            _paramsAddr["WPDistance_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WPDistance_Panel"], 1);
            _paramsAddr["WPDistance_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WPDistance_H_Position"], 70);
            _paramsAddr["WPDistance_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WPDistance_V_Position"], 24);
            _paramsAddr["WPDistance_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WPDistance_Font_Size"], 0);
            _paramsAddr["WPDistance_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WPDistance_H_Alignment"], 0);

            //heading, home and wp direction
            _paramsAddr["CHWDIR_Tmode_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHWDIR_Tmode_Enable"], 1);
            _paramsAddr["CHWDIR_Tmode_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHWDIR_Tmode_Panel"], 2);
            _paramsAddr["CHWDIR_Tmode_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHWDIR_Tmode_V_Position"], 15);
            _paramsAddr["CHWDIR_Nmode_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHWDIR_Nmode_Enable"], 1);
            _paramsAddr["CHWDIR_Nmode_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHWDIR_Nmode_Panel"], 1);
            _paramsAddr["CHWDIR_Nmode_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHWDIR_Nmode_H_Position"], 30);
            _paramsAddr["CHWDIR_Nmode_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHWDIR_Nmode_V_Position"], 35);
            _paramsAddr["CHWDIR_Nmode_Radius"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHWDIR_Nmode_Radius"], 20);
            _paramsAddr["CHWDIR_Nmode_Home_Radius"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHWDIR_Nmode_Home_Radius"], 25);
            _paramsAddr["CHWDIR_Nmode_WP_Radius"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHWDIR_Nmode_WP_Radius"], 25);

            //Attitude
            _paramsAddr["Attitude_MP_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Attitude_MP_Enable"], 1);
            _paramsAddr["Attitude_MP_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Attitude_MP_Panel"], 1);
            _paramsAddr["Attitude_MP_Mode"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Attitude_MP_Mode"], 0);
            _paramsAddr["Attitude_3D_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Attitude_3D_Enable"], 1);
            _paramsAddr["Attitude_3D_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Attitude_3D_Panel"], 2);

            //misc
            _paramsAddr["Misc_Units_Mode"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Misc_Units_Mode"], 0);
            _paramsAddr["Misc_Max_Panels"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Misc_Max_Panels"], 2);
            
            //PWM Config
            _paramsAddr["PWM_Video_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["PWM_Video_Enable"], 1);
            _paramsAddr["PWM_Video_Chanel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["PWM_Video_Chanel"], 6);
            _paramsAddr["PWM_Video_Value"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["PWM_Video_Value"], 1200);
            _paramsAddr["PWM_Panel_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["PWM_Panel_Enable"], 1);
            _paramsAddr["PWM_Panel_Chanel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["PWM_Panel_Chanel"], 7);
            _paramsAddr["PWM_Panel_Value"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["PWM_Panel_Value"], 1200);

            //should use bit mask? enable/disable maybe more intuition
            _paramsAddr["Alarm_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_H_Position"], 180);
            _paramsAddr["Alarm_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_V_Position"], 25);
            _paramsAddr["Alarm_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Font_Size"], 2);
            _paramsAddr["Alarm_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_H_Alignment"], 1);
            _paramsAddr["Alarm_GPS_Status_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_GPS_Status_Enable"], 1);
            _paramsAddr["Alarm_Low_Batt_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Low_Batt_Enable"], 1);
            _paramsAddr["Alarm_Low_Batt"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Low_Batt"], 20);
            _paramsAddr["Alarm_Under_Speed_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Under_Speed_Enable"], 0);
            _paramsAddr["Alarm_Under_Speed"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Under_Speed"], 2);
            _paramsAddr["Alarm_Over_Speed_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Over_Speed_Enable"], 0);
            _paramsAddr["Alarm_Over_Speed"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Over_Speed"], 100);
            _paramsAddr["Alarm_Under_Alt_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Under_Alt_Enable"], 0);
            _paramsAddr["Alarm_Under_Alt"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Under_Alt"], 10);
            _paramsAddr["Alarm_Over_Alt_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Over_Alt_Enable"], 0);
            _paramsAddr["Alarm_Over_Alt"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Over_Alt"], 1000);

            _paramsAddr["ClimbRate_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["ClimbRate_Enable"], 1);
            _paramsAddr["ClimbRate_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["ClimbRate_Panel"], 1);
            _paramsAddr["ClimbRate_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["ClimbRate_H_Position"], 5);
            _paramsAddr["ClimbRate_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["ClimbRate_V_Position"], 220);

            
            _paramsAddr["RSSI_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["RSSI_Enable"], 0);
            _paramsAddr["RSSI_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["RSSI_Panel"], 1);
            _paramsAddr["RSSI_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["RSSI_H_Position"], 70);
            _paramsAddr["RSSI_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["RSSI_V_Position"], 220);
            _paramsAddr["RSSI_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["RSSI_Font_Size"], 0);
            _paramsAddr["RSSI_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["RSSI_H_Alignment"], 0);
            _paramsAddr["RSSI_Min"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["RSSI_Min"], 0);
            _paramsAddr["RSSI_Max"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["RSSI_Max"], 255);
            _paramsAddr["RSSI_Raw_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["RSSI_Raw_Enable"], 0);
            
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

            data dataAtti = new PlayUAVOSD.data();
            dataAtti.paramname = "Attitude";
            dataAtti.desc = "飞行姿态";
            dataAtti.children.Add(genChildData(dataAtti.paramname, "MP_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Attitude_MP_Enable"]), "", "0, 1", "MissionPlanner地面站类似的界面,0:禁用, 1:启用"));
            dataAtti.children.Add(genChildData(dataAtti.paramname, "MP_Panel", getU16PanelString(eeprom, (int)_paramsAddr["Attitude_MP_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataAtti.children.Add(genChildData(dataAtti.paramname, "MP_Mode", getU16ParamString(eeprom, (int)_paramsAddr["Attitude_MP_Mode"]), "", "0, 1", "0:北约, 1:俄制"));
            dataAtti.children.Add(genChildData(dataAtti.paramname, "3D_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Attitude_3D_Enable"]), "", "0, 1", "3D界面,0:禁用, 1:启用"));
            dataAtti.children.Add(genChildData(dataAtti.paramname, "3D_Panel", getU16ParamString(eeprom, (int)_paramsAddr["Attitude_3D_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            roots.Add(dataAtti);

            data dataMisc = new PlayUAVOSD.data();
            dataMisc.paramname = "Misc";
            dataMisc.desc = "杂项";
            dataMisc.children.Add(genChildData(dataMisc.paramname, "Units_Mode", getU16ParamString(eeprom, (int)_paramsAddr["Misc_Units_Mode"]), "", "0, 1", "0:公制 1:英制"));
            dataMisc.children.Add(genChildData(dataMisc.paramname, "Max_Panels", getU16ParamString(eeprom, (int)_paramsAddr["Misc_Max_Panels"]), "", ">=1", "最大显示页面"));
            roots.Add(dataMisc);

            data dataPWM = new PlayUAVOSD.data();
            dataPWM.paramname = "PWM";
            dataPWM.desc = "切换视频，页面。以下只是参考值，根据自己的遥控器测试，设置";
            dataPWM.children.Add(genChildData(dataPWM.paramname, "Video_Enable", getU16ParamString(eeprom, (int)_paramsAddr["PWM_Video_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataPWM.children.Add(genChildData(dataPWM.paramname, "Video_Chanel", getU16ParamString(eeprom, (int)_paramsAddr["PWM_Video_Chanel"]), "", "1-8", "根据飞机类型及遥控，设置合适的通道"));
            dataPWM.children.Add(genChildData(dataPWM.paramname, "Video_Value", getU16ParamString(eeprom, (int)_paramsAddr["PWM_Video_Value"]), "", "", "当通道输出由低位大于这个值的时候，触发一次切换，默认1200适用于大多数遥控"));
            dataPWM.children.Add(genChildData(dataPWM.paramname, "Panel_Enable", getU16ParamString(eeprom, (int)_paramsAddr["PWM_Panel_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataPWM.children.Add(genChildData(dataPWM.paramname, "Panel_Chanel", getU16ParamString(eeprom, (int)_paramsAddr["PWM_Panel_Chanel"]), "", "1-8", "根据飞机类型及遥控，设置合适的通道"));
            dataPWM.children.Add(genChildData(dataPWM.paramname, "Panel_Value", getU16ParamString(eeprom, (int)_paramsAddr["PWM_Panel_Value"]), "", "", "当通道输出由低位大于这个值的时候，触发一次切换，默认1200适用于大多数遥控"));
            roots.Add(dataPWM);

            data dataArm = new PlayUAVOSD.data();
            dataArm.paramname = "ArmState";
            dataArm.desc = "解锁状态";
            dataArm.children.Add(genChildData(dataArm.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["ArmState_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataArm.children.Add(genChildData(dataArm.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["ArmState_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataArm.children.Add(genChildData(dataArm.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["ArmState_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataArm.children.Add(genChildData(dataArm.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["ArmState_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataArm.children.Add(genChildData(dataArm.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["ArmState_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataArm.children.Add(genChildData(dataArm.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["ArmState_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataArm);

            data dataBattVolt = new PlayUAVOSD.data();
            dataBattVolt.paramname = "BatteryVoltage";
            dataBattVolt.desc = "电池电压";
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["BatteryVoltage_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["BatteryVoltage_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["BatteryVoltage_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["BatteryVoltage_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["BatteryVoltage_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["BatteryVoltage_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataBattVolt);

            data dataBattCurrent = new PlayUAVOSD.data();
            dataBattCurrent.paramname = "BatteryCurrent";
            dataBattCurrent.desc = "电池电流";
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["BatteryCurrent_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["BatteryCurrent_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["BatteryCurrent_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["BatteryCurrent_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["BatteryCurrent_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["BatteryCurrent_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataBattCurrent);

            data dataBattConsumed = new PlayUAVOSD.data();
            dataBattConsumed.paramname = "BatteryConsumed";
            dataBattConsumed.desc = "已消耗电流，百分比";
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["BatteryConsumed_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["BatteryConsumed_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["BatteryConsumed_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["BatteryConsumed_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["BatteryConsumed_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["BatteryConsumed_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataBattConsumed);

            data dataFlightMode = new PlayUAVOSD.data();
            dataFlightMode.paramname = "FlightMode";
            dataFlightMode.desc = "飞行模式";
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["FlightMode_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["FlightMode_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["FlightMode_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["FlightMode_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["FlightMode_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["FlightMode_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataFlightMode);

            data dataGPSStatus = new PlayUAVOSD.data();
            dataGPSStatus.paramname = "GPSStatus";
            dataGPSStatus.desc = "GPS1 状态";
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPSStatus_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["GPSStatus_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPSStatus_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPSStatus_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPSStatus_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPSStatus_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPSStatus);

            data dataGPSHDOP = new PlayUAVOSD.data();
            dataGPSHDOP.paramname = "GPSHDOP";
            dataGPSHDOP.desc = "GPS1 水平精度";
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPSHDOP_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["GPSHDOP_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPSHDOP_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPSHDOP_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPSHDOP_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPSHDOP_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPSHDOP);

            data dataGPSLat = new PlayUAVOSD.data();
            dataGPSLat.paramname = "GPSLatitude";
            dataGPSLat.desc = "GPS1 纬度";
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPSLatitude_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["GPSLatitude_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPSLatitude_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPSLatitude_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPSLatitude_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPSLatitude_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPSLat);

            data dataGPSLon = new PlayUAVOSD.data();
            dataGPSLon.paramname = "GPSLongitude";
            dataGPSLon.desc = "GPS1 经度";
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPSLongitude_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["GPSLongitude_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPSLongitude_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPSLongitude_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPSLongitude_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPSLongitude_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPSLon);

            data dataGPS2Status = new PlayUAVOSD.data();
            dataGPS2Status.paramname = "GPS2Status";
            dataGPS2Status.desc = "GPS2 状态";
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Status_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["GPS2Status_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Status_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Status_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Status_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Status_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPS2Status);

            data dataGPS2HDOP = new PlayUAVOSD.data();
            dataGPS2HDOP.paramname = "GPS2HDOP";
            dataGPS2HDOP.desc = "GPS2 水平精度";
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS2HDOP_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["GPS2HDOP_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2HDOP_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2HDOP_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS2HDOP_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS2HDOP_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPS2HDOP);

            data dataGPS2Lat = new PlayUAVOSD.data();
            dataGPS2Lat.paramname = "GPS2Latitude";
            dataGPS2Lat.desc = "GPS2 纬度";
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Latitude_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["GPS2Latitude_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Latitude_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Latitude_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Latitude_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Latitude_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPS2Lat);

            data dataGPS2Lon = new PlayUAVOSD.data();
            dataGPS2Lon.paramname = "GPS2Longitude";
            dataGPS2Lon.desc = "GPS2 经度";
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Longitude_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["GPS2Longitude_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Longitude_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Longitude_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Longitude_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS2Longitude_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPS2Lon);

            data dataTime = new PlayUAVOSD.data();
            dataTime.paramname = "Time";
            dataTime.desc = "飞行时间";
            dataTime.children.Add(genChildData(dataTime.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Time_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataTime.children.Add(genChildData(dataTime.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["Time_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataTime.children.Add(genChildData(dataTime.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Time_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataTime.children.Add(genChildData(dataTime.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Time_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataTime.children.Add(genChildData(dataTime.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Time_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataTime.children.Add(genChildData(dataTime.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Time_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataTime);

            data dataAlt = new PlayUAVOSD.data();
            dataAlt.paramname = "Altitude";
            dataAlt.desc = "高度";
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_Enable"]), "", "0, 1", "Traditional hud. 0:禁用, 1:启用"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_Panel", getU16PanelString(eeprom, (int)_paramsAddr["Altitude_TALT_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Scale_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_Scale_Enable"]), "", "0, 1", "Scale hud. 0:禁用, 1:启用"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Scale_Panel", getU16PanelString(eeprom, (int)_paramsAddr["Altitude_Scale_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Scale_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_Scale_H_Position"]), "", "0 - 350", "水平位置"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Scale_Align", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_Scale_Align"]), "", "0, 1", "0:左 1:右"));
            roots.Add(dataAlt);

            data dataSpeed = new PlayUAVOSD.data();
            dataSpeed.paramname = "Speed";
            dataSpeed.desc = "速度";
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_Enable"]), "", "0, 1", "Traditional hud. 0:禁用, 1:启用"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_Panel", getU16PanelString(eeprom, (int)_paramsAddr["Speed_TSPD_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Scale_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Speed_Scale_Enable"]), "", "0, 1", "Scale hud. 0:禁用, 1:启用"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Scale_Panel", getU16PanelString(eeprom, (int)_paramsAddr["Speed_Scale_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Scale_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Speed_Scale_H_Position"]), "", "0 - 350", "水平位置"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Scale_Align", getU16ParamString(eeprom, (int)_paramsAddr["Speed_Scale_Align"]), "", "0, 1", "0:左 1:右"));
            roots.Add(dataSpeed);

            data dataThrottle = new PlayUAVOSD.data();
            dataThrottle.paramname = "Throttle";
            dataThrottle.desc = "油门";
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["Throttle_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "Scale_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_Scale_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            roots.Add(dataThrottle);

            data dataHomeDist = new PlayUAVOSD.data();
            dataHomeDist.paramname = "HomeDistance";
            dataHomeDist.desc = "家的距离";
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["HomeDistance_Enable"]), "", "0, 1", "是否显示家的距离0:否, 1:是"));
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["HomeDistance_Panel"]), "", "1 - Max_Panels", "家的距离显示在那个页面"));
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["HomeDistance_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["HomeDistance_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["HomeDistance_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["HomeDistance_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataHomeDist);

            data dataWPDist = new PlayUAVOSD.data();
            dataWPDist.paramname = "WPDistance";
            dataWPDist.desc = "航点的距离";
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["WPDistance_Enable"]), "", "0, 1", "是否显示航点距离0:否, 1:是"));
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["WPDistance_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["WPDistance_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["WPDistance_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["WPDistance_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["WPDistance_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataWPDist);

            data dataDir = new PlayUAVOSD.data();
            dataDir.paramname = "CHWDIR";
            dataDir.desc = "指南针，航点，家的方向的显示模式";
            dataDir.children.Add(genChildData(dataDir.paramname, "Tmode_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CHWDIR_Tmode_Enable"]), "", "0, 1", "是否显示传统样式。0:否, 1:是"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Tmode_Panel", getU16PanelString(eeprom, (int)_paramsAddr["CHWDIR_Tmode_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Tmode_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["CHWDIR_Tmode_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250 230为N制式最大，PAL制式可到250"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CHWDIR_Nmode_Enable"]), "", "0, 1", "是否显示动画样式。 0:禁用, 1:启用"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_Panel", getU16PanelString(eeprom, (int)_paramsAddr["CHWDIR_Nmode_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["CHWDIR_Nmode_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["CHWDIR_Nmode_V_Position"]), "像素", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_Radius", getU16ParamString(eeprom, (int)_paramsAddr["CHWDIR_Nmode_Radius"]), "像素", "0 - 230", "圆圈的半径"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_Home_Radius", getU16ParamString(eeprom, (int)_paramsAddr["CHWDIR_Nmode_Home_Radius"]), "像素", "0 - 230", "把家显示在离圆心多少距离的圆上"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_WP_Radius", getU16ParamString(eeprom, (int)_paramsAddr["CHWDIR_Nmode_WP_Radius"]), "像素", "0 - 230", "把航点显示在离圆心多少距离的圆上"));
            roots.Add(dataDir);

            
            data dataAlarm = new PlayUAVOSD.data();
            dataAlarm.paramname = "Alarm";
            dataAlarm.desc = "警告设置";
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_H_Position"]), "", "0 - 350", "水平位置"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_V_Position"]), "", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "GPS_Status_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_GPS_Status_Enable"]), "", "0, 1", "0:禁用, 1:启用 GPS未锁定报警"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Low_Batt_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Low_Batt_Enable"]), "", "0, 1", "0:禁用, 1:启用 电量过低报警"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Low_Batt", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Low_Batt"]), "", "0 - 99", "警戒值"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Under_Speed_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Under_Speed_Enable"]), "", "0, 1", "0:禁用, 1:启用 速度过低报警"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Under_Speed", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Under_Speed"]), "", "", "警戒值"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Over_Speed_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Over_Speed_Enable"]), "", "0, 1", "0:禁用, 1:启用 速度过高报警"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Over_Speed", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Over_Speed"]), "", "", "警戒值"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Under_Alt_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Under_Alt_Enable"]), "", "0, 1", "0:禁用, 1:启用 高度过低报警"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Under_Alt", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Under_Alt"]), "", "", "警戒值"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Over_Alt_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Over_Alt_Enable"]), "", "0, 1", "0:禁用, 1:启用 高度过高报警"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "Over_Alt", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_Over_Alt"]), "", "", "警戒值"));
            roots.Add(dataAlarm);

            data dataClimb = new PlayUAVOSD.data();
            dataClimb.paramname = "ClimbRate";
            dataClimb.desc = "爬升率，即垂直速度";
            dataClimb.children.Add(genChildData(dataClimb.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["ClimbRate_Enable"]), "", "0, 1", "是否显示爬升率 0:否, 1:是"));
            dataClimb.children.Add(genChildData(dataClimb.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["ClimbRate_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataClimb.children.Add(genChildData(dataClimb.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["ClimbRate_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataClimb.children.Add(genChildData(dataClimb.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["ClimbRate_V_Position"]), "", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250"));
            roots.Add(dataClimb);

            data dataRSSI = new PlayUAVOSD.data();
            dataRSSI.paramname = "RSSI";
            dataRSSI.desc = "首先MP里设置好，然后把Raw_Enable设为1.在OSD里观察打开，关闭遥控器得到的RSSI原始值，分别填到RSSI_Max, RSSI_Min,之后再把Raw_Enable设为0";
            dataRSSI.children.Add(genChildData(dataRSSI.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["RSSI_Enable"]), "", "0, 1", "是否显示爬升率 0:否, 1:是"));
            dataRSSI.children.Add(genChildData(dataRSSI.paramname, "Panel", getU16PanelString(eeprom, (int)_paramsAddr["RSSI_Panel"]), "", "1 - Max_Panels", "在哪个页面显示，多个页面以英文逗号隔开，比如1，3"));
            dataRSSI.children.Add(genChildData(dataRSSI.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["RSSI_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataRSSI.children.Add(genChildData(dataRSSI.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["RSSI_V_Position"]), "", "0 - 230", "垂直位置 230为N制式最大，PAL制式可到250"));
            dataRSSI.children.Add(genChildData(dataRSSI.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["RSSI_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataRSSI.children.Add(genChildData(dataRSSI.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["RSSI_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            dataRSSI.children.Add(genChildData(dataRSSI.paramname, "Min", getU16ParamString(eeprom, (int)_paramsAddr["RSSI_Min"]), "", "0-255", "启用RSSI原始值的时候，关掉遥控器，屏幕上显示的值填到这里"));
            dataRSSI.children.Add(genChildData(dataRSSI.paramname, "Max", getU16ParamString(eeprom, (int)_paramsAddr["RSSI_Max"]), "", "RSSI_Min-255", "启用RSSI原始值的时候，打开遥控器，屏幕上显示的值填到这里"));
            dataRSSI.children.Add(genChildData(dataRSSI.paramname, "Raw_Enable", getU16ParamString(eeprom, (int)_paramsAddr["RSSI_Raw_Enable"]), "", "0, 1", "0：显示的是百分比，1：显示的是RSSI原始值"));
            roots.Add(dataRSSI);

            foreach (var item in roots)
            {
                // if the child has no children, we dont need the root.
                if (((List<data>)item.children).Count == 1)
                {
                    Params.AddObject(((List<data>)item.children)[0]);
                    continue;
                }
                else
                {

                }

                Params.AddObject(item);
            }
        }

        private void btn_Load_Default_Click(object sender, EventArgs e)
        {
            
            //eeprom = paramdefault;
            paramdefault.CopyTo(eeprom, 0);

            //System.IO.MemoryStream stream = new System.IO.MemoryStream();
            //stream.Write(paramdefault, 0, paramdefault.Length);
            //stream.Close();
            //eeprom = stream.ToArray(); 

            //for (int i = 0; i < paramdefault.Length; i++)
            //{
            //    eeprom[i] = paramdefault[i];
            //}

            processToScreen();
        }

        private void btn_save_file_Click(object sender, EventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = ".posd",
                RestoreDirectory = true,
                Filter = "OSD Param List|*.posd"
            };

            var dr = sfd.ShowDialog();
            if (dr == DialogResult.OK)
            {
                Hashtable data = new Hashtable();
                string fullparamname;
                bool bPanelValue = false;
                foreach (data row in Params.Objects)
                {

                    foreach (var item in row.children)
                    {
                        if (item.Value != null)
                        {
                            float value;
                            fullparamname = row.paramname + "_" + item.paramname.ToString();
                            bPanelValue = false;
                            if (fullparamname.Contains("_Panel") && !(fullparamname.Contains("PWM")) && !(fullparamname.Contains("Max_Panels")))
                                bPanelValue = true;

                            if (bPanelValue)
                            {
                                value = panelValStr2F(item.Value.ToString());
                            }
                            else
                            {
                                value = float.Parse(item.Value.ToString());
                            }
                            data[fullparamname] = value;
                        }
                    }

                    if (row.Value != null)
                    {
                        float value = float.Parse(row.Value.ToString());

                        data[row.paramname.ToString()] = value;
                    }
                }

                Utilities.ParamFile.SaveParamFile(sfd.FileName, data);

            }
        }

        private void btn_load_file_Click(object sender, EventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                AddExtension = true,
                DefaultExt = ".posd",
                RestoreDirectory = true,
                Filter = "OSD Param List|*.posd"
            };
            var dr = ofd.ShowDialog();

            if (dr == DialogResult.OK)
            {
                loadparamsfromfile(ofd.FileName);
            }
        }

        void loadparamsfromfile(string fn)
        {
            Hashtable param2 = Utilities.ParamFile.loadParamFile(fn);

            foreach (string name in param2.Keys)
            {
                string value = param2[name].ToString();

                checkandupdateparam(name, value);
            }
        }

        void checkandupdateparam(string name, string value)
        {
            //if (name == "SYSID_SW_MREV")
            //    return;
            //if (name == "WP_TOTAL")
            //    return;
            //if (name == "CMD_TOTAL")
            //    return;
            //if (name == "FENCE_TOTAL")
            //    return;
            //if (name == "SYS_NUM_RESETS")
            //    return;
            //if (name == "ARSPD_OFFSET")
            //    return;
            //if (name == "GND_ABS_PRESS")
            //    return;
            //if (name == "GND_TEMP")
            //    return;
            //if (name == "CMD_INDEX")
            //    return;
            //if (name == "LOG_LASTFILE")
            //    return;
            //if (name == "FORMAT_VERSION")
            //    return;

            paramCompareForm_dtlvcallback(name, float.Parse(value));
        }

        void paramCompareForm_dtlvcallback(string param, float value)
        {
            string strParent = "";
            string strChild = "";
            bool bPanelValue = false;
            int nPos = param.IndexOf('_');
            if(nPos!=0)
            {
                strParent = param.Substring(0,nPos);
                strChild = param.Substring(nPos+1);
            }

            foreach (data item in Params.Objects)
            {
                if (item.paramname == strParent)
                {
                    foreach (data item2 in item.children)
                    {
                        if (item2.paramname == strChild)
                        {
                            if (param.Contains("_Panel") && !(param.Contains("PWM")) && !(param.Contains("Max_Panels")))
                                bPanelValue = true;

                            if (bPanelValue)
                            {
                                item2.Value = panelValF2Str((int)value);
                            }
                            else
                            {
                                item2.Value = value.ToString();
                            }

                            _changes[param] = value;
                            Params.RefreshObject(item2);
                            Params.Expand(item2);
                            u16toEPPROM(eeprom, (int)_paramsAddr[param], (short)value);
                            break;
                        }
                    }
                }
                
            }
        }

        private void Sav_To_EEPROM_MouseEnter(object sender, EventArgs e)
        {
            ToolTip p = new ToolTip();
            p.ShowAlways = true;
            p.SetToolTip(this.Sav_To_EEPROM, "把参数保存到芯片的FLASH里，断电后不会丢失");
        }

        private void Save_To_OSD_MouseEnter(object sender, EventArgs e)
        {
            ToolTip p = new ToolTip();
            p.ShowAlways = true;
            p.SetToolTip(this.Save_To_OSD, "把参数暂时保存到板子上，避免多次擦写FLASH，断电会丢失");
        }
    }
}