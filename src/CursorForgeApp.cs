using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CursorForge;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new CursorForgeForm());
    }
}

internal sealed class CursorForgeForm : Form
{
    private const string OutputName = "~右键安装.inf";
    private const string EmptyChoice = "未指定";

    private static readonly RoleDef[] Roles =
    [
        new("pointer", "正常选择", "Arrow", ["正常选择", "arrow", "normal", "pointer", "select", "cursor", "standard", "default", "mouse", "默认", "正常"]),
        new("help", "帮助选择", "Help", ["帮助选择", "help", "helpsel", "question", "帮助"]),
        new("work", "后台运行", "AppStarting", ["后台运行", "working", "work", "appstarting", "app_starting", "background", "start1", "后台"]),
        new("busy", "忙", "Wait", ["忙", "busy", "wait", "loading", "progress", "start", "等待"]),
        new("cross", "精准选择", "Crosshair", ["精准选择", "精确选择", "cross", "crosshair", "precision", "精准", "精确"]),
        new("text", "文本选择", "IBeam", ["文本选择", "text", "ibeam", "i-beam", "beam", "文本"]),
        new("hand", "手写", "NWPen", ["手写", "handwriting", "pen", "nwpen", "write", "笔"]),
        new("unavailiable", "不可用", "No", ["不可用", "unavailable", "unavailiable", "no", "not", "forbidden", "denied", "禁止"]),
        new("vert", "垂直调整大小", "SizeNS", ["垂直调整大小", "垂直调整", "vresize", "vert", "vertical", "sizens", "size_ns", "上下", "垂直"]),
        new("horz", "水平调整大小", "SizeWE", ["水平调整大小", "水平调整", "hresize", "hori", "horz", "horizontal", "sizewe", "size_we", "左右", "水平"]),
        new("dgn1", "沿对角线调整大小1", "SizeNWSE", ["沿对角线调整大小1", "沿对角线调整大小 1", "对角线调整大小1", "对角线1", "d1resize", "diag1", "dgn1", "nwse", "sizenwse", "左上"]),
        new("dgn2", "沿对角线调整大小2", "SizeNESW", ["沿对角线调整大小2", "沿对角线调整大小 2", "对角线调整大小2", "对角线2", "d2resize", "diag2", "dgn2", "nesw", "sizenesw", "右上"]),
        new("move", "移动", "SizeAll", ["移动", "move", "sizeall", "all"]),
        new("alternate", "候选", "UpArrow", ["候选", "候选选择", "alternate", "arrowup", "up", "uparrow", "mouse"]),
        new("link", "链接选择", "Hand", ["链接选择", "link", "hand", "hyperlink", "链接", "超链接"]),
        new("person", "个人选择", "Person", ["个人选择", "人员选择", "person", "people", "user", "用户", "人员", "个人"]),
        new("pin", "位置选择", "Pin", ["位置选择", "location", "loc", "pin", "place", "位置", "定位"])
    ];

    private static readonly string[] SchemeOrder =
    [
        "pointer", "help", "work", "busy", "cross", "text", "hand", "unavailiable",
        "vert", "horz", "dgn1", "dgn2", "move", "alternate", "link", "person", "pin"
    ];

