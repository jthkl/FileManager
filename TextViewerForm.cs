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
        // 当前正在查看的文件完整路径；始终初始化为非空字符串以兼容 Designer 要求。
        private string _filePath = string.Empty;

        // Win32 消息，用于从 RichTextBox 获取第一个可见的行索引（像素到行映射由控件内部维护）
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;

        // 导入 user32.dll 的 SendMessage，用于向 RichTextBox 发送 EM_GETFIRSTVISIBLELINE 请求。
        // 注意：调用前务必检查 rtb.IsHandleCreated，避免在设计器/初始化阶段调用 Win32 API。
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        // 无参构造用于 Designer 兼容；实际构造逻辑委托给带参数的构造函数。
        public TextViewerForm() : this(string.Empty) { }

        // 主构造函数：接收要打开的文件路径（可以为空），完成控件初始化与事件绑定。
        public TextViewerForm(string filePath)
        {
            InitializeComponent();

            // 保证 _filePath 不为 null，遵循项目约定以避免后续 NullReference 问题
            _filePath = filePath ?? string.Empty;

            // 尽早尝试为行号容器启用双缓冲，减少重绘闪烁
            try
            {
                var pd = typeof(Panel).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                pd?.SetValue(pnlLineNumbers, true, null);
                pd?.SetValue(pnlLineNumbersCanvas, true, null);
            }
            catch
            {
                // 在某些设计器/托管环境中反射可能失败，忽略并继续运行（不是致命错误）
            }

            // 初始时禁用“用默认程序打开”按钮，直到确认文件存在
            try { btnOpenDefault.Enabled = false; } catch { }

            // 事件订阅：先取消再订阅，确保不会重复添加处理器（防止单元测试或重复初始化时多次触发）
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

        // 窗体第一次显示时触发：如果有有效路径则开始异步加载文件并启用打开按钮
        private void TextViewerForm_Shown(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                // 无路径：清空文本并保持按钮禁用
                txtContent.Text = "";
                this.Text = "Text 查看器";
                btnOpenDefault.Enabled = false;
                return;
            }

            // 显示基本标题并检查文件是否存在
            this.Text = $"Text 查看器 - {Path.GetFileName(_filePath)}";
            btnOpenDefault.Enabled = File.Exists(_filePath);

            // 异步加载文件内容（避免阻塞 UI 线程）
            _ = LoadFileAsync(_filePath);
        }

        // 异步读取并显示文件内容，包含异常处理与 UI 线程切换
        private async Task LoadFileAsync(string path)
        {
            if (!File.Exists(path))
            {
                // 文件不存在，提示并禁用打开按钮
                MessageBox.Show("文件不存在或无法访问。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnOpenDefault.Enabled = false;
                return;
            }

            try
            {
                // 在后台线程读取文件并做简单的 BOM/编码检测
                string content = await Task.Run(() => ReadFileWithBomDetection(path));

                // 把读取到的内容设置到 RichTextBox（在 UI 线程）
                if (this.IsHandleCreated)
                {
                    this.Invoke((Action)(() =>
                    {
                        txtContent.Text = content;
                        AdjustLineNumberWidthAndCanvas();
                        // 初始时让行号 canvas 与文本内容对齐
                        SyncLineNumbersToText();
                        btnOpenDefault.Enabled = true;
                    }));
                }
                else
                {
                    // 在极少数情形下（控件句柄尚未创建），直接赋值并调整布局
                    txtContent.Text = content;
                    AdjustLineNumberWidthAndCanvas();
                    SyncLineNumbersToText();
                    btnOpenDefault.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                // 读取期间发生异常（例如权限或编码问题），显示错误并禁用打开按钮
                MessageBox.Show($"读取文件失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { btnOpenDefault.Enabled = false; } catch { }
            }
        }

        // 读取文件并进行简单 BOM / 编码检测
        // 优先通过 BOM 判断编码（UTF-8/UTF-16 LE/BE），否则尝试严格 UTF-8，再回退到系统默认编码（ANSI）
        private string ReadFileWithBomDetection(string path)
        {
            var bytes = File.ReadAllBytes(path);

            // UTF-8 BOM (EF BB BF)
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            // UTF-16 LE BOM (FF FE)
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            // UTF-16 BE BOM (FE FF)
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            // 尝试严格的 UTF-8（遇到非法序列会抛出），以避免将错误字节误解码
            try
            {
                var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                return utf8Strict.GetString(bytes);
            }
            catch
            {
                // 回退到系统默认编码（通常为 ANSI）以保证最大兼容性
                return Encoding.Default.GetString(bytes);
            }
        }

        // RichTextBox 垂直滚动时触发：同步调整行号 canvas 的像素偏移
        // 使用像素级跟随（基于首可见字符位置）以避免行号与文本不同步或闪烁
        private void TxtContent_VScrolled(object sender, EventArgs e)
        {
            SyncLineNumbersToText();
        }

        // 文本变更时调整行号列宽并同步位置
        private void TxtContent_TextChanged(object sender, EventArgs e)
        {
            AdjustLineNumberWidthAndCanvas();
            SyncLineNumbersToText();
        }

        // 字体变化（字号或字体）时需要重新计算列宽与 canvas 大小
        private void TxtContent_FontChanged(object sender, EventArgs e)
        {
            AdjustLineNumberWidthAndCanvas();
            SyncLineNumbersToText();
        }

        // 控件尺寸变化时同步调整
        private void TxtContent_Resize(object sender, EventArgs e)
        {
            AdjustLineNumberWidthAndCanvas();
            SyncLineNumbersToText();
        }

        // 计算行号列所需宽度并调整 canvas 尺寸（canvas 用于像素级绘制所有行号）
        private void AdjustLineNumberWidthAndCanvas()
        {
            try
            {
                // 计算文本行数与所需数字位数
                int totalLines = Math.Max(1, txtContent.Lines?.Length ?? 1);
                int digits = totalLines.ToString().Length;

                // 使用 Graphics 测量字符串宽度以决定列宽（有额外间距）
                using (var g = pnlLineNumbers.CreateGraphics())
                {
                    var size = g.MeasureString(new string('9', digits) + " ", txtContent.Font);
                    int desired = Math.Max(30, (int)size.Width + 8);

                    // 仅当宽度不同才更新布局，减少不必要的重排
                    if (pnlLineNumbers.Width != desired)
                    {
                        pnlLineNumbers.Width = desired;
                        txtContent.Location = new Point(pnlLineNumbers.Width, txtContent.Location.Y);
                        txtContent.Width = this.ClientSize.Width - pnlLineNumbers.Width;
                    }
                }

                // 确保 canvas 宽度与列宽一致，并计算 canvas 的高度以容纳全部行（用于位移策略）
                pnlLineNumbersCanvas.Width = pnlLineNumbers.ClientSize.Width;

                // 获取文本最后可见行的像素位置，作为估算内容高度的依据
                int lastCharIdx = txtContent.GetCharIndexFromPosition(new Point(1, txtContent.ClientSize.Height - 1));
                int lastLine = txtContent.GetLineFromCharIndex(lastCharIdx);
                int lastLineIdx = Math.Max(0, txtContent.GetFirstCharIndexFromLine(lastLine));
                var lastPos = txtContent.GetPositionFromCharIndex(lastLineIdx);

                // contentHeight 留有一点余量，防止精确计算带来的越界
                int contentHeight = lastPos.Y + txtContent.Font.Height + 40;
                pnlLineNumbersCanvas.Height = Math.Max(txtContent.ClientSize.Height, contentHeight);
            }
            catch
            {
                // 绘制/度量期间可能抛出异常（例如控件尚未完全创建），这里吞掉异常以保证 UI 不崩溃
            }
        }

        // 同步行号 canvas 的位置，使其在像素级别跟随文本的滚动位置
        private void SyncLineNumbersToText()
        {
            try
            {
                // 保护性检查：在设计时或控件尚未完成创建时不执行复杂计算
                if (!txtContent.IsHandleCreated || !pnlLineNumbersCanvas.IsHandleCreated)
                {
                    pnlLineNumbersCanvas.Invalidate();
                    return;
                }

                // 获取首个可见行（行索引），并转换为该行首字符的字符索引
                int firstVisible = GetFirstVisibleLine(txtContent);
                firstVisible = Math.Max(0, Math.Min(firstVisible, Math.Max(0, (txtContent.Lines?.Length ?? 1) - 1)));
                int firstCharIdx = txtContent.GetFirstCharIndexFromLine(firstVisible);
                if (firstCharIdx < 0) firstCharIdx = 0;

                // 根据字符索引计算像素位置（相对于 RichTextBox 的客户端坐标）
                var firstPos = txtContent.GetPositionFromCharIndex(firstCharIdx);

                // 将 canvas 上移（负 Y），使得 canvas 上绘制的行号能够与文本在像素级别对齐
                // 这种方法避免了在滚动高频发生时文本和绘制不同步的问题
                pnlLineNumbersCanvas.Location = new Point(0, -firstPos.Y);

                // 立即刷新 canvas（同步重绘），尽量减少重绘延迟导致的抖动
                pnlLineNumbersCanvas.Refresh();
            }
            catch
            {
                // 若出现异常，退回到异步重绘以保证不抛出不可恢复错误
                try { pnlLineNumbersCanvas.Invalidate(); } catch { }
            }
        }

        // Canvas 的 Paint 事件：在 canvas 的坐标系中直接根据文本字符的像素位置绘制每行行号
        // 由于 canvas 会被通过 Location 位移（SyncLineNumbersToText），这里不需要再计算相对偏移
        private void PnlLineNumbersCanvas_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                e.Graphics.Clear(pnlLineNumbersCanvas.BackColor);

                // 如果没有文本或行数为 0，直接返回
                if (txtContent.Lines == null || txtContent.Lines.Length == 0) return;

                var font = txtContent.Font;
                var brush = SystemBrushes.ControlText;
                int total = txtContent.Lines.Length;

                // 遍历所有行并绘制行号。对于非常大的文件（百万行），此处可以优化为只绘制可见行范围。
                for (int i = 0; i < total; i++)
                {
                    int idx = txtContent.GetFirstCharIndexFromLine(i);
                    if (idx < 0) continue; // 行索引到字符索引失败，跳过

                    // GetPositionFromCharIndex 返回该行首字符在 RichTextBox 客户区的像素坐标
                    var pos = txtContent.GetPositionFromCharIndex(idx);

                    // 计算文本右对齐的 X 坐标（预留 4px 内边距）
                    string text = (i + 1).ToString();
                    var sz = e.Graphics.MeasureString(text, font);
                    float x = pnlLineNumbersCanvas.ClientSize.Width - sz.Width - 4;

                    // 在 canvas 的坐标系中绘制行号；canvas 已经根据文本滚动做了像素偏移
                    e.Graphics.DrawString(text, font, brush, x, pos.Y);
                }
            }
            catch
            {
                // 忽略绘制异常，避免影响主 UI 流程
            }
        }

        // 获取 RichTextBox 的首可见行（包装了 Win32 调用并做句柄检查）
        private int GetFirstVisibleLine(System.Windows.Forms.RichTextBox rtb)
        {
            if (rtb == null) return 0;
            if (!rtb.IsHandleCreated) return 0;

            try
            {
                // SendMessage 返回第一个可见行（int），在极端情况下可能为负或超出范围，调用处需做边界检查
                return SendMessage(rtb.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // 任何 Win32 错误都退回到 0
                return 0;
            }
        }

        // “用默认程序打开”按钮点击处理：使用 Shell 启动文件
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
                // UseShellExecute=true 表示使用系统关联的默认程序打开文件
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
