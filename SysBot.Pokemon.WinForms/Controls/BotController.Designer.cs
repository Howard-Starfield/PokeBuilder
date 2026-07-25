using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    partial class BotController
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe event handlers to prevent memory leaks
                if (contextMenu != null)
                {
                    contextMenu.Opening -= RcMenuOnOpening;
                }

                // Unsubscribe MouseEnter/MouseLeave handlers from all controls
                foreach (var c in Controls.OfType<Control>())
                {
                    if (c != btnActions)
                    {
                        c.MouseEnter -= BotController_MouseEnter;
                        c.MouseLeave -= BotController_MouseLeave;
                    }
                }

                if (mainPanel != null)
                {
                    foreach (var c in mainPanel.Controls.OfType<Control>())
                    {
                        c.MouseEnter -= BotController_MouseEnter;
                        c.MouseLeave -= BotController_MouseLeave;
                    }
                }

                if (animationTimer != null)
                {
                    animationTimer.Stop();
                    animationTimer.Dispose();
                }

                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.mainPanel = new System.Windows.Forms.Panel();
            this.statusIndicator = new System.Windows.Forms.PictureBox();
            this.lblStatusValue = new System.Windows.Forms.Label();
            this.lblBotName = new System.Windows.Forms.Label();
            this.lblRoutineType = new System.Windows.Forms.Label();
            this.lblConnectionInfo = new System.Windows.Forms.Label();
            this.btnActions = new System.Windows.Forms.Button();
            this.contextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.animationTimer = new System.Windows.Forms.Timer(this.components);
            this.mainPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.statusIndicator)).BeginInit();
            this.SuspendLayout();

            // BotController
            this.SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
                         System.Windows.Forms.ControlStyles.UserPaint |
                         System.Windows.Forms.ControlStyles.DoubleBuffer |
                         System.Windows.Forms.ControlStyles.ResizeRedraw |
                         System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(22, 32, 45);
            this.Controls.Add(this.mainPanel);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.Margin = new System.Windows.Forms.Padding(0, 0, 0, 5);
            this.Name = "BotController";
            this.Size = new System.Drawing.Size(900, 100);
            this.Paint += new System.Windows.Forms.PaintEventHandler(this.BotController_Paint);
            this.MouseEnter += new System.EventHandler(this.BotController_MouseEnter);
            this.MouseLeave += new System.EventHandler(this.BotController_MouseLeave);

            // Main Panel
            this.mainPanel.Anchor = System.Windows.Forms.AnchorStyles.Top |
                                     System.Windows.Forms.AnchorStyles.Bottom |
                                     System.Windows.Forms.AnchorStyles.Left |
                                     System.Windows.Forms.AnchorStyles.Right;
            this.mainPanel.BackColor = System.Drawing.Color.FromArgb(22, 32, 45);
            this.mainPanel.Controls.Add(this.statusIndicator);
            this.mainPanel.Controls.Add(this.lblStatusValue);
            this.mainPanel.Controls.Add(this.lblBotName);
            this.mainPanel.Controls.Add(this.lblRoutineType);
            this.mainPanel.Controls.Add(this.lblConnectionInfo);
            this.mainPanel.Controls.Add(this.btnActions);
            this.mainPanel.Location = new System.Drawing.Point(3, 3);
            this.mainPanel.Name = "mainPanel";
            this.mainPanel.Size = new System.Drawing.Size(894, 94);
            this.mainPanel.TabIndex = 0;
            this.mainPanel.Paint += new System.Windows.Forms.PaintEventHandler(this.MainPanel_Paint);
            this.mainPanel.MouseEnter += new System.EventHandler(this.BotController_MouseEnter);
            this.mainPanel.MouseLeave += new System.EventHandler(this.BotController_MouseLeave);

            // Status Indicator (pulsing indicator on the left)
            this.statusIndicator.BackColor = System.Drawing.Color.Transparent;
            this.statusIndicator.Location = new System.Drawing.Point(12, 23);
            this.statusIndicator.Name = "statusIndicator";
            this.statusIndicator.Size = new System.Drawing.Size(8, 8);
            this.statusIndicator.TabIndex = 0;
            this.statusIndicator.TabStop = false;
            this.statusIndicator.Paint += new System.Windows.Forms.PaintEventHandler(this.StatusIndicator_Paint);

            // Status Value Label (RUNNING, STOPPED, etc.)
            this.lblStatusValue.AutoSize = false;
            this.lblStatusValue.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblStatusValue.ForeColor = System.Drawing.Color.FromArgb(90, 186, 71);
            this.lblStatusValue.Location = new System.Drawing.Point(45, 20);
            this.lblStatusValue.Name = "lblStatusValue";
            this.lblStatusValue.Size = new System.Drawing.Size(180, 16);
            this.lblStatusValue.TabIndex = 1;
            this.lblStatusValue.Text = "RUNNING";
            this.lblStatusValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // Bot Name Label
            this.lblBotName.AutoSize = false;
            this.lblBotName.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblBotName.ForeColor = System.Drawing.Color.FromArgb(239, 239, 239);
            this.lblBotName.Location = new System.Drawing.Point(45, 38);
            this.lblBotName.Name = "lblBotName";
            this.lblBotName.Size = new System.Drawing.Size(400, 18);
            this.lblBotName.TabIndex = 2;
            this.lblBotName.Text = "192.168.1.100";
            this.lblBotName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblBotName.MouseEnter += new System.EventHandler(this.BotController_MouseEnter);
            this.lblBotName.MouseLeave += new System.EventHandler(this.BotController_MouseLeave);

            // Routine Type Label (trade type @ time)
            this.lblRoutineType.AutoSize = false;
            this.lblRoutineType.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.lblRoutineType.ForeColor = System.Drawing.Color.FromArgb(139, 179, 217);
            this.lblRoutineType.Location = new System.Drawing.Point(45, 56);
            this.lblRoutineType.Name = "lblRoutineType";
            this.lblRoutineType.Size = new System.Drawing.Size(400, 14);
            this.lblRoutineType.TabIndex = 3;
            this.lblRoutineType.Text = "FlexTrade";
            this.lblRoutineType.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblRoutineType.MouseEnter += new System.EventHandler(this.BotController_MouseEnter);
            this.lblRoutineType.MouseLeave += new System.EventHandler(this.BotController_MouseLeave);

            // Connection Info Label (current activity with arrow icon)
            this.lblConnectionInfo.Anchor = System.Windows.Forms.AnchorStyles.Top |
                                           System.Windows.Forms.AnchorStyles.Left |
                                           System.Windows.Forms.AnchorStyles.Right;
            this.lblConnectionInfo.AutoEllipsis = true;
            this.lblConnectionInfo.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.lblConnectionInfo.ForeColor = System.Drawing.Color.FromArgb(176, 176, 176);
            this.lblConnectionInfo.Location = new System.Drawing.Point(45, 74);
            this.lblConnectionInfo.Name = "lblConnectionInfo";
            this.lblConnectionInfo.Size = new System.Drawing.Size(600, 15);
            this.lblConnectionInfo.TabIndex = 4;
            this.lblConnectionInfo.Text = "→ Waiting for command...";
            this.lblConnectionInfo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblConnectionInfo.MouseEnter += new System.EventHandler(this.BotController_MouseEnter);
            this.lblConnectionInfo.MouseLeave += new System.EventHandler(this.BotController_MouseLeave);

            // Actions Button (top-right corner)
            this.btnActions.Anchor = System.Windows.Forms.AnchorStyles.Top |
                                    System.Windows.Forms.AnchorStyles.Right;
            this.btnActions.BackColor = System.Drawing.Color.FromArgb(102, 192, 244);
            this.btnActions.FlatAppearance.BorderSize = 0;
            this.btnActions.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(92, 173, 220);
            this.btnActions.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(122, 207, 255);
            this.btnActions.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnActions.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
            this.btnActions.ForeColor = System.Drawing.Color.White;
            this.btnActions.Location = new System.Drawing.Point(770, 12);
            this.btnActions.Name = "btnActions";
            this.btnActions.Size = new System.Drawing.Size(110, 25);
            this.btnActions.TabIndex = 5;
            this.btnActions.Text = "➤ BOT MENU";
            this.btnActions.UseVisualStyleBackColor = false;
            this.btnActions.Click += new System.EventHandler(this.BtnActions_Click);
            this.btnActions.Paint += new System.Windows.Forms.PaintEventHandler(this.BtnActions_Paint);
            this.btnActions.MouseEnter += new System.EventHandler(this.BtnActions_MouseEnter);
            this.btnActions.MouseLeave += new System.EventHandler(this.BtnActions_MouseLeave);

            // Context Menu
            this.contextMenu.BackColor = System.Drawing.Color.FromArgb(35, 45, 60);
            this.contextMenu.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.contextMenu.ForeColor = System.Drawing.Color.White;
            this.contextMenu.Name = "contextMenu";
            this.contextMenu.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.contextMenu.ShowImageMargin = false;
            this.contextMenu.Size = new System.Drawing.Size(150, 4);

            // Animation Timer
            this.animationTimer.Interval = 50;
            this.animationTimer.Tick += new System.EventHandler(this.AnimationTimer_Tick);
            this.animationTimer.Enabled = false; // Disabled to remove animations

            // Component initialization
            this.L_Description = this.lblConnectionInfo;
            this.L_Left = this.lblBotName;
            this.PB_Lamp = this.statusIndicator;
            this.RCMenu = this.contextMenu;
            this.progressBar = new System.Windows.Forms.Panel();
            this.actionButton = this.btnActions;

            this.mainPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.statusIndicator)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        // Legacy controls for compatibility
        private System.Windows.Forms.Label L_Description;
        private System.Windows.Forms.Label L_Left;
        private System.Windows.Forms.PictureBox PB_Lamp;
        private System.Windows.Forms.ContextMenuStrip RCMenu;
        private System.Windows.Forms.Panel progressBar;
        private System.Windows.Forms.Button actionButton;
        private System.Windows.Forms.Timer animationTimer;

        // Main controls
        private System.Windows.Forms.Panel mainPanel;
        private System.Windows.Forms.PictureBox statusIndicator;
        private System.Windows.Forms.Label lblStatusValue;
        private System.Windows.Forms.Label lblBotName;
        private System.Windows.Forms.Label lblRoutineType;
        private System.Windows.Forms.Label lblConnectionInfo;
        private System.Windows.Forms.Button btnActions;
        private System.Windows.Forms.ContextMenuStrip contextMenu;
    }
}
