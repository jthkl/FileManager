using System;

namespace FileManager
{
    partial class TextViewerForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.Panel pnlLineNumbers;
        private System.Windows.Forms.Panel pnlLineNumbersCanvas;
        public ScrollingRichTextBox txtContent;
        private System.Windows.Forms.Panel pnlBottom;
        private System.Windows.Forms.Button btnOpenDefault;

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
            this.pnlLineNumbers = new System.Windows.Forms.Panel();
            this.pnlLineNumbersCanvas = new System.Windows.Forms.Panel();
            this.txtContent = new FileManager.ScrollingRichTextBox();
            this.pnlBottom = new System.Windows.Forms.Panel();
            this.btnOpenDefault = new System.Windows.Forms.Button();
            this.pnlLineNumbers.SuspendLayout();
            this.pnlBottom.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlLineNumbers
            // 
            this.pnlLineNumbers.BackColor = System.Drawing.SystemColors.ControlLight;
            this.pnlLineNumbers.Controls.Add(this.pnlLineNumbersCanvas);
            this.pnlLineNumbers.Dock = System.Windows.Forms.DockStyle.Left;
            this.pnlLineNumbers.Location = new System.Drawing.Point(0, 0);
            this.pnlLineNumbers.Name = "pnlLineNumbers";
            this.pnlLineNumbers.Size = new System.Drawing.Size(50, 583);
            this.pnlLineNumbers.TabIndex = 0;
            // 
            // pnlLineNumbersCanvas
            // 
            this.pnlLineNumbersCanvas.Location = new System.Drawing.Point(0, 0);
            this.pnlLineNumbersCanvas.Name = "pnlLineNumbersCanvas";
            this.pnlLineNumbersCanvas.Size = new System.Drawing.Size(50, 400);
            this.pnlLineNumbersCanvas.TabIndex = 1;
            this.pnlLineNumbersCanvas.Paint += new System.Windows.Forms.PaintEventHandler(this.PnlLineNumbersCanvas_Paint);
            // 
            // txtContent
            // 
            this.txtContent.AcceptsTab = true;
            this.txtContent.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtContent.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtContent.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtContent.Location = new System.Drawing.Point(50, 0);
            this.txtContent.Name = "txtContent";
            this.txtContent.Size = new System.Drawing.Size(750, 583);
            this.txtContent.TabIndex = 2;
            this.txtContent.Text = "";
            this.txtContent.WordWrap = false;
            // 
            // pnlBottom
            // 
            this.pnlBottom.Controls.Add(this.btnOpenDefault);
            this.pnlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlBottom.Location = new System.Drawing.Point(0, 583);
            this.pnlBottom.Name = "pnlBottom";
            this.pnlBottom.Size = new System.Drawing.Size(800, 40);
            this.pnlBottom.TabIndex = 3;
            // 
            // btnOpenDefault
            // 
            this.btnOpenDefault.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpenDefault.Enabled = false;
            this.btnOpenDefault.Location = new System.Drawing.Point(521, 6);
            this.btnOpenDefault.Name = "btnOpenDefault";
            this.btnOpenDefault.Size = new System.Drawing.Size(249, 28);
            this.btnOpenDefault.TabIndex = 0;
            this.btnOpenDefault.Text = "用默认程序打开当前文件(&O)";
            this.btnOpenDefault.UseVisualStyleBackColor = true;
            this.btnOpenDefault.Click += new System.EventHandler(this.BtnOpenDefault_Click);
            // 
            // TextViewerForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 623);
            this.Controls.Add(this.txtContent);
            this.Controls.Add(this.pnlLineNumbers);
            this.Controls.Add(this.pnlBottom);
            this.Name = "TextViewerForm";
            this.Text = "Text 查看器";
            this.pnlLineNumbers.ResumeLayout(false);
            this.pnlBottom.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion
    }

    // 公共的 RichTextBox，带垂直滚动消息通知，和无参构造以支持设计器。
    public class ScrollingRichTextBox : System.Windows.Forms.RichTextBox
    {
        public event EventHandler VScrolled;

        public ScrollingRichTextBox()
        {
            // 设计器需要无参构造
        }

        private const int WM_VSCROLL = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL)
            {
                VScrolled?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}