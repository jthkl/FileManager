using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FileManager.Models;
using FileManager.Storage;

namespace FileManager
{
    public partial class Form1 : Form
    {
        private MetadataStore _store;
        private List<FileEntry> _entries;
        private List<string> _categories;

        private const string ROOT_NODE_NAME = "__ROOT__";
        private const string ROOT_NODE_TEXT = "根分类";

        public Form1()
        {
            InitializeComponent();
            _store = new MetadataStore();

            // 运行时 UI 设置（把可变逻辑放在此文件，不修改 Designer）
            ApplyRuntimeUi();

            // 读取容器（包含 categories 与 entries）
            var container = _store.Load();
            _entries = container.Entries ?? new List<FileEntry>();
            _categories = container.Categories ?? new List<string>();

            // TreeView 使用 "/" 作为分隔符
            treeCategories.PathSeparator = "/";

            PopulateCategories();
            PopulateGrid();
            lblMetadataPath.Text = $"元数据: {_store.MdPath}";
        }

        // 在运行时设置表头、事件绑定等，不写入 Designer
        private void ApplyRuntimeUi()
        {
            // 设置列头为中文
            if (dgvFiles.Columns.Contains("colId")) dgvFiles.Columns["colId"].HeaderText = "Id";
            if (dgvFiles.Columns.Contains("colName")) dgvFiles.Columns["colName"].HeaderText = "名称";
            if (dgvFiles.Columns.Contains("colExt")) dgvFiles.Columns["colExt"].HeaderText = "类型";
            if (dgvFiles.Columns.Contains("colSize")) dgvFiles.Columns["colSize"].HeaderText = "大小";
            if (dgvFiles.Columns.Contains("colCreated")) dgvFiles.Columns["colCreated"].HeaderText = "创建时间";
            if (dgvFiles.Columns.Contains("colPath")) dgvFiles.Columns["colPath"].HeaderText = "存储位置";
            if (dgvFiles.Columns.Contains("colDesc")) dgvFiles.Columns["colDesc"].HeaderText = "说明";

            // 隐藏 Id 列
            if (dgvFiles.Columns.Contains("colId")) dgvFiles.Columns["colId"].Visible = false;

            // 绑定状态栏双击事件在运行时（不要在 Designer 里）
            lblMetadataPath.DoubleClick -= lblMetadataPath_DoubleClick;
            lblMetadataPath.DoubleClick += lblMetadataPath_DoubleClick;
        }

        private void PopulateCategories()
        {
            treeCategories.Nodes.Clear();

            // 根节点
            var root = new TreeNode(ROOT_NODE_TEXT) { Name = ROOT_NODE_NAME };
            treeCategories.Nodes.Add(root);

            var ordered = _categories.Distinct().OrderBy(s => s).ToList();
            var nodeMap = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var fullPath in ordered)
            {
                if (string.IsNullOrWhiteSpace(fullPath)) continue;
                var parts = fullPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                string acc = "";
                TreeNodeCollection nodes = root.Nodes; // 所有分类放在根节点之下
                for (int i = 0; i < parts.Length; i++)
                {
                    acc = i == 0 ? parts[i] : (acc + "/" + parts[i]);
                    if (!nodeMap.TryGetValue(acc, out var node))
                    {
                        node = new TreeNode(parts[i]) { Name = acc }; // Name 存储实际 fullPath，便于匹配
                        nodes.Add(node);
                        nodeMap[acc] = node;
                    }
                    nodes = node.Nodes;
                }
            }

            treeCategories.ExpandAll();
        }

