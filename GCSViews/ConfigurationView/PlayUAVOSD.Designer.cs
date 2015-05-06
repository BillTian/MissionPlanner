namespace MissionPlanner.GCSViews.ConfigurationView
{
    partial class PlayUAVOSD
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.Save_To_OSD = new System.Windows.Forms.Button();
            this.Load_from_OSD = new System.Windows.Forms.Button();
            this.Load_Default = new System.Windows.Forms.Button();
            this.Params = new BrightIdeasSoftware.DataTreeListView();
            this.olvColumn1 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn2 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn3 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn4 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn5 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Sav_To_EEPROM = new System.Windows.Forms.Button();
            this.btn_save_file = new System.Windows.Forms.Button();
            this.btn_load_file = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.Params)).BeginInit();
            this.SuspendLayout();
            // 
            // Save_To_OSD
            // 
            this.Save_To_OSD.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.Save_To_OSD.BackColor = System.Drawing.SystemColors.Control;
            this.Save_To_OSD.Location = new System.Drawing.Point(632, 54);
            this.Save_To_OSD.Name = "Save_To_OSD";
            this.Save_To_OSD.Size = new System.Drawing.Size(101, 30);
            this.Save_To_OSD.TabIndex = 0;
            this.Save_To_OSD.Text = "保存到内存";
            this.Save_To_OSD.UseVisualStyleBackColor = false;
            this.Save_To_OSD.Click += new System.EventHandler(this.btn_Save_To_OSD_Click);
            this.Save_To_OSD.MouseEnter += new System.EventHandler(this.Save_To_OSD_MouseEnter);
            // 
            // Load_from_OSD
            // 
            this.Load_from_OSD.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.Load_from_OSD.Location = new System.Drawing.Point(632, 18);
            this.Load_from_OSD.Name = "Load_from_OSD";
            this.Load_from_OSD.Size = new System.Drawing.Size(101, 30);
            this.Load_from_OSD.TabIndex = 1;
            this.Load_from_OSD.Text = "读取参数";
            this.Load_from_OSD.UseVisualStyleBackColor = true;
            this.Load_from_OSD.Click += new System.EventHandler(this.btn_Load_from_OSD_Click);
            // 
            // Load_Default
            // 
            this.Load_Default.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.Load_Default.Location = new System.Drawing.Point(632, 196);
            this.Load_Default.Name = "Load_Default";
            this.Load_Default.Size = new System.Drawing.Size(101, 24);
            this.Load_Default.TabIndex = 3;
            this.Load_Default.Text = "加载默认参数";
            this.Load_Default.UseVisualStyleBackColor = true;
            this.Load_Default.Click += new System.EventHandler(this.btn_Load_Default_Click);
            // 
            // Params
            // 
            this.Params.AllColumns.Add(this.olvColumn1);
            this.Params.AllColumns.Add(this.olvColumn2);
            this.Params.AllColumns.Add(this.olvColumn3);
            this.Params.AllColumns.Add(this.olvColumn4);
            this.Params.AllColumns.Add(this.olvColumn5);
            this.Params.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.Params.BackColor = System.Drawing.SystemColors.ScrollBar;
            this.Params.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.olvColumn1,
            this.olvColumn2,
            this.olvColumn3,
            this.olvColumn4,
            this.olvColumn5});
            this.Params.DataSource = null;
            this.Params.ForeColor = System.Drawing.Color.White;
            this.Params.Location = new System.Drawing.Point(4, 4);
            this.Params.Name = "Params";
            this.Params.OwnerDraw = true;
            this.Params.RootKeyValueString = "";
            this.Params.RowHeight = 26;
            this.Params.ShowGroups = false;
            this.Params.Size = new System.Drawing.Size(622, 457);
            this.Params.TabIndex = 79;
            this.Params.UseAlternatingBackColors = true;
            this.Params.UseCompatibleStateImageBehavior = false;
            this.Params.View = System.Windows.Forms.View.Details;
            this.Params.VirtualMode = true;
            this.Params.CellEditFinishing += new BrightIdeasSoftware.CellEditEventHandler(this.Params_CellEditFinishing);
            this.Params.FormatRow += new System.EventHandler<BrightIdeasSoftware.FormatRowEventArgs>(this.Params_FormatRow);
            // 
            // olvColumn1
            // 
            this.olvColumn1.AspectName = "paramname";
            this.olvColumn1.CellPadding = null;
            this.olvColumn1.IsEditable = false;
            this.olvColumn1.Text = "参数名";
            this.olvColumn1.Width = 160;
            // 
            // olvColumn2
            // 
            this.olvColumn2.AspectName = "Value";
            this.olvColumn2.AutoCompleteEditor = false;
            this.olvColumn2.AutoCompleteEditorMode = System.Windows.Forms.AutoCompleteMode.None;
            this.olvColumn2.CellPadding = null;
            this.olvColumn2.Text = "值";
            this.olvColumn2.Width = 80;
            // 
            // olvColumn3
            // 
            this.olvColumn3.AspectName = "unit";
            this.olvColumn3.CellPadding = null;
            this.olvColumn3.IsEditable = false;
            this.olvColumn3.Text = "单位";
            // 
            // olvColumn4
            // 
            this.olvColumn4.AspectName = "range";
            this.olvColumn4.CellPadding = null;
            this.olvColumn4.IsEditable = false;
            this.olvColumn4.Text = "范围";
            this.olvColumn4.Width = 100;
            this.olvColumn4.WordWrap = true;
            // 
            // olvColumn5
            // 
            this.olvColumn5.AspectName = "desc";
            this.olvColumn5.CellPadding = null;
            this.olvColumn5.IsEditable = false;
            this.olvColumn5.Text = "描述";
            this.olvColumn5.Width = 210;
            this.olvColumn5.WordWrap = true;
            // 
            // Sav_To_EEPROM
            // 
            this.Sav_To_EEPROM.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.Sav_To_EEPROM.Location = new System.Drawing.Point(632, 90);
            this.Sav_To_EEPROM.Name = "Sav_To_EEPROM";
            this.Sav_To_EEPROM.Size = new System.Drawing.Size(101, 30);
            this.Sav_To_EEPROM.TabIndex = 80;
            this.Sav_To_EEPROM.Text = "保存到FLASH";
            this.Sav_To_EEPROM.UseVisualStyleBackColor = true;
            this.Sav_To_EEPROM.Click += new System.EventHandler(this.Sav_To_EEPROM_Click);
            this.Sav_To_EEPROM.MouseEnter += new System.EventHandler(this.Sav_To_EEPROM_MouseEnter);
            // 
            // btn_save_file
            // 
            this.btn_save_file.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.btn_save_file.Location = new System.Drawing.Point(632, 256);
            this.btn_save_file.Name = "btn_save_file";
            this.btn_save_file.Size = new System.Drawing.Size(101, 25);
            this.btn_save_file.TabIndex = 81;
            this.btn_save_file.Text = "保存文件";
            this.btn_save_file.UseVisualStyleBackColor = true;
            this.btn_save_file.Click += new System.EventHandler(this.btn_save_file_Click);
            // 
            // btn_load_file
            // 
            this.btn_load_file.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.btn_load_file.Location = new System.Drawing.Point(632, 226);
            this.btn_load_file.Name = "btn_load_file";
            this.btn_load_file.Size = new System.Drawing.Size(101, 24);
            this.btn_load_file.TabIndex = 82;
            this.btn_load_file.Text = "加载文件";
            this.btn_load_file.UseVisualStyleBackColor = true;
            this.btn_load_file.Click += new System.EventHandler(this.btn_load_file_Click);
            // 
            // PlayUAVOSD
            // 
            this.Controls.Add(this.btn_load_file);
            this.Controls.Add(this.btn_save_file);
            this.Controls.Add(this.Sav_To_EEPROM);
            this.Controls.Add(this.Load_Default);
            this.Controls.Add(this.Save_To_OSD);
            this.Controls.Add(this.Load_from_OSD);
            this.Controls.Add(this.Params);
            this.Name = "PlayUAVOSD";
            this.Size = new System.Drawing.Size(809, 489);
            ((System.ComponentModel.ISupportInitialize)(this.Params)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button Save_To_OSD;
        private System.Windows.Forms.Button Load_from_OSD;
        private System.Windows.Forms.Button Load_Default;
        private BrightIdeasSoftware.DataTreeListView Params;
        private BrightIdeasSoftware.OLVColumn olvColumn1;
        private BrightIdeasSoftware.OLVColumn olvColumn2;
        private BrightIdeasSoftware.OLVColumn olvColumn3;
        private BrightIdeasSoftware.OLVColumn olvColumn4;
        private BrightIdeasSoftware.OLVColumn olvColumn5;
        private System.Windows.Forms.Button Sav_To_EEPROM;
        private System.Windows.Forms.Button btn_save_file;
        private System.Windows.Forms.Button btn_load_file;


 
    }
}

