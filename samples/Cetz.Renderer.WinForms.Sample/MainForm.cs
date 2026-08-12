using Cetz.Renderer.Core;
using Cetz.Renderer.Demo.Shared;

namespace Cetz.Renderer.WinForms.Sample;

public sealed class MainForm : Form
{
    private readonly CetzDocumentRenderer _renderer;
    private readonly global::Cetz.Renderer.WinForms.CetzView _preview = new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(28),
        Zoom = 0.9
    };
    private readonly ComboBox _demoPicker = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        DisplayMember = nameof(CetzDemo.DisplayName)
    };
    private readonly TextBox _source = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        Dock = DockStyle.Fill,
        Font = new Font(FontFamily.GenericMonospace, 10),
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false
    };
    private readonly Label _description = new()
    {
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        ForeColor = Color.SlateGray
    };
    private readonly Label _status = new()
    {
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        ForeColor = Color.SlateGray,
        Text = "준비",
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Button _renderButton = new()
    {
        Anchor = AnchorStyles.Top | AnchorStyles.Right,
        AutoSize = true,
        Text = "렌더링"
    };
    private bool _opened;

    public MainForm()
    {
        Text = "Cetz.Renderer WinForms Sample";
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(238, 242, 247);

        _renderer = new CetzDocumentRenderer(new CetzRendererOptions
        {
            NativeLibraryPath = ResolveNativeLibrary(),
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        });

        _demoPicker.SelectedIndexChanged += DemoPicker_SelectedIndexChanged;
        _renderButton.Click += async (_, _) => await RenderAsync();
        Shown += async (_, _) =>
        {
            _opened = true;
            await RenderAsync();
        };
        FormClosed += (_, _) => _renderer.Dispose();

        _demoPicker.Items.AddRange(CetzDemoCatalog.All.Cast<object>().ToArray());
        Controls.Add(BuildLayout());
        _demoPicker.SelectedIndex = 1;
        SelectDemo();
    }

    private Control BuildLayout()
    {
        var editor = new TableLayoutPanel
        {
            BackColor = Color.White,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            RowCount = 6
        };
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        editor.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6),
            Text = "Demo resource"
        }, 0, 0);
        editor.Controls.Add(_demoPicker, 0, 1);
        editor.Controls.Add(_description, 0, 2);

        var sourceHeader = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 8)
        };
        sourceHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sourceHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sourceHeader.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            Text = "Typst / CeTZ source",
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        sourceHeader.Controls.Add(_renderButton, 1, 0);
        editor.Controls.Add(sourceHeader, 0, 3);
        editor.Controls.Add(_source, 0, 4);
        editor.Controls.Add(_status, 0, 5);

        var split = new SplitContainer
        {
            Size = ClientSize,
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            IsSplitterFixed = false,
            Panel1MinSize = 340,
            Panel2MinSize = 400,
            SplitterDistance = 430,
            SplitterWidth = 5
        };
        split.Panel1.Controls.Add(editor);
        split.Panel2.Controls.Add(_preview);
        return split;
    }

    private async void DemoPicker_SelectedIndexChanged(object? sender, EventArgs e)
    {
        SelectDemo();
        if (_opened)
            await RenderAsync();
    }

    private void SelectDemo()
    {
        if (_demoPicker.SelectedItem is not CetzDemo demo)
            return;
        _source.Text = demo.Source;
        _description.Text = $"{demo.Description}  ·  {demo.Paths.Count} file(s)";
    }

    private async Task RenderAsync()
    {
        if (_demoPicker.SelectedItem is not CetzDemo demo)
            return;

        SetRenderingState(true, "렌더링 중…", Color.SlateGray);
        try
        {
            var document = await _renderer.RenderProjectAsync(
                demo.CreateProject(_source.Text),
                options: new CetzDocumentRenderOptions { Ppi = 144 });

            if (IsDisposed)
                return;
            _preview.Document = document;
            var summary = $"{demo.DisplayName} · {demo.Paths.Count} file(s) · {document.Pages.Count} page(s) · " +
                $"{document.Timing.TotalMilliseconds:F0} ms";
            _status.Text = summary;
            _status.ForeColor = Color.FromArgb(8, 127, 91);
            Text = $"Cetz.Renderer WinForms Sample — {summary} · Typst {document.TypstVersion}";
        }
        catch (Exception exception)
        {
            if (IsDisposed)
                return;
            _status.Text = exception.Message;
            _status.ForeColor = Color.Crimson;
            Text = $"Cetz.Renderer WinForms Sample — Error: {exception.Message}";
        }
        finally
        {
            if (!IsDisposed)
                SetRenderingState(false, _status.Text, _status.ForeColor);
        }
    }

    private void SetRenderingState(bool rendering, string status, Color color)
    {
        _renderButton.Enabled = !rendering;
        _demoPicker.Enabled = !rendering;
        _status.Text = status;
        _status.ForeColor = color;
        UseWaitCursor = rendering;
    }

    private static string ResolveNativeLibrary()
    {
        var configured = Environment.GetEnvironmentVariable("CETZ_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var besideApp = Path.Combine(AppContext.BaseDirectory, "cetz_dotnet_native.dll");
        if (File.Exists(besideApp))
            return besideApp;
        throw new FileNotFoundException(
            "Native runtime was not found. Build the Rust core or set CETZ_NATIVE_LIBRARY.",
            besideApp);
    }
}
