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
        byte[] eeprom = new byte[1024];
        PlayUAVOSD self;
        
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

        public class optosd
        {
            public int en;         //0:disabled, 1:enabled
            public short posX;
            public short posY;
            public int fontsize;   //0:small, 1:normal, 2:large
            public int align;      //0:left,  1:center, 2:right
        }

        optosd panAlarm     = new optosd();
        optosd panArm       = new optosd();
        optosd panBattVolt  = new optosd();


        /* *********************************************** */
        // Version number, incrementing this will erase/upload factory settings.
        // Only devs should increment this
        const int VER = 1;

        // setting address in epprom
        
        const int panAlarm_en_ADDR = 0;
        const int panAlarm_posX_ADDR = 2;
        const int panAlarm_posY_ADDR = 4;
        const int panAlarm_fontsize_ADDR = 6;
        const int panAlarm_align_ADDR = 8;

        const int panArm_en_ADDR = 10;
        const int panArm_posX_ADDR = 12;
        const int panArm_posY_ADDR = 14;
        const int panArm_fontsize_ADDR = 16;
        const int panArm_align_ADDR = 18;

        const int panBattVolt_en_ADDR = 20;
        const int panBattVolt_posX_ADDR = 22;
        const int panBattVolt_posY_ADDR = 24;
        const int panBattVolt_fontsize_ADDR = 26;
        const int panBattVolt_align_ADDR = 28;

        const int panBattCurrent_en_ADDR = 30;
        const int panBattCurrent_posX_ADDR = 32;
        const int panBattCurrent_posY_ADDR = 34;
        const int panBattCurrent_fontsize_ADDR = 36;
        const int panBattCurrent_align_ADDR = 38;

        const int panBattConsumed_en_ADDR = 40;
        const int panBattConsumed_posX_ADDR = 42;
        const int panBattConsumed_posY_ADDR = 44;
        const int panBattConsumed_fontsize_ADDR = 46;
        const int panBattConsumed_align_ADDR = 48;

        const int panFlightMode_en_ADDR = 50;
        const int panFlightMode_posX_ADDR = 52;
        const int panFlightMode_posY_ADDR = 54;
        const int panFlightMode_fontsize_ADDR = 56;
        const int panFlightMode_align_ADDR = 58;


        const int panGpsStatus_en_ADDR = 60;
        const int panGpsStatus_posX_ADDR = 62;
        const int panGpsStatus_posY_ADDR = 64;
        const int panGpsStatus_fontsize_ADDR = 66;
        const int panGpsStatus_align_ADDR = 68;

        const int panGpsLat_en_ADDR = 70;
        const int panGpsLat_posX_ADDR = 72;
        const int panGpsLat_posY_ADDR = 74;
        const int panGpsLat_fontsize_ADDR = 76;
        const int panGpsLat_align_ADDR = 78;

        const int panGpsLon_en_ADDR = 80;
        const int panGpsLon_posX_ADDR = 82;
        const int panGpsLon_posY_ADDR = 84;
        const int panGpsLon_fontsize_ADDR = 86;
        const int panGpsLon_align_ADDR = 88;

        const int panTime_en_ADDR = 90;
        const int panTime_posX_ADDR = 92;
        const int panTime_posY_ADDR = 94;
        const int panTime_fontsize_ADDR = 96;
        const int panTime_align_ADDR = 98;

        const int panHome_en_ADDR = 100;
        const int panHome_mode_ADDR = 102;      //0:fix position 1:dynamic pos
        const int panHome_fix_posX_ADDR = 104;
        const int panHome_fix_posY_ADDR = 106;
        const int panHome_dynamic_radius_ADDR = 108;    //distance from the screen center under dynamic mode

        const int panWP_en_ADDR = 110;
        const int panWP_mode_ADDR = 112;      //0:fix position 1:dynamic pos
        const int panWP_fix_posX_ADDR = 114;
        const int panWP_fix_posY_ADDR = 116;
        const int panWP_dynamic_radius_ADDR = 118;    //distance from the screen center under dynamic mode

        //RSSI - not implement yet!
        const int panRSSI_en_ADDR = 120;
        const int panRSSI_posX_ADDR = 122;
        const int panRSSI_posY_ADDR = 124;
        const int panRSSI_fontsize_ADDR = 126;
        const int panRSSI_align_ADDR = 128;
        const int panRSSI_minValue_ADDR = 130;
        const int panRSSI_maxValue_ADDR = 132;
        const int panRSSI_warnValue_ADDR = 134;
        const int panRSSI_ifRaw_ADDR = 136;

        //altitude scale, vertical position always in middle
        const int panAltScale_en_ADDR = 138;
        const int panAltScale_posX_ADDR = 140;
        const int panAltScale_align_ADDR = 142;     //0:left 1:right
        const int panAltScale_src_ADDR = 144;     //which altitude will be shown, baro or other? not implement yet!

        //Speed scale, vertical position always in middle
        const int panSpeedScale_en_ADDR = 146;
        const int panSpeedScale_posX_ADDR = 148;
        const int panSpeedScale_align_ADDR = 150;     //0:left 1:right
        const int panSpeedScale_src_ADDR = 152;     //which speed will be shown, GPS or AirSpeed or other? not implement yet!

        //Attitude
        const int panAttitude_mode_ADDR = 154;      //0:2D  1:3D
        const int panAttitude_Russia_mode_ADDR = 156;   //if use Russia mode
        const int panAttitude_2D_compass_en_ADDR = 158;
        const int panAttitude_2D_compass_posX_ADDR = 160;     //discard, always stay at screen middle
        const int panAttitude_2D_compass_posY_ADDR = 162;
        const int panAttitude_2D_roll_scale_en_ADDR = 164;
        const int panAttitude_2D_roll_scale_radius_ADDR = 166;      //radius from the center
        const int panAttitude_2D_roll_value_en_ADDR = 168;    //if show the triangle and roll value
        const int panAttitude_2D_pitch_scale_en_ADDR = 170;
        const int panAttitude_2D_pitch_scale_Vstep_ADDR = 172;  //the distance between each scale line
        const int panAttitude_2D_pitch_scale_Hlen_ADDR = 174;   //the length of scale line
        const int panAttitude_2D_horizontal_len_ADDR = 176;   //the length of horizontal line
        const int panAttitude_2D_pitch_value_en_ADDR = 178;   //if show the pitch value under the uav
        const int panAttitude_3D_cam_posX_ADDR = 180;   //Not Implement Yet! - camera's pos and dir
        const int panAttitude_3D_cam_posY_ADDR = 182;
        const int panAttitude_3D_cam_posZ_ADDR = 184;
        const int panAttitude_3D_cam_dirX_ADDR = 186;
        const int panAttitude_3D_cam_dirY_ADDR = 188;
        const int panAttitude_3D_cam_dirZ_ADDR = 190;

        const int panUnits_mode_ADDR = 192;         //0:metric   1:imperial

        const int panWarn_minSpeed_ADDR = 194;
        const int panWarn_maxSpeed_ADDR = 196;
        const int panWarn_minAlt_ADDR = 198;
        const int panWarn_maxAlt_ADDR = 200;
        const int panWarn_minBattVol_ADDR = 202;        //float value should 32 bits
        const int panWarn_minBattPercent_ADDR = 206;

        public void Activate()
        {
            
            Params.Rows[Params.RowCount - 1].Cells[0].Value = "X";
            Params.Rows[Params.RowCount - 1].Cells[1].Value = (float)1;
            Params.Rows.Add();

            Params.Rows[Params.RowCount - 1].Cells[0].Value = "Y";
            Params.Rows[Params.RowCount - 1].Cells[1].Value = (float)2;
       //     Params.Rows.Add();
           
        }

        public void u16toEPPROM(int addr, short val)
        {
            eeprom[addr] = (byte)(val & 0xFF);
            eeprom[addr + 1] = (byte)((val >> 8) & 0xFF);
        }
        public PlayUAVOSD()
        {
            self = this;
            InitializeComponent();
            comPort = new MissionPlanner.Comms.SerialPort();

            u16toEPPROM(panAlarm_en_ADDR, 1);
            u16toEPPROM(panAlarm_posX_ADDR, 0);
            u16toEPPROM(panAlarm_posY_ADDR, 220);
            u16toEPPROM(panAlarm_fontsize_ADDR, 0);
            u16toEPPROM(panAlarm_align_ADDR, 0);

            u16toEPPROM(panArm_en_ADDR, 1);
            u16toEPPROM(panArm_posX_ADDR, 0);
            u16toEPPROM(panArm_posY_ADDR, 14);
            u16toEPPROM(panArm_fontsize_ADDR, 0);
            u16toEPPROM(panArm_align_ADDR, 0);

            u16toEPPROM(panBattVolt_en_ADDR, 1);
            u16toEPPROM(panBattVolt_posX_ADDR, 350);
            u16toEPPROM(panBattVolt_posY_ADDR, 4);
            u16toEPPROM(panBattVolt_fontsize_ADDR, 0);
            u16toEPPROM(panBattVolt_align_ADDR, 2);

            u16toEPPROM(panBattCurrent_en_ADDR, 1);
            u16toEPPROM(panBattCurrent_posX_ADDR, 350);
            u16toEPPROM(panBattCurrent_posY_ADDR, 14);
            u16toEPPROM(panBattCurrent_fontsize_ADDR, 0);
            u16toEPPROM(panBattCurrent_align_ADDR, 2);

            u16toEPPROM(panBattConsumed_en_ADDR, 1);
            u16toEPPROM(panBattConsumed_posX_ADDR, 350);
            u16toEPPROM(panBattConsumed_posY_ADDR, 24);
            u16toEPPROM(panBattConsumed_fontsize_ADDR, 0);
            u16toEPPROM(panBattConsumed_align_ADDR, 2);

            u16toEPPROM(panFlightMode_en_ADDR, 1);
            u16toEPPROM(panFlightMode_posX_ADDR, 0);
            u16toEPPROM(panFlightMode_posY_ADDR, 4);
            u16toEPPROM(panFlightMode_fontsize_ADDR, 0);
            u16toEPPROM(panFlightMode_align_ADDR, 0);

            u16toEPPROM(panGpsStatus_en_ADDR, 1);
            u16toEPPROM(panGpsStatus_posX_ADDR, 0);
            u16toEPPROM(panGpsStatus_posY_ADDR, 230);
            u16toEPPROM(panGpsStatus_fontsize_ADDR, 0);
            u16toEPPROM(panGpsStatus_align_ADDR, 0);

            u16toEPPROM(panGpsLat_en_ADDR, 1);
            u16toEPPROM(panGpsLat_posX_ADDR, 200);
            u16toEPPROM(panGpsLat_posY_ADDR, 230);
            u16toEPPROM(panGpsLat_fontsize_ADDR, 0);
            u16toEPPROM(panGpsLat_align_ADDR, 0);

            u16toEPPROM(panGpsLon_en_ADDR, 1);
            u16toEPPROM(panGpsLon_posX_ADDR, 300);
            u16toEPPROM(panGpsLon_posY_ADDR, 230);
            u16toEPPROM(panGpsLon_fontsize_ADDR, 0);
            u16toEPPROM(panGpsLon_align_ADDR, 2);

            u16toEPPROM(panTime_en_ADDR, 1);
            u16toEPPROM(panTime_posX_ADDR, 350);
            u16toEPPROM(panTime_posY_ADDR, 220);
            u16toEPPROM(panTime_fontsize_ADDR, 0);
            u16toEPPROM(panTime_align_ADDR, 2);

            u16toEPPROM(panHome_en_ADDR, 1);
            u16toEPPROM(panHome_mode_ADDR, 1);
            u16toEPPROM(panHome_fix_posX_ADDR, 150);
            u16toEPPROM(panHome_fix_posY_ADDR, 210);
            u16toEPPROM(panHome_dynamic_radius_ADDR, 70);

            u16toEPPROM(panWP_en_ADDR, 1);
            u16toEPPROM(panWP_mode_ADDR, 1);
            u16toEPPROM(panWP_fix_posX_ADDR, 150);
            u16toEPPROM(panWP_fix_posY_ADDR, 210);
            u16toEPPROM(panWP_dynamic_radius_ADDR, 120);

            //****
            //RSSI - TODO
            //****

            u16toEPPROM(panAltScale_en_ADDR, 1);
            u16toEPPROM(panAltScale_posX_ADDR, 350);
            u16toEPPROM(panAltScale_align_ADDR, 1);
            u16toEPPROM(panAltScale_src_ADDR, 0);       //NIY!

            u16toEPPROM(panSpeedScale_en_ADDR, 1);
            u16toEPPROM(panSpeedScale_posX_ADDR, 0);
            u16toEPPROM(panSpeedScale_align_ADDR, 0);
            u16toEPPROM(panSpeedScale_src_ADDR, 0);     //NIY!

            u16toEPPROM(panAttitude_mode_ADDR, 0);
            u16toEPPROM(panAttitude_Russia_mode_ADDR, 0);
            u16toEPPROM(panAttitude_2D_compass_en_ADDR, 1);
            //u16toEPPROM(panAttitude_2D_compass_posX_ADDR, 1); //discard, 
            u16toEPPROM(panAttitude_2D_compass_posY_ADDR, 15);
            u16toEPPROM(panAttitude_2D_roll_scale_en_ADDR, 1);
            u16toEPPROM(panAttitude_2D_roll_scale_radius_ADDR, 75);
            u16toEPPROM(panAttitude_2D_roll_value_en_ADDR, 1);
            u16toEPPROM(panAttitude_2D_pitch_scale_en_ADDR, 1);
            u16toEPPROM(panAttitude_2D_pitch_scale_Vstep_ADDR, 22);
            u16toEPPROM(panAttitude_2D_pitch_scale_Hlen_ADDR, 20);
            u16toEPPROM(panAttitude_2D_horizontal_len_ADDR, 90);
            u16toEPPROM(panAttitude_2D_pitch_value_en_ADDR, 1);
            u16toEPPROM(panAttitude_3D_cam_posX_ADDR, 0);
            u16toEPPROM(panAttitude_3D_cam_posY_ADDR, 0);
            u16toEPPROM(panAttitude_3D_cam_posZ_ADDR, 0);
            u16toEPPROM(panAttitude_3D_cam_dirX_ADDR, 0);
            u16toEPPROM(panAttitude_3D_cam_dirY_ADDR, 0);
            u16toEPPROM(panAttitude_3D_cam_dirZ_ADDR, 0);

            u16toEPPROM(panUnits_mode_ADDR, 0);

            u16toEPPROM(panWarn_minSpeed_ADDR, 1);
            u16toEPPROM(panWarn_maxSpeed_ADDR, 40);
            u16toEPPROM(panWarn_minAlt_ADDR, 1);
            u16toEPPROM(panWarn_maxAlt_ADDR, 500);
            //u16toEPPROM(panWarn_minBattVol_ADDR, 10); //TODO - for float
            u16toEPPROM(panWarn_minBattPercent_ADDR, 10);

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

        private void button1_Click(object sender, EventArgs e)
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

        private void button2_Click(object sender, EventArgs e)
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

        private void button3_Click(object sender, EventArgs e)
        {
           
        }

    }
}