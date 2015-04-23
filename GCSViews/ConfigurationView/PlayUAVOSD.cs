﻿using System;
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
            _paramsAddr["Arm_State_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Arm_State_Panel"], 1);
            _paramsAddr["Arm_State_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Arm_State_H_Position"], 350);
            _paramsAddr["Arm_State_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Arm_State_V_Position"], 44);
            _paramsAddr["Arm_State_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Arm_State_Font_Size"], 0);
            _paramsAddr["Arm_State_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Arm_State_H_Alignment"], 2);

            _paramsAddr["Battery_Voltage_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Voltage_Enable"], 1);
            _paramsAddr["Battery_Voltage_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Voltage_Panel"], 1);
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
            _paramsAddr["Battery_Current_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Current_Panel"], 1);
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
            _paramsAddr["Battery_Consumed_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Battery_Consumed_Panel"], 1);
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
            _paramsAddr["Flight_Mode_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Flight_Mode_Panel"], 1);
            _paramsAddr["Flight_Mode_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Flight_Mode_H_Position"], 350);
            _paramsAddr["Flight_Mode_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Flight_Mode_V_Position"], 34);
            _paramsAddr["Flight_Mode_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Flight_Mode_Font_Size"], 0);
            _paramsAddr["Flight_Mode_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Flight_Mode_H_Alignment"], 2);

            _paramsAddr["GPS_Status_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_Enable"], 1);
            _paramsAddr["GPS_Status_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_Panel"], 1);
            _paramsAddr["GPS_Status_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_H_Position"], 0);
            _paramsAddr["GPS_Status_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_V_Position"], 230);
            _paramsAddr["GPS_Status_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_Font_Size"], 0);
            _paramsAddr["GPS_Status_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Status_H_Alignment"], 0);

            _paramsAddr["GPS_HDOP_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_HDOP_Enable"], 1);
            _paramsAddr["GPS_HDOP_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_HDOP_Panel"], 1);
            _paramsAddr["GPS_HDOP_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_HDOP_H_Position"], 50);
            _paramsAddr["GPS_HDOP_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_HDOP_V_Position"], 230);
            _paramsAddr["GPS_HDOP_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_HDOP_Font_Size"], 0);
            _paramsAddr["GPS_HDOP_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_HDOP_H_Alignment"], 0);

            _paramsAddr["GPS_Latitude_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Latitude_Enable"], 1);
            _paramsAddr["GPS_Latitude_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Latitude_Panel"], 1);
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
            _paramsAddr["GPS_Longitude_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Longitude_Panel"], 1);
            _paramsAddr["GPS_Longitude_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Longitude_H_Position"], 280);
            _paramsAddr["GPS_Longitude_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Longitude_V_Position"], 230);
            _paramsAddr["GPS_Longitude_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Longitude_Font_Size"], 0);
            _paramsAddr["GPS_Longitude_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS_Longitude_H_Alignment"], 0);

            _paramsAddr["GPS2_Status_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Status_Enable"], 1);
            _paramsAddr["GPS2_Status_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Status_Panel"], 2);
            _paramsAddr["GPS2_Status_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Status_H_Position"], 0);
            _paramsAddr["GPS2_Status_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Status_V_Position"], 230);
            _paramsAddr["GPS2_Status_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Status_Font_Size"], 0);
            _paramsAddr["GPS2_Status_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Status_H_Alignment"], 0);

            _paramsAddr["GPS2_HDOP_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_HDOP_Enable"], 1);
            _paramsAddr["GPS2_HDOP_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_HDOP_Panel"], 2);
            _paramsAddr["GPS2_HDOP_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_HDOP_H_Position"], 50);
            _paramsAddr["GPS2_HDOP_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_HDOP_V_Position"], 230);
            _paramsAddr["GPS2_HDOP_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_HDOP_Font_Size"], 0);
            _paramsAddr["GPS2_HDOP_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_HDOP_H_Alignment"], 0);

            _paramsAddr["GPS2_Latitude_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Latitude_Enable"], 1);
            _paramsAddr["GPS2_Latitude_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Latitude_Panel"], 2);
            _paramsAddr["GPS2_Latitude_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Latitude_H_Position"], 200);
            _paramsAddr["GPS2_Latitude_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Latitude_V_Position"], 230);
            _paramsAddr["GPS2_Latitude_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Latitude_Font_Size"], 0);
            _paramsAddr["GPS2_Latitude_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Latitude_H_Alignment"], 0);

            _paramsAddr["GPS2_Longitude_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Longitude_Enable"], 1);
            _paramsAddr["GPS2_Longitude_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Longitude_Panel"], 2);
            _paramsAddr["GPS2_Longitude_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Longitude_H_Position"], 280);
            _paramsAddr["GPS2_Longitude_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Longitude_V_Position"], 230);
            _paramsAddr["GPS2_Longitude_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Longitude_Font_Size"], 0);
            _paramsAddr["GPS2_Longitude_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["GPS2_Longitude_H_Alignment"], 0);

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
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_H_Position"], 25);
            _paramsAddr["Throttle_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Throttle_V_Position"], 210);
            
            //home distance
            _paramsAddr["Home_Distance_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Home_Distance_Enable"], 1);
            _paramsAddr["Home_Distance_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Home_Distance_Panel"], 1);
            _paramsAddr["Home_Distance_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Home_Distance_H_Position"], 70);
            _paramsAddr["Home_Distance_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Home_Distance_V_Position"], 14);
            _paramsAddr["Home_Distance_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Home_Distance_Font_Size"], 0);
            _paramsAddr["Home_Distance_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Home_Distance_H_Alignment"], 0);

            //way-point distance
            _paramsAddr["WP_Distance_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WP_Distance_Enable"], 1);
            _paramsAddr["WP_Distance_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WP_Distance_Panel"], 1);
            _paramsAddr["WP_Distance_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WP_Distance_H_Position"], 70);
            _paramsAddr["WP_Distance_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WP_Distance_V_Position"], 24);
            _paramsAddr["WP_Distance_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WP_Distance_Font_Size"], 0);
            _paramsAddr["WP_Distance_H_Alignment"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["WP_Distance_H_Alignment"], 0);

            //heading, home and wp direction
            _paramsAddr["CHW_DIR_Tmode_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHW_DIR_Tmode_Enable"], 1);
            _paramsAddr["CHW_DIR_Tmode_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHW_DIR_Tmode_Panel"], 2);
            _paramsAddr["CHW_DIR_Tmode_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHW_DIR_Tmode_V_Position"], 15);
            _paramsAddr["CHW_DIR_Nmode_Enable"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHW_DIR_Nmode_Enable"], 1);
            _paramsAddr["CHW_DIR_Nmode_Panel"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHW_DIR_Nmode_Panel"], 1);
            _paramsAddr["CHW_DIR_Nmode_H_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHW_DIR_Nmode_H_Position"], 30);
            _paramsAddr["CHW_DIR_Nmode_V_Position"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHW_DIR_Nmode_V_Position"], 35);
            _paramsAddr["CHW_DIR_Nmode_Radius"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHW_DIR_Nmode_Radius"], 20);
            _paramsAddr["CHW_DIR_Nmode_Home_Radius"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHW_DIR_Nmode_Home_Radius"], 25);
            _paramsAddr["CHW_DIR_Nmode_WP_Radius"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["CHW_DIR_Nmode_WP_Radius"], 25);

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
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_V_Position"], 30);
            _paramsAddr["Alarm_Font_Size"] = address; address += 2;
            u16toEPPROM(paramdefault, (int)_paramsAddr["Alarm_Font_Size"], 1);
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
            dataAtti.children.Add(genChildData(dataAtti.paramname, "MP_Panel", getU16ParamString(eeprom, (int)_paramsAddr["Attitude_MP_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataAtti.children.Add(genChildData(dataAtti.paramname, "MP_Mode", getU16ParamString(eeprom, (int)_paramsAddr["Attitude_MP_Mode"]), "", "0, 1", "0:北约, 1:俄制"));
            dataAtti.children.Add(genChildData(dataAtti.paramname, "3D_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Attitude_3D_Enable"]), "", "0, 1", "3D界面,0:禁用, 1:启用"));
            dataAtti.children.Add(genChildData(dataAtti.paramname, "3D_Panel", getU16ParamString(eeprom, (int)_paramsAddr["Attitude_3D_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
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
            dataArm.paramname = "Arm_State";
            dataArm.desc = "解锁状态";
            dataArm.children.Add(genChildData(dataArm.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataArm.children.Add(genChildData(dataArm.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataArm.children.Add(genChildData(dataArm.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataArm.children.Add(genChildData(dataArm.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataArm.children.Add(genChildData(dataArm.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataArm.children.Add(genChildData(dataArm.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Arm_State_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataArm);

            data dataBattVolt = new PlayUAVOSD.data();
            dataBattVolt.paramname = "Battery_Voltage";
            dataBattVolt.desc = "电池电压";
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataBattVolt.children.Add(genChildData(dataBattVolt.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Voltage_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataBattVolt);

            data dataBattCurrent = new PlayUAVOSD.data();
            dataBattCurrent.paramname = "Battery_Current";
            dataBattCurrent.desc = "电池电流";
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataBattCurrent.children.Add(genChildData(dataBattCurrent.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Current_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataBattCurrent);

            data dataBattConsumed = new PlayUAVOSD.data();
            dataBattConsumed.paramname = "Battery_Consumed";
            dataBattConsumed.desc = "已消耗电流，百分比";
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataBattConsumed.children.Add(genChildData(dataBattConsumed.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Battery_Consumed_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataBattConsumed);

            data dataFlightMode = new PlayUAVOSD.data();
            dataFlightMode.paramname = "Flight_Mode";
            dataFlightMode.desc = "飞行模式";
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataFlightMode.children.Add(genChildData(dataFlightMode.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Flight_Mode_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataFlightMode);

            data dataGPSStatus = new PlayUAVOSD.data();
            dataGPSStatus.paramname = "GPS_Status";
            dataGPSStatus.desc = "GPS1 状态";
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPSStatus.children.Add(genChildData(dataGPSStatus.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Status_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPSStatus);

            data dataGPSHDOP = new PlayUAVOSD.data();
            dataGPSHDOP.paramname = "GPS_HDOP";
            dataGPSHDOP.desc = "GPS1 水平精度";
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS_HDOP_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["GPS_HDOP_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_HDOP_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_HDOP_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS_HDOP_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPSHDOP.children.Add(genChildData(dataGPSHDOP.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS_HDOP_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPSHDOP);

            data dataGPSLat = new PlayUAVOSD.data();
            dataGPSLat.paramname = "GPS_Latitude";
            dataGPSLat.desc = "GPS1 纬度";
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPSLat.children.Add(genChildData(dataGPSLat.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Latitude_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPSLat);

            data dataGPSLon = new PlayUAVOSD.data();
            dataGPSLon.paramname = "GPS_Longitude";
            dataGPSLon.desc = "GPS1 经度";
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPSLon.children.Add(genChildData(dataGPSLon.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS_Longitude_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPSLon);

            data dataGPS2Status = new PlayUAVOSD.data();
            dataGPS2Status.paramname = "GPS2_Status";
            dataGPS2Status.desc = "GPS2 状态";
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Status_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Status_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Status_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Status_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Status_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPS2Status.children.Add(genChildData(dataGPS2Status.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Status_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPS2Status);

            data dataGPS2HDOP = new PlayUAVOSD.data();
            dataGPS2HDOP.paramname = "GPS2_HDOP";
            dataGPS2HDOP.desc = "GPS2 水平精度";
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_HDOP_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_HDOP_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_HDOP_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_HDOP_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_HDOP_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPS2HDOP.children.Add(genChildData(dataGPS2HDOP.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_HDOP_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPS2HDOP);

            data dataGPS2Lat = new PlayUAVOSD.data();
            dataGPS2Lat.paramname = "GPS2_Latitude";
            dataGPS2Lat.desc = "GPS2 纬度";
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Latitude_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Latitude_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Latitude_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Latitude_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Latitude_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPS2Lat.children.Add(genChildData(dataGPS2Lat.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Latitude_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPS2Lat);

            data dataGPS2Lon = new PlayUAVOSD.data();
            dataGPS2Lon.paramname = "GPS2_Longitude";
            dataGPS2Lon.desc = "GPS2 经度";
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Longitude_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Longitude_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Longitude_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Longitude_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Longitude_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataGPS2Lon.children.Add(genChildData(dataGPS2Lon.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["GPS2_Longitude_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataGPS2Lon);

            data dataTime = new PlayUAVOSD.data();
            dataTime.paramname = "Time";
            dataTime.desc = "飞行时间";
            dataTime.children.Add(genChildData(dataTime.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Time_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataTime.children.Add(genChildData(dataTime.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["Time_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataTime.children.Add(genChildData(dataTime.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Time_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataTime.children.Add(genChildData(dataTime.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Time_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataTime.children.Add(genChildData(dataTime.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Time_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataTime.children.Add(genChildData(dataTime.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Time_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataTime);

            data dataAlt = new PlayUAVOSD.data();
            dataAlt.paramname = "Altitude";
            dataAlt.desc = "高度";
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_Enable"]), "", "0, 1", "Traditional hud. 0:禁用, 1:启用"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_Panel", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "TALT_H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_TALT_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Scale_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_Scale_Enable"]), "", "0, 1", "Scale hud. 0:禁用, 1:启用"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Scale_Panel", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_Scale_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Scale_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_Scale_H_Position"]), "", "0 - 350", "水平位置"));
            dataAlt.children.Add(genChildData(dataAlt.paramname, "Scale_Align", getU16ParamString(eeprom, (int)_paramsAddr["Altitude_Scale_Align"]), "", "0, 1", "0:左 1:右"));
            roots.Add(dataAlt);

            data dataSpeed = new PlayUAVOSD.data();
            dataSpeed.paramname = "Speed";
            dataSpeed.desc = "速度";
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_Enable"]), "", "0, 1", "Traditional hud. 0:禁用, 1:启用"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_Panel", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "TSPD_H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Speed_TSPD_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Scale_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Speed_Scale_Enable"]), "", "0, 1", "Scale hud. 0:禁用, 1:启用"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Scale_Panel", getU16ParamString(eeprom, (int)_paramsAddr["Speed_Scale_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Scale_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Speed_Scale_H_Position"]), "", "0 - 350", "水平位置"));
            dataSpeed.children.Add(genChildData(dataSpeed.paramname, "Scale_Align", getU16ParamString(eeprom, (int)_paramsAddr["Speed_Scale_Align"]), "", "0, 1", "0:左 1:右"));
            roots.Add(dataSpeed);

            data dataThrottle = new PlayUAVOSD.data();
            dataThrottle.paramname = "Throttle";
            dataThrottle.desc = "油门";
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "Scale_Enable", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_Scale_Enable"]), "", "0, 1", "0:禁用, 1:启用"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataThrottle.children.Add(genChildData(dataThrottle.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Throttle_V_Position"]), "像素", "0 - 230", "垂直位置"));
            roots.Add(dataThrottle);

            data dataHomeDist = new PlayUAVOSD.data();
            dataHomeDist.paramname = "Home_Distance";
            dataHomeDist.desc = "家的距离";
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["Home_Distance_Enable"]), "", "0, 1", "是否显示家的距离0:否, 1:是"));
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["Home_Distance_Panel"]), "", "1 - Max_Panels", "家的距离显示在那个页面"));
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Home_Distance_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Home_Distance_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["Home_Distance_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataHomeDist.children.Add(genChildData(dataHomeDist.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["Home_Distance_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataHomeDist);

            data dataWPDist = new PlayUAVOSD.data();
            dataWPDist.paramname = "WP_Distance";
            dataWPDist.desc = "航点的距离";
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "Enable", getU16ParamString(eeprom, (int)_paramsAddr["WP_Distance_Enable"]), "", "0, 1", "是否显示航点距离0:否, 1:是"));
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "Panel", getU16ParamString(eeprom, (int)_paramsAddr["WP_Distance_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["WP_Distance_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["WP_Distance_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "Font_Size", getU16ParamString(eeprom, (int)_paramsAddr["WP_Distance_Font_Size"]), "", "0, 1, 2", "0:小号, 1:正常, 2:大号"));
            dataWPDist.children.Add(genChildData(dataWPDist.paramname, "H_Alignment", getU16ParamString(eeprom, (int)_paramsAddr["WP_Distance_H_Alignment"]), "", "0, 1, 2", "0:左对齐,  1:居中, 2:右对齐"));
            roots.Add(dataWPDist);

            data dataDir = new PlayUAVOSD.data();
            dataDir.paramname = "CHW_DIR";
            dataDir.desc = "指南针，航点，家的方向的显示模式";
            dataDir.children.Add(genChildData(dataDir.paramname, "Tmode_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CHW_DIR_Tmode_Enable"]), "", "0, 1", "是否显示传统样式。0:否, 1:是"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Tmode_Panel", getU16ParamString(eeprom, (int)_paramsAddr["CHW_DIR_Tmode_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Tmode_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["CHW_DIR_Tmode_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_Enable", getU16ParamString(eeprom, (int)_paramsAddr["CHW_DIR_Nmode_Enable"]), "", "0, 1", "是否显示动画样式。 0:禁用, 1:启用"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_Panel", getU16ParamString(eeprom, (int)_paramsAddr["CHW_DIR_Nmode_Panel"]), "", "1 - Max_Panels", "在哪个页面显示"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_H_Position", getU16ParamString(eeprom, (int)_paramsAddr["CHW_DIR_Nmode_H_Position"]), "像素", "0 - 350", "水平位置"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_V_Position", getU16ParamString(eeprom, (int)_paramsAddr["CHW_DIR_Nmode_V_Position"]), "像素", "0 - 230", "垂直位置"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_Radius", getU16ParamString(eeprom, (int)_paramsAddr["CHW_DIR_Nmode_Radius"]), "像素", "0 - 230", "圆圈的半径"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_Home_Radius", getU16ParamString(eeprom, (int)_paramsAddr["CHW_DIR_Nmode_Home_Radius"]), "像素", "0 - 230", "把家显示在离圆心多少距离的圆上"));
            dataDir.children.Add(genChildData(dataDir.paramname, "Nmode_WP_Radius", getU16ParamString(eeprom, (int)_paramsAddr["CHW_DIR_Nmode_WP_Radius"]), "像素", "0 - 230", "把航点显示在离圆心多少距离的圆上"));
            roots.Add(dataDir);

            
            data dataAlarm = new PlayUAVOSD.data();
            dataAlarm.paramname = "Alarm";
            dataAlarm.desc = "警告设置";
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "H_Position", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_H_Position"]), "", "0 - 350", "水平位置"));
            dataAlarm.children.Add(genChildData(dataAlarm.paramname, "V_Position", getU16ParamString(eeprom, (int)_paramsAddr["Alarm_V_Position"]), "", "0 - 230", "垂直位置"));
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

        
    }
}