        private void PopulateGrid()
        {
            dgvFiles.Rows.Clear();
            // 使用 SelectedNode.Name（保存实际分类 fullPath），避免因根节点的显示前缀影响匹配
            string selCat = treeCategories.SelectedNode?.Name ?? null;
            // 当选中根节点或无选择时 selCat 可能为 "__ROOT__" 或 null，应视为无分类筛选
            if (string.Equals(selCat, ROOT_NODE_NAME, StringComparison.OrdinalIgnoreCase)) selCat = null;

            var list = _entries.AsEnumerable();
            if (!string.IsNullOrEmpty(selCat))
            {
                list = list.Where(x => string.Equals(x.Category, selCat, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var e in list)
            {
                dgvFiles.Rows.Add(e.Id, e.Name, e.Extension, FormatSize(e.Size), e.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), e.Path, e.Description);
            }
        }

        private string FormatSize(long size)
        {
            if (size < 1024) return $"{size} B";
            if (size < 1024 * 1024) return $"{size / 1024.0:F1} KB";
            if (size < 1024 * 1024 * 1024) return $"{size / (1024.0 * 1024):F1} MB";
            return $"{size / (1024.0 * 1024 * 1024):F1} GB";
        }

        private void btnAddCategory_Click(object sender, EventArgs e)
        {
            var name = Prompt.ShowDialog("输入分类名称：", "新增分类");
            if (string.IsNullOrWhiteSpace(name)) return;

            // 计算要添加的完整路径（若选中父节点则添加为子分类）
            // 使用 SelectedNode.Name（Name 保存实际 fullPath），并把根节点视为无父
            var parentName = treeCategories.SelectedNode?.Name;
            if (string.Equals(parentName, ROOT_NODE_NAME, StringComparison.OrdinalIgnoreCase)) parentName = null;
            string newFull = string.IsNullOrEmpty(parentName) ? name : (parentName + "/" + name);

            // 不允许重复
            if (_categories.Any(c => string.Equals(c, newFull, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("分类已存在。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _categories.Add(newFull);
            // 立即保存（分类即使没有文件也需保存）
            SaveMetadata();

            PopulateCategories();

            // 选中刚创建的节点（FindNodeByFullPath 使用 Name 匹配）
            var node = FindNodeByFullPath(newFull);
            if (node != null) treeCategories.SelectedNode = node;
        }

        private void btnRemoveCategory_Click(object sender, EventArgs e)
        {
            var node = treeCategories.SelectedNode;
            if (node == null) return;

            // 使用 node.Name 作为实际分类标识；若选中根节点则直接返回（不允许删除根）
            if (string.Equals(node.Name, ROOT_NODE_NAME, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("无法删除根分类。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var targetFull = node.Name;

            // 找出属于该分类的文件（精确匹配）
            var filesInCategory = _entries.Where(x => string.Equals(x.Category, targetFull, StringComparison.OrdinalIgnoreCase)).ToList();

            // 如果有文件，先询问确认（提示将删除这些文件的快捷方式，但不会删除磁盘上的文件）
            if (filesInCategory.Count > 0)
            {
                var msg = $"分类 \"{targetFull}\" 包含 {filesInCategory.Count} 个文件。\n删除该分类将把这些文件的分类置空并尝试删除它们的快捷方式（不会删除文件本身）。\n是否继续？";
                if (MessageBox.Show(msg, "确认删除分类", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }

            // 删除属于该分类的文件快捷方式（在常见位置搜索 .lnk）
            foreach (var ent in filesInCategory)
            {
                try
                {
                    DeleteShortcutsForFile(ent.Path);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"删除快捷方式时出错: {ex.Message}");
                }
            }

            // 从类别列表中移除精确匹配的路径（不自动删除子分类）
            _categories = _categories.Where(c => !string.Equals(c, targetFull, StringComparison.OrdinalIgnoreCase)).ToList();

            // 对属于该分类（精确匹配）的文件，转为 null（表示无分类），避免自动恢复“未分类”
            foreach (var ent in filesInCategory)
            {
                ent.Category = null;
            }

            SaveMetadata();
            PopulateCategories();
            PopulateGrid();
        }

        private void btnAddFile_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Multiselect = true;
                ofd.CheckFileExists = true;
                if (ofd.ShowDialog() != DialogResult.OK) return;
                // 使用 SelectedNode.Name（Name 保存实际分类 fullPath）；根节点视为无分类
                var selName = treeCategories.SelectedNode?.Name;
                if (string.Equals(selName, ROOT_NODE_NAME, StringComparison.OrdinalIgnoreCase)) selName = null;
                foreach (var f in ofd.FileNames)
                {
                    AddOrUpdateEntry(f, selName);
                }
                SaveMetadata();
                PopulateCategories();
                PopulateGrid();
            }
        }

        private void AddOrUpdateEntry(string filePath, string category)
        {
            try
            {
                var fi = new FileInfo(filePath);
                var name = fi.Name;
                var ext = fi.Extension;
                var existing = _entries.FirstOrDefault(x => string.Equals(x.Path, filePath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Name = name;
                    existing.Extension = ext;
                    existing.Size = fi.Length;
                    existing.CreatedUtc = fi.CreationTimeUtc;
                    existing.Category = _categories.Contains(category) ? category : null;
                }
                else
                {
                    // 如果目标分类不存在（可能被删除或未选择），使用 null 表示无分类
                    if (category == null || !_categories.Contains(category)) category = null;

                    _entries.Add(new FileEntry
                    {
                        Category = category,
                        Name = name,
                        Path = filePath,
                        Extension = ext,
                        Size = fi.Length,
                        CreatedUtc = fi.CreationTimeUtc,
                        Description = ""
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加文件失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnRemoveFile_Click(object sender, EventArgs e)
        {
            if (dgvFiles.SelectedRows.Count == 0) return;
            if (MessageBox.Show("确认从管理列表中删除选中记录？（不会删除磁盘上的文件）", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (DataGridViewRow row in dgvFiles.SelectedRows)
            {
                if (row.Cells["colId"].Value is Guid id)
                {
                    var entryToRemove = _entries.FirstOrDefault(ent => ent.Id == id);
                    if (entryToRemove != null) _entries.Remove(entryToRemove);
                }
                else
                {
                    var idStr = row.Cells["colId"].Value?.ToString();
                    if (Guid.TryParse(idStr, out var g))
                    {
                        var entryToRemove = _entries.FirstOrDefault(ent => ent.Id == g);
                        if (entryToRemove != null) _entries.Remove(entryToRemove);
                    }
                }
            }
            SaveMetadata();
            PopulateCategories();
            PopulateGrid();
        }

        private void btnEditDesc_Click(object sender, EventArgs e)
        {
            if (dgvFiles.SelectedRows.Count != 1) { MessageBox.Show("请选择单个记录进行编辑说明。"); return; }
            var row = dgvFiles.SelectedRows[0];
            var idVal = row.Cells["colId"].Value?.ToString();
            if (!Guid.TryParse(idVal, out var id)) return;
            var entry = _entries.FirstOrDefault(x => x.Id == id);
            if (entry == null) return;
            var newDesc = Prompt.ShowDialog("输入说明：", "编辑说明", entry.Description);
            if (newDesc == null) return;
            entry.Description = newDesc;
            SaveMetadata();
            PopulateGrid();
        }

        private void dgvFiles_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var idVal = dgvFiles.Rows[e.RowIndex].Cells["colId"].Value?.ToString();
            if (!Guid.TryParse(idVal, out var id)) return;
            var entry = _entries.FirstOrDefault(x => x.Id == id);
            if (entry == null) return;
            TryOpenEntry(entry);
        }

        private void TryOpenEntry(FileEntry entry)
        {
            try
            {
                if (entry.IsTextBased())
                {
                    // 打开自带查看器
                    if (!File.Exists(entry.Path))
                    {
                        MessageBox.Show("文件不存在或无法访问。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    var viewer = new TextViewerForm(entry.Path);
                    viewer.Show(this);
                }
                else
                {
                    // 用系统默认程序打开
                    var psi = new ProcessStartInfo(entry.Path) { UseShellExecute = true };
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCreateShortcut_Click(object sender, EventArgs e)
        {
            if (dgvFiles.SelectedRows.Count == 0) return;
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "选择保存快捷方式的文件夹";
                if (fbd.ShowDialog() != DialogResult.OK) return;
                var targetFolder = fbd.SelectedPath;
                foreach (DataGridViewRow row in dgvFiles.SelectedRows)
                {
                    var idVal = row.Cells["colId"].Value?.ToString();
                    if (!Guid.TryParse(idVal, out var id)) continue;
                    var entry = _entries.FirstOrDefault(x => x.Id == id);
                    if (entry == null) continue;
                    var shortcutPath = System.IO.Path.Combine(targetFolder, entry.Name + ".lnk");
                    try
                    {
                        CreateShortcut(shortcutPath, entry.Path);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"创建快捷方式失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                MessageBox.Show("快捷方式已创建（如有权限问题请以管理员身份或选择其他目录）。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void treeCategories_AfterSelect(object sender, TreeViewEventArgs e)
        {
            PopulateGrid();
        }

        // 创建 .lnk 快捷方式（使用 WScript.Shell）
        private void CreateShortcut(string shortcutPath, string targetPath)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) throw new InvalidOperationException("无法访问 WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                var shortcut = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                // 设置 TargetPath
                var scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                // 保存
                scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, new object[] { });
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        // 删除指向指定文件的常见位置的快捷方式（递归查找 .lnk）
        private void DeleteShortcutsForFile(string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath)) return;

            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            }.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var folder in folders)
            {
                try
                {
                    DeleteShortcutsInDirectory(folder, targetPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"扫描快捷方式目录出错 ({folder}): {ex.Message}");
                }
            }
        }

        private void DeleteShortcutsInDirectory(string dir, string targetPath)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    var resolved = GetShortcutTarget(lnk);
                    if (string.IsNullOrEmpty(resolved)) continue;
                    // 比较目标路径（规范化）
                    try
                    {
                        var rFull = Path.GetFullPath(resolved).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var tFull = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (string.Equals(rFull, tFull, StringComparison.OrdinalIgnoreCase))
                        {
                            // 删除快捷方式文件
                            File.Delete(lnk);
                            Debug.WriteLine($"已删除快捷方式: {lnk}");
                        }
                    }
                    catch (Exception exInner)
                    {
                        Debug.WriteLine($"比较路径或删除快捷方式时出错 ({lnk}): {exInner.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"读取快捷方式目标出错 ({lnk}): {ex.Message}");
                }
            }
        }

        // 使用 WScript.Shell 读取 .lnk 的 TargetPath
        private string GetShortcutTarget(string shortcutPath)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return null;
            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(t);
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                var target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                return target;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }

        // 保存当前 entries 与 categories 到元数据
        private void SaveMetadata()
        {
            var container = new MetadataContainer
            {
                Categories = _categories.Distinct().OrderBy(s => s).ToList(),
                Entries = _entries
            };
            _store.Save(container);
            lblMetadataPath.Text = $"元数据: {_store.MdPath}";
        }

        // 根据 fullPath 查找 TreeNode（Name 存储 fullPath）
        private TreeNode FindNodeByFullPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            var stack = new Stack<TreeNode>();
            foreach (TreeNode n in treeCategories.Nodes) stack.Push(n);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (string.Equals(node.Name, fullPath, StringComparison.OrdinalIgnoreCase)) return node;
                foreach (TreeNode c in node.Nodes) stack.Push(c);
            }
            return null;
        }

        // 状态栏双击打开元数据文件
        private void lblMetadataPath_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                var md = _store?.MdPath;
                if (string.IsNullOrEmpty(md))
                {
                    MessageBox.Show("未找到元数据文件路径。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!File.Exists(md))
                {
                    MessageBox.Show($"元数据文件不存在：{md}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var psi = new ProcessStartInfo(md) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开元数据文件：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void dgvFiles_Click(object sender, EventArgs e)
        {
            //获得鼠标单击的位置
            var hit = dgvFiles.HitTest(((MouseEventArgs)e).X, ((MouseEventArgs)e).Y);
            if (hit != null)
            {
                //判断是否单击在某一行上
                if (hit.RowIndex >= 0)
                {
                    //获得表头
                    var column = dgvFiles.Columns[hit.ColumnIndex];
                    if (column != null)
                    {                       
                        var liename = column.HeaderText;
                        if (liename == "存储位置")
                        {
                            //使用资源管理器打开文件所在位置
                            var idVal = dgvFiles.Rows[hit.RowIndex].Cells["colId"].Value?.ToString();
                            if (!Guid.TryParse(idVal, out var id)) return;
                            var entry = _entries.FirstOrDefault(x => x.Id == id);
                            
                            if (entry != null && MessageBox.Show("是否在资源管理器中打开文件位置？", "询问", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes )
                            {
                                var folderPath = Path.GetDirectoryName(entry.Path);
                                if (Directory.Exists(folderPath))
                                {
                                    Process.Start("explorer.exe", folderPath);
                                }
                                else
                                {
                                    MessageBox.Show("文件所在目录不存在或无法访问。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            }
                        }
                    }

                }

            }

        }
    }
        // 简单的同步输入框
        public static class Prompt
    {
        public static string ShowDialog(string text, string caption, string defaultValue = "")
        {
            using (var form = new Form())
            {
                form.Width = 400;
                form.Height = 150;
                form.Text = caption;
                var lbl = new Label() { Left = 10, Top = 10, Text = text, AutoSize = true };
                var txt = new TextBox() { Left = 10, Top = 35, Width = 360, Text = defaultValue };
                var ok = new Button() { Text = "OK", Left = 200, Width = 80, Top = 70, DialogResult = DialogResult.OK };
                var cancel = new Button() { Text = "取消", Left = 290, Width = 80, Top = 70, DialogResult = DialogResult.Cancel };
                form.Controls.Add(lbl);
                form.Controls.Add(txt);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                if (form.ShowDialog() == DialogResult.OK) return txt.Text;
                return null;
            }
        }
    }
}
