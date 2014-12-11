namespace MissionPlanner.GCSViews
{
    partial class OSD
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.backstageView = new MissionPlanner.Controls.BackstageView.BackstageView();
            this.backstageViewPageMinimOSD = new MissionPlanner.Controls.BackstageView.BackstageViewPage();
            this.initialSetupBindingSource = new System.Windows.Forms.BindingSource(this.components);
            this.MinimOSD1 = new MissionPlanner.GCSViews.ConfigurationView.MinimOSD();
            ((System.ComponentModel.ISupportInitialize)(this.initialSetupBindingSource)).BeginInit();
            this.SuspendLayout();
            // 
            // backstageView
            // 
            this.backstageView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.backstageView.HighlightColor1 = System.Drawing.SystemColors.Highlight;
            this.backstageView.HighlightColor2 = System.Drawing.SystemColors.MenuHighlight;
            this.backstageView.Location = new System.Drawing.Point(0, 0);
            this.backstageView.Margin = new System.Windows.Forms.Padding(4);
            this.backstageView.Name = "backstageView";
            this.backstageView.Pages.Add(this.backstageViewPageMinimOSD);
            this.backstageView.Size = new System.Drawing.Size(1000, 450);
            this.backstageView.TabIndex = 0;
            this.backstageView.WidthMenu = 172;
            // 
            // backstageViewPageMinimOSD
            // 
            this.backstageViewPageMinimOSD.Advanced = false;
            this.backstageViewPageMinimOSD.DataBindings.Add(new System.Windows.Forms.Binding("Show", this.initialSetupBindingSource, "isDisConnected", true));
            this.backstageViewPageMinimOSD.LinkText = "MinimOSD";
            this.backstageViewPageMinimOSD.Page = this.MinimOSD1;
            this.backstageViewPageMinimOSD.Parent = null;
            this.backstageViewPageMinimOSD.Show = true;
            this.backstageViewPageMinimOSD.Spacing = 30;
            this.backstageViewPageMinimOSD.Text = "MinimOSD";
            // 
            // initialSetupBindingSource
            // 
            this.initialSetupBindingSource.DataSource = typeof(MissionPlanner.GCSViews.OSD);
            // 
            // MinimOSD1
            // 
            this.MinimOSD1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MinimOSD1.Location = new System.Drawing.Point(0, 0);
            this.MinimOSD1.Name = "MinimOSD1";
            this.MinimOSD1.Size = new System.Drawing.Size(843, 567);
            this.MinimOSD1.TabIndex = 0;
            // 
            // OSD
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.Controls.Add(this.backstageView);
            this.MinimumSize = new System.Drawing.Size(1000, 450);
            this.Name = "OSD";
            this.Size = new System.Drawing.Size(1000, 450);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.HardwareConfig_FormClosing);
            this.Load += new System.EventHandler(this.HardwareConfig_Load);
            ((System.ComponentModel.ISupportInitialize)(this.initialSetupBindingSource)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private Controls.BackstageView.BackstageView backstageView;
        private Controls.BackstageView.BackstageViewPage backstageViewPageMinimOSD;
        private System.Windows.Forms.BindingSource initialSetupBindingSource;
       
        private ConfigurationView.MinimOSD MinimOSD1;
    }
}