    private readonly List<CursorFile> files = [];
    private readonly Dictionary<string, string> mappings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CursorPreview> previewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 120 };

    private DataGridView grid = null!;
    private Label schemeNameLabel = null!;
    private Label fileCountLabel = null!;
    private Label matchedCountLabel = null!;
    private Label saveStatusLabel = null!;
    private Button clearButton = null!;
    private Button saveButton = null!;
    private string folderPath = "";
    private string schemeName = "";
    private int previewElapsedMs;

    public CursorForgeForm()
    {
        Text = "Cursor INF Maker";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1080, 1000);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Ui.Shell;
        Font = new Font("Microsoft YaHei UI", 9F);
        AllowDrop = true;

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        BuildLayout();
        ResetState();

        previewTimer.Tick += (_, _) => AdvancePreviewFrame();
        DragEnter += HandleDragEnter;
        DragDrop += HandleDragDrop;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildTitleBar(), 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);
        root.Controls.Add(BuildGrid(), 0, 2);
    }

    private Control BuildTitleBar()
    {
        var titleBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 16)
        };
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));

        var titleBlock = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };

        titleBlock.Controls.Add(new Label
        {
            Text = "Cursor INF Maker",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            ForeColor = Ui.Text,
            Margin = new Padding(0, 8, 0, 0)
        });
        titleBar.Controls.Add(titleBlock, 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 14, 0, 0)
        };
        titleBar.Controls.Add(actions, 1, 0);

        saveButton = MakeButton("保存安装inf文件", 142, Ui.Primary, Color.White);
        saveButton.Click += (_, _) => SaveInf();
        actions.Controls.Add(saveButton);

        var chooseButton = MakeButton("选择文件夹", 104, Color.White, Ui.Text);
        chooseButton.Click += (_, _) => ChooseFolder();
        actions.Controls.Add(chooseButton);

        clearButton = MakeButton("清空", 74, Color.White, Ui.Text);
        clearButton.Click += (_, _) => ResetState();
        actions.Controls.Add(clearButton);

        return titleBar;
    }

    private Control BuildSummary()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Ui.Panel,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var summary = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 13, 0, 0),
            Margin = new Padding(0)
        };
        schemeNameLabel = MakeSummaryLabel();
        fileCountLabel = MakeSummaryLabel();
        matchedCountLabel = MakeSummaryLabel();
        summary.Controls.Add(schemeNameLabel);
        summary.Controls.Add(fileCountLabel);
        summary.Controls.Add(matchedCountLabel);
        panel.Controls.Add(summary, 0, 0);

        saveStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true,
            ForeColor = Ui.Muted,
            Margin = new Padding(0),
            Padding = new Padding(8, 0, 2, 0)
        };
        panel.Controls.Add(saveStatusLabel, 1, 0);
        return panel;
    }

    private Control BuildGrid()
    {
        grid = new DisplayGrid
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeColumns = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = Ui.Panel,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            EditMode = DataGridViewEditMode.EditProgrammatically,
            EnableHeadersVisualStyles = false,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            ScrollBars = ScrollBars.None,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            GridColor = Ui.Line,
            Margin = new Padding(0, 10, 0, 0),
            RowTemplate = { Height = 46 },
            TabStop = false
        };
        grid.ColumnHeadersHeight = 38;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Ui.Header;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Text;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(12, 0, 12, 0);
        grid.DefaultCellStyle.BackColor = Ui.Panel;
        grid.DefaultCellStyle.ForeColor = Ui.Text;
        grid.DefaultCellStyle.SelectionBackColor = Ui.Panel;
        grid.DefaultCellStyle.SelectionForeColor = Ui.Text;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(250, 251, 253);
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Ui.Text;

        grid.Columns.Add(MakeTextColumn("Role", "项目", 240, DataGridViewContentAlignment.MiddleLeft));
        var previewColumn = new DataGridViewImageColumn
        {
            Name = "Preview",
            HeaderText = "预览",
            ReadOnly = true,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Width = 120,
            ImageLayout = DataGridViewImageCellLayout.Zoom
        };
        previewColumn.DefaultCellStyle.NullValue = null!;
        previewColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        previewColumn.DefaultCellStyle.Padding = new Padding(0);
        previewColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        previewColumn.HeaderCell.Style.Padding = new Padding(0);
        grid.Columns.Add(previewColumn);
        grid.Columns.Add(MakeTextColumn("File", "匹配文件", 680, DataGridViewContentAlignment.MiddleCenter));
        grid.CellPainting += PaintPreviewCell;
        grid.RowPostPaint += PaintMissingRowBorder;
        grid.CellMouseDown += (_, _) => ClearGridFocus();
        grid.SelectionChanged += (_, _) => ClearGridFocus();

        return grid;
    }

    private static DataGridViewTextBoxColumn MakeTextColumn(string name, string headerText, int width, DataGridViewContentAlignment alignment)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = headerText,
            ReadOnly = true,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Width = width,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Padding = new Padding(12, 0, 12, 0),
                Alignment = alignment
            }
        };
        column.HeaderCell.Style.Alignment = alignment;
        return column;
    }

    private void PaintPreviewCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Preview")
        {
            return;
        }

        e.Handled = true;
        Graphics graphics = e.Graphics!;
        GraphicsState state = graphics.Save();
        try
        {
            Color backColor = IsMissingRow(e.RowIndex) ? Ui.MissingBack : e.RowIndex % 2 == 0 ? Ui.Panel : Ui.AltRow;
            using (var backBrush = new SolidBrush(backColor))
            {
                graphics.FillRectangle(backBrush, e.CellBounds);
            }

            using (var linePen = new Pen(Ui.Line))
            {
                graphics.DrawLine(linePen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            }

            var box = new Rectangle(e.CellBounds.Left + (e.CellBounds.Width - 40) / 2, e.CellBounds.Top + (e.CellBounds.Height - 40) / 2, 40, 40);
            DrawPreviewBackground(graphics, box);

            Image? image = GetPreviewFrameForRow(e.RowIndex);
            if (image != null)
            {
                Rectangle target = FitImage(image.Size, Rectangle.Inflate(box, -4, -4));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(image, target);
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private Image? GetPreviewFrameForRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows[rowIndex].Tag is not RoleDef role)
        {
            return null;
        }

        CursorFile? file = FindCursorFile(GetMapping(role.Key));
        CursorPreview? preview = file == null ? null : GetPreview(file);
        if (preview == null)
        {
            return null;
        }

        return preview.FrameAt(preview.IsAnimated ? previewElapsedMs : 0);
    }

    private void PaintMissingRowBorder(object? sender, DataGridViewRowPostPaintEventArgs e)
    {
        if (!IsMissingRow(e.RowIndex))
        {
            return;
        }

        Rectangle bounds = grid.GetRowDisplayRectangle(e.RowIndex, false);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        float left = 0.5f;
        float right = Math.Min(grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible), grid.ClientSize.Width) - 0.5f;
        float top = bounds.Top + 0.5f;
        float bottom = bounds.Bottom - 1.5f;

        GraphicsState state = e.Graphics.Save();
        e.Graphics.SetClip(grid.ClientRectangle);
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        using var borderPen = new Pen(Ui.MissingBorder);
        e.Graphics.DrawLine(borderPen, left, top, right, top);
        e.Graphics.DrawLine(borderPen, left, bottom, right, bottom);
        e.Graphics.DrawLine(borderPen, left, top, left, bottom);
        e.Graphics.DrawLine(borderPen, right, top, right, bottom);
        e.Graphics.Restore(state);
    }

    private static void DrawPreviewBackground(Graphics graphics, Rectangle box)
    {
        using var light = new SolidBrush(Color.White);
        using var dark = new SolidBrush(Color.FromArgb(235, 238, 243));
        graphics.FillRectangle(light, box);

        const int cell = 6;
        for (int y = box.Top; y < box.Bottom; y += cell)
        {
            for (int x = box.Left; x < box.Right; x += cell)
            {
                if (((x - box.Left) / cell + (y - box.Top) / cell) % 2 == 0)
                {
                    graphics.FillRectangle(dark, x, y, Math.Min(cell, box.Right - x), Math.Min(cell, box.Bottom - y));
                }
            }
        }

        using var border = new Pen(Ui.PreviewBorder);
        graphics.DrawRectangle(border, box);
    }

    private static Rectangle FitImage(Size imageSize, Rectangle bounds)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0)
        {
            return bounds;
        }

        float scale = Math.Min(bounds.Width / (float)imageSize.Width, bounds.Height / (float)imageSize.Height);
        int width = Math.Max(1, (int)Math.Round(imageSize.Width * scale));
        int height = Math.Max(1, (int)Math.Round(imageSize.Height * scale));
        return new Rectangle(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
    }

    private static Label MakeSummaryLabel()
    {
        return new Label
        {
            AutoSize = true,
            ForeColor = Ui.Text,
            Margin = new Padding(0, 0, 24, 0)
        };
    }

    private static Button MakeButton(string text, int width, Color backColor, Color foreColor)
    {
        var button = new RoundedButton
        {
            Text = text,
            Width = width,
            Height = 34,
            Margin = new Padding(8, 0, 0, 0),
            BaseColor = backColor,
            ForeColor = foreColor,
            BorderColor = backColor == Ui.Primary ? Ui.PrimaryDark : Ui.ButtonBorder,
            Radius = 8
        };
        return button;
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含 .cur / .ani 的光标文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(folderPath))
        {
            dialog.SelectedPath = folderPath;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadFolder(dialog.SelectedPath);
        }
    }

    private void LoadFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        DisposePreviews();
        folderPath = path;
        schemeName = new DirectoryInfo(path).Name;
        ClearSaveStatus();
        files.Clear();
        mappings.Clear();

        foreach (string file in EnumerateCursorFiles(path))
        {
            files.Add(new CursorFile(file));
        }

        files.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        AutoMap();
        RefreshGrid();
        RefreshUi();
    }

    private static IEnumerable<string> EnumerateCursorFiles(string folder)
    {
        IEnumerable<string> localFiles;
        try
        {
            localFiles = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (string file in localFiles)
        {
            if (IsCursor(file))
            {
                yield return file;
            }
        }
    }

    private void ResetState()
    {
        DisposePreviews();
        files.Clear();
        mappings.Clear();
        folderPath = "";
        schemeName = "";
        ClearSaveStatus();
        RefreshGrid();
        RefreshUi();
    }

    private void AutoMap()
    {
        mappings.Clear();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (RoleDef role in Roles)
        {
            CursorFile? best = null;
            int bestScore = 0;

            foreach (CursorFile file in files)
            {
                if (used.Contains(file.FullPath))
                {
                    continue;
                }

                int score = ScoreFile(role, file.Name);
                if (score > bestScore)
                {
                    best = file;
                    bestScore = score;
                }
            }

            if (best != null && bestScore >= 58)
            {
                mappings[role.Key] = best.Name;
                used.Add(best.FullPath);
            }
            else
            {
                mappings[role.Key] = "";
            }
        }
    }

    private void RefreshGrid()
    {
        grid.Rows.Clear();
        foreach (RoleDef role in Roles)
        {
            string name = GetMapping(role.Key);
            CursorFile? file = FindCursorFile(name);
            if (file != null)
            {
                GetPreview(file);
            }

            int index = grid.Rows.Add(role.Label, DBNull.Value, string.IsNullOrEmpty(name) ? EmptyChoice : name);
            grid.Rows[index].Tag = role;
            grid.Rows[index].Height = 46;
            ApplyRowState(grid.Rows[index], IsMissingMapping(name), index);
        }
        ClearGridFocus();
        UpdatePreviewAnimationTimer();
    }

    private void ApplyRowState(DataGridViewRow row, bool isMissing, int index)
    {
        Color backColor = isMissing ? Ui.MissingBack : index % 2 == 0 ? Ui.Panel : Ui.AltRow;
        Color foreColor = isMissing ? Ui.MissingText : Ui.Text;
        row.DefaultCellStyle.BackColor = backColor;
        row.DefaultCellStyle.SelectionBackColor = backColor;
        row.DefaultCellStyle.ForeColor = foreColor;
        row.DefaultCellStyle.SelectionForeColor = foreColor;

        foreach (DataGridViewCell cell in row.Cells)
        {
            cell.Style.BackColor = backColor;
            cell.Style.SelectionBackColor = backColor;
            cell.Style.ForeColor = foreColor;
            cell.Style.SelectionForeColor = foreColor;
        }
    }

    private bool IsMissingMapping(string name)
    {
        return files.Count > 0 && string.IsNullOrEmpty(name);
    }

    private bool IsMissingRow(int rowIndex)
    {
        return rowIndex >= 0
            && rowIndex < grid.Rows.Count
            && grid.Rows[rowIndex].Tag is RoleDef role
            && IsMissingMapping(GetMapping(role.Key));
    }

    private void ClearGridFocus()
    {
        grid.ClearSelection();
        try
        {
            grid.CurrentCell = null;
        }
        catch
        {
        }
    }

    private void RefreshUi()
    {
        bool hasFiles = files.Count > 0;
        saveButton.Enabled = hasFiles;
        clearButton.Visible = hasFiles;
        schemeNameLabel.Text = "方案：" + (string.IsNullOrEmpty(schemeName) ? "—" : schemeName);
        fileCountLabel.Text = "光标文件：" + files.Count;
        int matched = Roles.Count(role => !string.IsNullOrEmpty(GetMapping(role.Key)));
        matchedCountLabel.Text = $"匹配：{matched} / {Roles.Length}";

        if (!hasFiles && !string.IsNullOrEmpty(folderPath))
        {
            ShowSaveStatus("未找到 .cur / .ani 文件", Ui.WarnText);
            return;
        }
    }

    private string GetMapping(string key)
    {
        return mappings.TryGetValue(key, out string? value) ? value : "";
    }

    private CursorFile? FindCursorFile(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return files.FirstOrDefault(file => string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void SaveInf()
    {
        if (files.Count == 0 || !Directory.Exists(folderPath))
        {
            return;
        }

        try
        {
            string target = Path.Combine(folderPath, OutputName);
            File.WriteAllText(target, BuildInf(), Encoding.Unicode);
            ShowSaveStatus("保存成功：" + OutputName, Ui.SaveOkText);
        }
        catch (Exception ex)
        {
            ShowSaveStatus("保存失败：" + ex.Message, Ui.WarnText);
        }
    }

    private void ShowSaveStatus(string text, Color color)
    {
        saveStatusLabel.Text = text;
        saveStatusLabel.ForeColor = color;
    }

    private void ClearSaveStatus()
    {
        saveStatusLabel.Text = "";
        saveStatusLabel.ForeColor = Ui.Muted;
    }

    private string BuildInf()
    {
        string scheme = SafeInf(string.IsNullOrEmpty(schemeName) ? "Custom Cursor" : schemeName);
        string curDir = "Cursors\\" + scheme;
        Dictionary<string, string> presentRoles = SchemeOrder
            .Select(key => new { Key = key, FileName = GetMapping(key) })
            .Where(item => !string.IsNullOrEmpty(item.FileName))
            .ToDictionary(item => item.Key, item => item.FileName, StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>
        {
            "[Version]",
            "signature=\"$CHICAGO$\"",
            "",
            "[DefaultInstall]",
            "CopyFiles = Scheme.Cur",
            "AddReg    = Scheme.Reg,Wreg",
            "RunPostSetupCommands = OpenMouseSettings",
            "",
            "[DestinationDirs]",
            "Scheme.Cur = 10,\"%CUR_DIR%\"",
            "",
            "[Scheme.Reg]",
            "HKCU,\"Control Panel\\Cursors\\Schemes\",\"%SCHEME_NAME%\",,\"" + BuildSchemeList(presentRoles) + "\"",
            "",
            "[Wreg]",
            "HKCU,\"Control Panel\\Cursors\",,0x00020000,\"%SCHEME_NAME%\""
        };

        foreach (RoleDef role in Roles)
        {
            if (presentRoles.ContainsKey(role.Key))
            {
                lines.Add($"HKCU,\"Control Panel\\Cursors\",{role.Reg},0x00020000,\"%10%\\%CUR_DIR%\\%{role.Key}%\"");
            }
        }

        lines.Add("HKLM,\"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Runonce\\Setup\\\",\"\",,\"rundll32.exe shell32.dll,Control_RunDLL main.cpl @0,1\"");
        lines.Add("");
        lines.Add("[Scheme.Cur]");
        foreach (CursorFile file in files)
        {
            lines.Add("\"" + SafeInf(file.Name) + "\"");
        }

        lines.Add("");
        lines.Add("[OpenMouseSettings]");
        lines.Add("rundll32.exe shell32.dll,Control_RunDLL main.cpl @0,1");
        lines.Add("");
        lines.Add("[Strings]");
        lines.Add("CUR_DIR         = \"" + SafeInf(curDir) + "\"");
        lines.Add("SCHEME_NAME     = \"" + scheme + "\"");

        foreach (string key in SchemeOrder)
        {
            if (presentRoles.TryGetValue(key, out string? fileName))
            {
                lines.Add(key.PadRight(15) + " = \"" + SafeInf(fileName) + "\"");
            }
        }

        lines.Add("");
        return string.Join("\r\n", lines);
    }

    private static string BuildSchemeList(IReadOnlyDictionary<string, string> presentRoles)
    {
        return string.Join(",", SchemeOrder.Select(key => presentRoles.ContainsKey(key) ? "%10%\\%CUR_DIR%\\%" + key + "%" : ""));
    }

    private void AdvancePreviewFrame()
    {
        previewElapsedMs += previewTimer.Interval;
        bool hasAnimatedPreview = false;

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not RoleDef role)
            {
                continue;
            }

            CursorFile? file = FindCursorFile(GetMapping(role.Key));
            CursorPreview? preview = file == null ? null : GetPreview(file);
            if (preview == null)
            {
                continue;
            }

            if (!preview.IsAnimated)
            {
                continue;
            }

            hasAnimatedPreview = true;
            DataGridViewCell previewCell = row.Cells["Preview"];
            grid.InvalidateCell(previewCell);
        }

        if (!hasAnimatedPreview)
        {
            previewTimer.Stop();
            return;
        }
    }

    private void UpdatePreviewAnimationTimer()
    {
        bool hasAnimatedPreview = previewCache.Values.Any(preview => preview.IsAnimated);
        if (hasAnimatedPreview)
        {
            previewTimer.Start();
        }
        else
        {
            previewTimer.Stop();
        }
    }

    private CursorPreview? GetPreview(CursorFile file)
    {
        if (previewCache.TryGetValue(file.FullPath, out CursorPreview? preview))
        {
            return preview;
        }

        preview = LoadCursorPreview(file.FullPath);
        if (preview != null)
        {
            previewCache[file.FullPath] = preview;
        }

        return preview;
    }

    private static CursorPreview? LoadCursorPreview(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            return Path.GetExtension(path).Equals(".ani", StringComparison.OrdinalIgnoreCase)
                ? CursorImageParser.LoadAniPreview(data)
                : CursorImageParser.LoadCurPreview(data);
        }
        catch
        {
            return null;
        }
    }

    private void DisposePreviews()
    {
        previewTimer.Stop();
        previewElapsedMs = 0;
        foreach (CursorPreview preview in previewCache.Values)
        {
            preview.Dispose();
        }

        previewCache.Clear();
    }

    private static bool IsCursor(string path)
    {
        return Regex.IsMatch(path, "\\.(cur|ani)$", RegexOptions.IgnoreCase);
    }

    private static int ScoreFile(RoleDef role, string name)
    {
        string low = NormalizeName(name);
        int best = 0;
        foreach (string word in role.Words)
        {
            string normalized = NormalizeName(word);
            if (low == normalized)
            {
                best = Math.Max(best, 100);
            }
            else if (low.EndsWith(normalized, StringComparison.Ordinal))
            {
                best = Math.Max(best, 82);
            }
            else if (low.Contains(normalized, StringComparison.Ordinal))
            {
                best = Math.Max(best, 58);
            }
        }

        return best;
    }

    private static string NormalizeName(string value)
    {
        return Regex.Replace(Path.GetFileNameWithoutExtension(value).ToLowerInvariant(), @"[\s_.\-]+", "");
    }

    private static string SafeInf(string value)
    {
        return (value ?? "").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private void HandleDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    private void HandleDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        string path = paths[0];
        if (Directory.Exists(path))
        {
            LoadFolder(path);
        }
        else if (File.Exists(path))
        {
            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                LoadFolder(parent);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposePreviews();
            previewTimer.Dispose();
        }

        base.Dispose(disposing);
    }

}

