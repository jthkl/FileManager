using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileManager
{
    public partial class TextViewerForm : Form
    {
        private string _filePath = string.Empty;

        // Win32 用于获取首可见行
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        public TextViewerForm() : this(string.Empty) { }

        public TextViewerForm(string filePath)
        {
            InitializeComponent();
            _filePath = filePath ?? string.Empty;

            // 尽早开启双缓冲，减少面板闪烁（使用反射设置受保护属性）
            try
            {
                var pd = typeof(Panel).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                pd?.SetValue(pnlLineNumbers, true, null);
                pd?.SetValue(pnlLineNumbersCanvas, true, null);
            }
            catch { /* 忽略在设计器/不同平台可能抛出的错误 */ }

            // 把打开按钮初始设为不可用（当没有有效文件）
            try { btnOpenDefault.Enabled = false; } catch { }

            // 事件绑定（确保只有一次订阅）
            txtContent.VScrolled -= TxtContent_VScrolled;
            txtContent.VScrolled += TxtContent_VScrolled;
            txtContent.TextChanged -= TxtContent_TextChanged;
            txtContent.TextChanged += TxtContent_TextChanged;
            txtContent.FontChanged -= TxtContent_FontChanged;
            txtContent.FontChanged += TxtContent_FontChanged;
            txtContent.Resize -= TxtContent_Resize;
            txtContent.Resize += TxtContent_Resize;

            this.Shown -= TextViewerForm_Shown;
            this.Shown += TextViewerForm_Shown;
        }

        private void TextViewerForm_Shown(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                txtContent.Text = "";
                this.Text = "Text 查看器";
                btnOpenDefault.Enabled = false;
                return;
            }

            this.Text = $"Text 查看器 - {Path.GetFileName(_filePath)}";
            btnOpenDefault.Enabled = File.Exists(_filePath);
            _ = LoadFileAsync(_filePath);
        }

        private async Task LoadFileAsync(string path)
        {
            if (!File.Exists(path))
            {
                MessageBox.Show("文件不存在或无法访问。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnOpenDefault.Enabled = false;
                return;
            }

            try
            {
                string content = await Task.Run(() => ReadFileWithBomDetection(path));
                if (this.IsHandleCreated)
                {
                    this.Invoke((Action)(() =>
                    {
                        txtContent.Text = content;
                        AdjustLineNumberWidthAndCanvas();
                        // 初始同步位置
                        SyncLineNumbersToText();
                        btnOpenDefault.Enabled = true;
                    }));
                }
                else
                {
                    txtContent.Text = content;
                    AdjustLineNumberWidthAndCanvas();
                    SyncLineNumbersToText();
                    btnOpenDefault.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取文件失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { btnOpenDefault.Enabled = false; } catch { }
            }
        }

        // 简单的 BOM / 编码检测：优先根据 BOM 判断，随后尝试严格 UTF8，否则回退到系统默认编码
        private string ReadFileWithBomDetection(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 LE
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 BE
            }

            // 尝试严格的 UTF8（遇到非法序列会抛出）
            try
            {
                var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                return utf8Strict.GetString(bytes);
            }
            catch
            {
                // 回退到系统默认编码（ANSI）
                return Encoding.Default.GetString(bytes);
            }
        }

        private void TxtContent_VScrolled(object sender, EventArgs e)
        {
            // 计算第一可见行对应字符的像素位置并移动 canvas，使其像素级跟随文本
            SyncLineNumbersToText();
        }

        private void TxtContent_TextChanged(object sender, EventArgs e)
        {
            AdjustLineNumberWidthAndCanvas();
            SyncLineNumbersToText();
        }

        private void TxtContent_FontChanged(object sender, EventArgs e)
        {
            AdjustLineNumberWidthAndCanvas();
            SyncLineNumbersToText();
        }

        private void TxtContent_Resize(object sender, EventArgs e)
        {
            AdjustLineNumberWidthAndCanvas();
            SyncLineNumbersToText();
        }

        // 调整行号列宽并同步 canvas 大小
        private void AdjustLineNumberWidthAndCanvas()
        {
            try
            {
                int totalLines = Math.Max(1, txtContent.Lines?.Length ?? 1);
                int digits = totalLines.ToString().Length;
                using (var g = pnlLineNumbers.CreateGraphics())
                {
                    var size = g.MeasureString(new string('9', digits) + " ", txtContent.Font);
                    int desired = Math.Max(30, (int)size.Width + 8);
                    if (pnlLineNumbers.Width != desired)
                    {
                        pnlLineNumbers.Width = desired;
                        txtContent.Location = new Point(pnlLineNumbers.Width, txtContent.Location.Y);
                        txtContent.Width = this.ClientSize.Width - pnlLineNumbers.Width;
                    }
                }

                // 让 canvas 宽度与列一致，计算一个足够的高度覆盖文本的内容高度
                pnlLineNumbersCanvas.Width = pnlLineNumbers.ClientSize.Width;
                int lastCharIdx = txtContent.GetCharIndexFromPosition(new Point(1, txtContent.ClientSize.Height - 1));
                int lastLine = txtContent.GetLineFromCharIndex(lastCharIdx);
                int lastLineIdx = Math.Max(0, txtContent.GetFirstCharIndexFromLine(lastLine));
                var lastPos = txtContent.GetPositionFromCharIndex(lastLineIdx);
                int contentHeight = lastPos.Y + txtContent.Font.Height + 40;
                pnlLineNumbersCanvas.Height = Math.Max(txtContent.ClientSize.Height, contentHeight);
            }
            catch { /* 忽略绘制期异常 */ }
        }

        // 将 canvas 的 Y 位置设置为 -firstVisibleCharPos.Y，从而像素级跟随文本内容的滚动
        private void SyncLineNumbersToText()
        {
            try
            {
                if (!txtContent.IsHandleCreated || !pnlLineNumbersCanvas.IsHandleCreated) { pnlLineNumbersCanvas.Invalidate(); return; }

                int firstVisible = GetFirstVisibleLine(txtContent);
                firstVisible = Math.Max(0, Math.Min(firstVisible, Math.Max(0, (txtContent.Lines?.Length ?? 1) - 1)));
                int firstCharIdx = txtContent.GetFirstCharIndexFromLine(firstVisible);
                if (firstCharIdx < 0) firstCharIdx = 0;
                var firstPos = txtContent.GetPositionFromCharIndex(firstCharIdx);

                // 将 canvas 上移以让其内容跟随文本移动（canvas 的坐标系与 txtContent 的坐标系一致）
                pnlLineNumbersCanvas.Location = new Point(0, -firstPos.Y);

                // 同步刷新（尽量在 VScrolled 中立即重绘）
                pnlLineNumbersCanvas.Refresh();
            }
            catch
            {
                try { pnlLineNumbersCanvas.Invalidate(); } catch { }
            }
        }

        // Canvas Paint：在 canvas 的坐标系中直接使用 txtContent 的字符位置绘制行号（不再做可见行的相对计算）
        private void PnlLineNumbersCanvas_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                e.Graphics.Clear(pnlLineNumbersCanvas.BackColor);
                if (txtContent.Lines == null || txtContent.Lines.Length == 0) return;

                var font = txtContent.Font;
                var brush = SystemBrushes.ControlText;
                int total = txtContent.Lines.Length;
                // 绘制所有行（通过 canvas 位移处理可见性）
                for (int i = 0; i < total; i++)
                {
                    int idx = txtContent.GetFirstCharIndexFromLine(i);
                    if (idx < 0) continue;
                    var pos = txtContent.GetPositionFromCharIndex(idx);
                    // pos.Y 是相对于 txtContent 的像素位置；canvas 已经通过 Location 做了相对偏移
                    string text = (i + 1).ToString();
                    var sz = e.Graphics.MeasureString(text, font);
                    float x = pnlLineNumbersCanvas.ClientSize.Width - sz.Width - 4;
                    e.Graphics.DrawString(text, font, brush, x, pos.Y);
                }
            }
            catch { /* 忽略绘制异常，避免影响主流程 */ }
        }

        private int GetFirstVisibleLine(System.Windows.Forms.RichTextBox rtb)
        {
            if (rtb == null) return 0;
            if (!rtb.IsHandleCreated) return 0;
            try
            {
                return SendMessage(rtb.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                return 0;
            }
        }

        private void BtnOpenDefault_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                MessageBox.Show("当前没有要打开的文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(_filePath))
            {
                MessageBox.Show("文件不存在或无法访问。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnOpenDefault.Enabled = false;
                return;
            }

            try
            {
                var psi = new ProcessStartInfo(_filePath) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法用默认程序打开：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
