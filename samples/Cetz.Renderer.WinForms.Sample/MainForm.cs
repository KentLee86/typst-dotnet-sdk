using Cetz.Renderer.Core;
using Cetz.Renderer.Demo.Shared;

namespace Cetz.Renderer.WinForms.Sample;

public sealed class MainForm : Form
{
    private readonly CetzRendererOptions _rendererOptions;
    private readonly CetzViewportInteractionController _viewportInteraction;
    private CetzRenderController? _renderController;
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
    private readonly ComboBox _zoomModePicker = CreateEnumPicker();
    private readonly ComboBox _viewModePicker = CreateEnumPicker();
    private readonly NumericUpDown _zoom = new()
    {
        DecimalPlaces = 2,
        Increment = 0.1m,
        Minimum = 0.1m,
        Maximum = 8m,
        Value = 0.9m,
        Width = 64
    };
    private readonly Button _previousButton = new() { AutoSize = true, Text = "◀ 이전" };
    private readonly Button _nextButton = new() { AutoSize = true, Text = "다음 ▶" };
    private readonly Label _pageIndicator = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleCenter };
    private bool _opened;

    public MainForm()
    {
        Text = "Cetz.Renderer WinForms Sample";
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(238, 242, 247);

        _rendererOptions = new CetzRendererOptions
        {
            NativeLibraryPath = ResolveNativeLibrary(),
            PackageResolution = CetzPackageResolution.EmbeddedOnly
        };
        _viewportInteraction = new CetzViewportInteractionController(_preview);
        _preview.MouseDown += BeginPan;
        _preview.MouseMove += ContinuePan;
        _preview.MouseUp += EndPan;
        _preview.MouseCaptureChanged += (_, _) =>
        {
            if (!_preview.Capture) _viewportInteraction.EndPan();
        };
        _preview.MouseWheel += ZoomWithWheel;

        _demoPicker.SelectedIndexChanged += DemoPicker_SelectedIndexChanged;
        _renderButton.Click += async (_, _) => await RenderAsync();
        _zoomModePicker.Items.AddRange(Enum.GetValues<CetzZoomMode>().Cast<object>().ToArray());
        _viewModePicker.Items.AddRange(Enum.GetValues<CetzPageViewMode>().Cast<object>().ToArray());
        _zoomModePicker.SelectedItem = CetzZoomMode.Custom;
        _viewModePicker.SelectedItem = CetzPageViewMode.ContinuousSingle;
        _zoomModePicker.SelectedIndexChanged += (_, _) =>
        {
            if (_zoomModePicker.SelectedItem is CetzZoomMode mode)
            {
                _preview.SetZoomMode(mode);
                _zoom.Enabled = mode == CetzZoomMode.Custom;
            }
            UpdateNavigation();
        };
        _viewModePicker.SelectedIndexChanged += (_, _) =>
        {
            if (_viewModePicker.SelectedItem is CetzPageViewMode mode) _preview.SetViewMode(mode);
            UpdateNavigation();
        };
        _zoom.ValueChanged += (_, _) =>
        {
            _preview.SetZoom((double)_zoom.Value);
            _zoomModePicker.SelectedItem = CetzZoomMode.Custom;
        };
        _previousButton.Click += (_, _) => { _preview.MovePrevious(); UpdateNavigation(); };
        _nextButton.Click += (_, _) => { _preview.MoveNext(); UpdateNavigation(); };
        Shown += async (_, _) =>
        {
            _renderController = new CetzRenderController(
                _preview, _rendererOptions, SynchronizationContext.Current);
            _opened = true;
            await RenderAsync();
        };
        FormClosed += (_, _) =>
        {
            _renderController?.Dispose();
        };

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
        var previewPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPanel.Controls.Add(BuildPreviewToolbar(), 0, 0);
        previewPanel.Controls.Add(_preview, 0, 1);
        split.Panel2.Controls.Add(previewPanel);
        return split;
    }

    private Control BuildPreviewToolbar()
    {
        var toolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            WrapContents = true
        };
        toolbar.Controls.AddRange([
            new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 3), Text = "확대:" },
            _zoomModePicker,
            _zoom,
            new Label { AutoSize = true, Margin = new Padding(12, 8, 3, 3), Text = "보기:" },
            _viewModePicker,
            _previousButton,
            _pageIndicator,
            _nextButton
        ]);
        return toolbar;
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
        if (_demoPicker.SelectedItem is not CetzDemo demo || _renderController is null)
            return;

        SetRenderingState(true, "렌더링 중…", Color.SlateGray);
        try
        {
            var document = await _renderController.RenderProjectAsync(
                demo.CreateProject(_source.Text),
                options: new CetzDocumentRenderOptions { Ppi = 144 });

            if (IsDisposed || document is null)
                return;
            UpdateNavigation();
            var summary = $"{demo.DisplayName} · {demo.Paths.Count} file(s) · {document.Pages.Count} page(s) · " +
                $"{document.Timing.TotalMilliseconds:F0} ms";
            _status.Text = summary;
            _status.ForeColor = Color.FromArgb(8, 127, 91);
            Text = $"Cetz.Renderer WinForms Sample — {summary} · Typst {document.TypstVersion}";
        }
        catch (Exception exception)
        {
            if (IsDisposed || !ReferenceEquals(_renderController.LastError, exception))
                return;
            _status.Text = exception.Message;
            _status.ForeColor = Color.Crimson;
            Text = $"Cetz.Renderer WinForms Sample — Error: {exception.Message}";
        }
        finally
        {
            if (!IsDisposed && !_renderController.IsRendering)
                SetRenderingState(false, _status.Text, _status.ForeColor);
        }
    }

    private void SetRenderingState(bool rendering, string status, Color color)
    {
        _renderButton.Enabled = !rendering;
        _status.Text = status;
        _status.ForeColor = color;
        UseWaitCursor = rendering;
    }

    private void UpdateNavigation()
    {
        var count = _preview.PageCount;
        var displayPage = count == 0 ? 0 : _preview.CurrentPageIndex + 1;
        var facing = _preview.ViewMode is CetzPageViewMode.ContinuousFacing or CetzPageViewMode.FacingPages;
        var lastVisible = count == 0 ? 0 : Math.Min(count, displayPage + (facing ? 1 : 0));
        _pageIndicator.Text = facing && lastVisible > displayPage
            ? $"{displayPage}–{lastVisible} / {count}"
            : $"{displayPage} / {count}";
        _previousButton.Enabled = count > 0 && _preview.CurrentPageIndex > 0;
        _nextButton.Enabled = count > 0 && _preview.CurrentPageIndex + (facing ? 2 : 1) < count;
    }

    private void BeginPan(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        var offset = CurrentScrollOffset();
        _viewportInteraction.BeginPan(args.X, args.Y, offset.X, offset.Y);
        _preview.Focus();
        _preview.Capture = true;
        _preview.Cursor = Cursors.Hand;
    }

    private void ContinuePan(object? sender, MouseEventArgs args)
    {
        if (!_viewportInteraction.TryPanTo(args.X, args.Y, out var offset)) return;
        ApplyScrollOffset(offset);
    }

    private void EndPan(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        _viewportInteraction.EndPan();
        _preview.Capture = false;
        _preview.Cursor = Cursors.Default;
    }

    private void ZoomWithWheel(object? sender, MouseEventArgs args)
    {
        if (!ModifierKeys.HasFlag(Keys.Control)) return;
        var current = CurrentScrollOffset();
        var offset = _viewportInteraction.ZoomByWheel(
            args.Delta, args.X, args.Y, current.X, current.Y);
        _zoomModePicker.SelectedItem = CetzZoomMode.Custom;
        _zoom.Value = Math.Clamp((decimal)_preview.Zoom, _zoom.Minimum, _zoom.Maximum);
        BeginInvoke(() => ApplyScrollOffset(offset));
        UpdateNavigation();
    }

    private CetzViewportOffset CurrentScrollOffset()
        => new(-_preview.AutoScrollPosition.X, -_preview.AutoScrollPosition.Y);

    private void ApplyScrollOffset(CetzViewportOffset offset)
        => _preview.AutoScrollPosition = new Point(
            checked((int)Math.Min(int.MaxValue, Math.Round(offset.X))),
            checked((int)Math.Min(int.MaxValue, Math.Round(offset.Y))));

    private static ComboBox CreateEnumPicker() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 132
    };

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