internal sealed class DisplayGrid : DataGridView
{
    public DisplayGrid()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        ClearGridState();
        Parent?.Focus();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        ClearGridState();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        ClearGridState();
    }

    protected override void OnSelectionChanged(EventArgs e)
    {
        base.OnSelectionChanged(e);
        if (SelectedCells.Count > 0 || SelectedRows.Count > 0 || SelectedColumns.Count > 0)
        {
            ClearGridState();
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        return true;
    }

    protected override bool ProcessDataGridViewKey(KeyEventArgs e)
    {
        return true;
    }

    private void ClearGridState()
    {
        ClearSelection();
        try
        {
            CurrentCell = null;
        }
        catch
        {
        }
    }
}

internal sealed class RoundedButton : Button
{
    private bool hovering;
    private bool pressing;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BaseColor { get; set; } = Color.White;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Ui.ButtonBorder;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 8;

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovering = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovering = false;
        pressing = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            pressing = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        pressing = false;
        base.OnMouseUp(mevent);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics graphics = pevent.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Parent?.BackColor ?? Ui.Shell);

        Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill = GetStateColor();
        Color border = Enabled ? BorderColor : Ui.DisabledBorder;
        Color text = Enabled ? ForeColor : Ui.DisabledText;

        using GraphicsPath path = RoundedRect(bounds, Radius);
        using var fillBrush = new SolidBrush(fill);
        using var borderPen = new Pen(border);
        graphics.FillPath(fillBrush, path);
        graphics.DrawPath(borderPen, path);

        Rectangle textBounds = new Rectangle(8, 0, Width - 16, Height);
        TextRenderer.DrawText(graphics, Text, Font, textBounds, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private Color GetStateColor()
    {
        if (!Enabled)
        {
            return Ui.DisabledBack;
        }

        if (pressing)
        {
            return Blend(BaseColor, Color.Black, BaseColor == Ui.Primary ? 0.16f : 0.08f);
        }

        if (hovering)
        {
            return Blend(BaseColor, BaseColor == Ui.Primary ? Color.White : Ui.Primary, BaseColor == Ui.Primary ? 0.12f : 0.06f);
        }

        return BaseColor;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Blend(Color color, Color mix, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        int r = (int)Math.Round(color.R + (mix.R - color.R) * amount);
        int g = (int)Math.Round(color.G + (mix.G - color.G) * amount);
        int b = (int)Math.Round(color.B + (mix.B - color.B) * amount);
        return Color.FromArgb(color.A, r, g, b);
    }
}

internal static class CursorImageParser
{
    private const int MaxAniFrames = 30;
    public static CursorPreview? LoadCurPreview(byte[] data)
    {
        Image? image = LoadCur(data);
        return image == null ? null : new CursorPreview([image], [120]);
    }

    public static Image? LoadCur(byte[] data)
    {
        if (data.Length < 6 || ReadU16(data, 0) != 0)
        {
            return null;
        }

        int count = ReadU16(data, 4);
        if (count <= 0)
        {
            return null;
        }

        var entries = new List<CursorEntry>();
        for (int i = 0; i < count; i++)
        {
            int offset = 6 + i * 16;
            if (offset + 16 > data.Length)
            {
                break;
            }

            int width = data[offset] == 0 ? 256 : data[offset];
            int height = data[offset + 1] == 0 ? 256 : data[offset + 1];
            int bytes = checked((int)ReadU32(data, offset + 8));
            int imageOffset = checked((int)ReadU32(data, offset + 12));
            if (bytes <= 0 || imageOffset < 0 || imageOffset + bytes > data.Length)
            {
                continue;
            }

            entries.Add(new CursorEntry(width, height, bytes, imageOffset));
        }

        foreach (CursorEntry entry in entries.OrderByDescending(item => item.Width * item.Height))
        {
            byte[] imageData = data.AsSpan(entry.ImageOffset, entry.Bytes).ToArray();
            Image? image = DecodeImageData(imageData);
            if (image != null)
            {
                return image;
            }
        }

        return null;
    }

    public static CursorPreview? LoadAniPreview(byte[] data)
    {
        if (data.Length < 12 || FourCc(data, 0) != "RIFF" || FourCc(data, 8) != "ACON")
        {
            return null;
        }

        var frames = new List<Image>();
        ParseAniChunks(data, 12, data.Length, frames);
        if (frames.Count == 0)
        {
            return null;
        }

        if (frames.Count > MaxAniFrames)
        {
            foreach (Image frame in frames.Skip(MaxAniFrames))
            {
                frame.Dispose();
            }

            frames = frames.Take(MaxAniFrames).ToList();
        }

        return new CursorPreview(frames, Enumerable.Repeat(120, frames.Count));
    }

    private static void ParseAniChunks(byte[] data, int start, int end, List<Image> frames)
    {
        int position = start;
        while (position + 8 <= end)
        {
            string id = FourCc(data, position);
            int size = checked((int)ReadU32(data, position + 4));
            int contentStart = position + 8;
            int contentEnd = contentStart + size;
            if (size < 0 || contentEnd > end || contentEnd > data.Length)
            {
                break;
            }

            if (id == "icon")
            {
                Image? image = LoadCur(data.AsSpan(contentStart, size).ToArray());
                if (image != null)
                {
                    frames.Add(image);
                }
            }
            else if (id == "LIST" && size >= 4)
            {
                ParseAniChunks(data, contentStart + 4, contentEnd, frames);
            }

            position = contentEnd + (size & 1);
        }
    }

    private static Image? DecodeImageData(byte[] data)
    {
        if (IsPng(data))
        {
            using var stream = new MemoryStream(data);
            using Image image = Image.FromStream(stream);
            return new Bitmap(image);
        }

        return DecodeDib(data);
    }

    private static Image? DecodeDib(byte[] data)
    {
        if (data.Length < 40)
        {
            return null;
        }

        int headerSize = checked((int)ReadU32(data, 0));
        if (headerSize < 40 || headerSize > data.Length)
        {
            return null;
        }

        int width = ReadI32(data, 4);
        int rawHeight = ReadI32(data, 8);
        int planes = ReadU16(data, 12);
        int bpp = ReadU16(data, 14);
        int compression = checked((int)ReadU32(data, 16));
        if (width <= 0 || rawHeight == 0 || planes != 1 || compression != 0)
        {
            return null;
        }

        int height = Math.Abs(rawHeight) / 2;
        if (height <= 0)
        {
            return null;
        }

        int colorCount = 0;
        if (bpp <= 8)
        {
            colorCount = checked((int)ReadU32(data, 32));
            if (colorCount == 0)
            {
                colorCount = 1 << bpp;
            }
        }

        int paletteOffset = headerSize;
        int pixelOffset = headerSize + colorCount * 4;
        int xorStride = ((width * bpp + 31) / 32) * 4;
        int andOffset = pixelOffset + xorStride * height;
        int andStride = ((width + 31) / 32) * 4;
        if (pixelOffset < 0 || pixelOffset >= data.Length || andOffset > data.Length)
        {
            return null;
        }

        Color[] palette = new Color[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            int offset = paletteOffset + i * 4;
            if (offset + 3 >= data.Length)
            {
                return null;
            }

            palette[i] = Color.FromArgb(255, data[offset + 2], data[offset + 1], data[offset]);
        }

        bool hasAlpha = bpp == 32 && HasAnyAlpha(data, pixelOffset, xorStride, width, height);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        bool bottomUp = rawHeight > 0;

        for (int y = 0; y < height; y++)
        {
            int sourceY = bottomUp ? height - 1 - y : y;
            int row = pixelOffset + sourceY * xorStride;
            int maskRow = andOffset + sourceY * andStride;

            for (int x = 0; x < width; x++)
            {
                Color color = ReadPixel(data, row, x, bpp, palette, hasAlpha);
                int alpha = color.A;
                if (maskRow + (x >> 3) < data.Length)
                {
                    int mask = (data[maskRow + (x >> 3)] >> (7 - (x & 7))) & 1;
                    if (mask == 1)
                    {
                        alpha = 0;
                    }
                }

                bitmap.SetPixel(x, y, Color.FromArgb(alpha, color.R, color.G, color.B));
            }
        }

        return bitmap;
    }

    private static Color ReadPixel(byte[] data, int row, int x, int bpp, Color[] palette, bool hasAlpha)
    {
        if (bpp == 32)
        {
            int offset = row + x * 4;
            if (offset + 3 >= data.Length)
            {
                return Color.Transparent;
            }

            int alpha = hasAlpha ? data[offset + 3] : 255;
            return Color.FromArgb(alpha, data[offset + 2], data[offset + 1], data[offset]);
        }

        if (bpp == 24)
        {
            int offset = row + x * 3;
            if (offset + 2 >= data.Length)
            {
                return Color.Transparent;
            }

            return Color.FromArgb(255, data[offset + 2], data[offset + 1], data[offset]);
        }

        int index = 0;
        if (bpp == 8)
        {
            int offset = row + x;
            if (offset >= data.Length)
            {
                return Color.Transparent;
            }

            index = data[offset];
        }
        else if (bpp == 4)
        {
            int offset = row + (x >> 1);
            if (offset >= data.Length)
            {
                return Color.Transparent;
            }

            byte value = data[offset];
            index = (x & 1) == 0 ? value >> 4 : value & 0x0F;
        }
        else if (bpp == 1)
        {
            int offset = row + (x >> 3);
            if (offset >= data.Length)
            {
                return Color.Transparent;
            }

            index = (data[offset] >> (7 - (x & 7))) & 1;
        }

        return index >= 0 && index < palette.Length ? palette[index] : Color.Transparent;
    }

    private static bool HasAnyAlpha(byte[] data, int pixelOffset, int stride, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            int row = pixelOffset + y * stride;
            for (int x = 0; x < width; x++)
            {
                int offset = row + x * 4 + 3;
                if (offset < data.Length && data[offset] != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPng(byte[] data)
    {
        return data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
    }

    private static string FourCc(byte[] data, int offset)
    {
        if (offset + 4 > data.Length)
        {
            return "";
        }

        return Encoding.ASCII.GetString(data, offset, 4);
    }

    private static int ReadU16(byte[] data, int offset)
    {
        return offset + 2 <= data.Length ? BitConverter.ToUInt16(data, offset) : 0;
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        return offset + 4 <= data.Length ? BitConverter.ToUInt32(data, offset) : 0;
    }

    private static int ReadI32(byte[] data, int offset)
    {
        return offset + 4 <= data.Length ? BitConverter.ToInt32(data, offset) : 0;
    }

    private sealed record CursorEntry(int Width, int Height, int Bytes, int ImageOffset);
}

internal sealed record RoleDef(string Key, string Label, string Reg, string[] Words);

internal sealed class CursorFile
{
    public CursorFile(string fullPath)
    {
        FullPath = fullPath;
        Name = Path.GetFileName(fullPath);
    }

    public string FullPath { get; }
    public string Name { get; }
}

internal sealed class CursorPreview : IDisposable
{
    private readonly List<Image> frames;
    private readonly List<int> durations;
    private bool disposed;

    public CursorPreview(IEnumerable<Image> frames, IEnumerable<int> durations)
    {
        this.frames = frames.ToList();
        this.durations = durations.ToList();

        while (this.durations.Count < this.frames.Count)
        {
            this.durations.Add(120);
        }

        for (int i = 0; i < this.durations.Count; i++)
        {
            this.durations[i] = Math.Clamp(this.durations[i], 40, 2000);
        }
    }

    public bool IsAnimated => frames.Count > 1;

    public Image? FrameAt(int elapsedMs)
    {
        if (frames.Count == 0)
        {
            return null;
        }

        if (frames.Count == 1)
        {
            return frames[0];
        }

        int totalDuration = durations.Take(frames.Count).Sum();
        if (totalDuration <= 0)
        {
            return frames[0];
        }

        int position = elapsedMs % totalDuration;
        for (int i = 0; i < frames.Count; i++)
        {
            position -= durations[i];
            if (position < 0)
            {
                return frames[i];
            }
        }

        return frames[^1];
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        var disposedFrames = new HashSet<Image>();
        foreach (Image frame in frames)
        {
            if (disposedFrames.Add(frame))
            {
                frame.Dispose();
            }
        }

        disposed = true;
    }
}

internal static class Ui
{
    public static readonly Color Shell = Color.FromArgb(244, 246, 249);
    public static readonly Color Panel = Color.White;
    public static readonly Color AltRow = Color.FromArgb(249, 250, 252);
    public static readonly Color Header = Color.FromArgb(247, 249, 252);
    public static readonly Color Text = Color.FromArgb(31, 41, 55);
    public static readonly Color Muted = Color.FromArgb(107, 114, 128);
    public static readonly Color Line = Color.FromArgb(232, 236, 242);
    public static readonly Color Border = Color.FromArgb(174, 183, 197);
    public static readonly Color ButtonBorder = Color.FromArgb(207, 216, 228);
    public static readonly Color PreviewBorder = Color.FromArgb(203, 213, 225);
    public static readonly Color Primary = Color.FromArgb(37, 99, 235);
    public static readonly Color PrimaryDark = Color.FromArgb(29, 78, 216);
    public static readonly Color WarnBack = Color.FromArgb(255, 251, 235);
    public static readonly Color WarnText = Color.FromArgb(146, 64, 14);
    public static readonly Color MissingBack = Color.FromArgb(255, 252, 224);
    public static readonly Color MissingBorder = Color.FromArgb(234, 179, 8);
    public static readonly Color MissingText = Color.FromArgb(86, 64, 8);
    public static readonly Color SaveOkText = Color.FromArgb(22, 101, 52);
    public static readonly Color DisabledBack = Color.FromArgb(241, 245, 249);
    public static readonly Color DisabledBorder = Color.FromArgb(226, 232, 240);
    public static readonly Color DisabledText = Color.FromArgb(148, 163, 184);
}
