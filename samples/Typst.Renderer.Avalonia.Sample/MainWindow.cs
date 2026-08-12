using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Typst.Renderer.Core;
using Typst.Renderer.Demo.Shared;

namespace Typst.Renderer.Avalonia.Sample;

public sealed class MainWindow : Window
{
    private static readonly DemoChoice[] DemoChoices =
    [
        .. TypstDemoCatalog.All.Select(static demo => new DemoChoice(demo)),
        DemoChoice.LiveQuotation
    ];

    private readonly TypstRenderController _renderController;
    private readonly global::Typst.Renderer.Avalonia.TypstViewport _preview = new()
    {
        Background = new SolidColorBrush(Color.Parse("#DDE4EE"))
    };
    private global::Typst.Renderer.Avalonia.TypstView _view => _preview.View;
    private readonly TextBox _source = new()
    {
        AcceptsReturn = true,
        FontFamily = FontFamily.Default,
        FontSize = 13
    };
    private readonly ComboBox _demoPicker = new()
    {
        ItemsSource = DemoChoices,
        SelectedIndex = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly Border _quotationEditor = new()
    {
        IsVisible = false,
        Background = new SolidColorBrush(Color.Parse("#F4F7FB")),
        BorderBrush = new SolidColorBrush(Color.Parse("#D6DCE7")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10)
    };
    private readonly TextBox _recipientName = new() { Text = QuotationTemplate.Defaults.RecipientName };
    private readonly TextBox _registrationNumber = new() { Text = QuotationTemplate.Defaults.RegistrationNumber };
    private readonly TextBox _contactName = new() { Text = QuotationTemplate.Defaults.ContactName };
    private readonly TextBox _contactPhone = new() { Text = QuotationTemplate.Defaults.Phone };
    private readonly TextBox _contactEmail = new() { Text = QuotationTemplate.Defaults.Email };
    private readonly TextBox _recipientAddress = new() { Text = QuotationTemplate.Defaults.Address };
    private readonly TextBox _projectName = new() { Text = QuotationTemplate.Defaults.ProjectName };
    private readonly TextBox _quoteDate = new() { Text = QuotationTemplate.Defaults.QuoteDate };
    private readonly TextBlock _description = new()
    {
        Foreground = Brushes.SlateGray,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _status = new() { Text = "준비", Foreground = Brushes.SlateGray };
    private readonly Button _renderButton = new() { Content = "렌더링", HorizontalAlignment = HorizontalAlignment.Right };
    private readonly ComboBox _zoomModePicker = new()
    {
        ItemsSource = Enum.GetValues<TypstZoomMode>(),
        SelectedItem = TypstZoomMode.FitWidth,
        Width = 130
    };
    private readonly ComboBox _viewModePicker = new()
    {
        ItemsSource = Enum.GetValues<TypstPageViewMode>(),
        SelectedItem = TypstPageViewMode.ContinuousSingle,
        Width = 180
    };
    private readonly ComboBox _qualityPicker = new()
    {
        ItemsSource = RasterQualityChoices,
        SelectedIndex = 2,
        Width = 170
    };
    private readonly Button _previousPage = new() { Content = "◀ 이전" };
    private readonly Button _nextPage = new() { Content = "다음 ▶" };
    private readonly TextBox _currentPageInput = new()
    {
        Text = "1",
        Width = 68,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
    private readonly TextBlock _pageCountText = new() { VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _qualityRefresh;
    private CancellationTokenSource? _quotationRefresh;
    private bool _syncingPageInput;
    private bool _opened;

    private static readonly RasterQualityChoice[] RasterQualityChoices =
    [
        new(TypstRasterQualityMode.Fixed, "픽셀 고정 · 144 PPI"),
        new(TypstRasterQualityMode.HighResolution, "고해상도 · 288 PPI"),
        new(TypstRasterQualityMode.Automatic, "자동 고해상도")
    ];

    public MainWindow(string? initialDemoId = null)
    {
        Title = "Typst.Renderer Avalonia Sample";
        // Avalonia sizes the client area; these values produce the same outer
        // 1280 x 820 Windows frame used by the other desktop samples.
        Width = 1266;
        Height = 783;
        MinWidth = 900;
        MinHeight = 600;
        Background = new SolidColorBrush(Color.Parse("#EEF2F7"));

        _renderController = new TypstRenderController(_view, new TypstRendererOptions
        {
            NativeLibraryPath = ResolveNativeLibrary(),
            PackageResolution = TypstPackageResolution.EmbeddedOnly
        });

        foreach (var field in QuotationTextBoxes())
            field.TextChanged += (_, _) => QuotationFieldChanged();

        if (!string.IsNullOrWhiteSpace(initialDemoId))
        {
            var initialIndex = Array.FindIndex(
                DemoChoices,
                demo => demo.Id.Equals(initialDemoId, StringComparison.OrdinalIgnoreCase));
            if (initialIndex >= 0)
                _demoPicker.SelectedIndex = initialIndex;
        }

        SelectDemo();
        _demoPicker.SelectionChanged += async (_, _) =>
        {
            SelectDemo();
            if (_opened)
                await RenderAsync();
        };
        _renderButton.Click += async (_, _) => await RenderAsync();
        _zoomModePicker.SelectionChanged += (_, _) =>
        {
            if (_zoomModePicker.SelectedItem is TypstZoomMode mode) _view.SetZoomMode(mode);
        };
        _viewModePicker.SelectionChanged += (_, _) =>
        {
            if (_viewModePicker.SelectedItem is TypstPageViewMode mode) _view.SetViewMode(mode);
            UpdatePageStatus();
        };
        _qualityPicker.SelectionChanged += async (_, _) =>
        {
            CancelQualityRefresh();
            if (_opened) await RenderAsync();
        };
        _preview.ZoomChanged += async (_, _) =>
        {
            _zoomModePicker.SelectedItem = TypstZoomMode.Custom;
            await ScheduleAutomaticQualityRefreshAsync();
        };
        _preview.CurrentPageChanged += (_, _) => UpdatePageStatus();
        _currentPageInput.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            NavigateFromPageInput();
            args.Handled = true;
        };
        _currentPageInput.LostFocus += (_, _) => NavigateFromPageInput();
        _previousPage.Click += (_, _) => { _view.MovePrevious(); UpdatePageStatus(); };
        _nextPage.Click += (_, _) => { _view.MoveNext(); UpdatePageStatus(); };
        _view.SetZoomMode((TypstZoomMode)_zoomModePicker.SelectedItem!);
        _view.SetViewMode((TypstPageViewMode)_viewModePicker.SelectedItem!);
        Closed += (_, _) =>
        {
            CancelQualityRefresh();
            CancelQuotationRefresh();
            _renderController.Dispose();
            _preview.Dispose();
        };
        Content = BuildLayout();
        Opened += async (_, _) =>
        {
            _opened = true;
            await RenderAsync();
        };
    }

    private Control BuildLayout()
    {
        var editorHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        editorHeader.Children.Add(new TextBlock
        {
            Text = "Typst / CeTZ source",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(_renderButton, 1);
        editorHeader.Children.Add(_renderButton);

        var demoSelector = new StackPanel { Spacing = 6 };
        demoSelector.Children.Add(new TextBlock
        {
            Text = "Demo resource",
            FontWeight = FontWeight.SemiBold
        });
        demoSelector.Children.Add(_demoPicker);
        demoSelector.Children.Add(_description);

        _quotationEditor.Child = BuildQuotationEditor();

        var editor = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(22),
            RowSpacing = 12
        };
        editor.Children.Add(demoSelector);
        Grid.SetRow(_quotationEditor, 1);
        editor.Children.Add(_quotationEditor);
        Grid.SetRow(editorHeader, 2);
        editor.Children.Add(editorHeader);
        Grid.SetRow(_source, 3);
        editor.Children.Add(_source);
        Grid.SetRow(_status, 4);
        editor.Children.Add(_status);

        var previewToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 8)
        };
        previewToolbar.Children.Add(new TextBlock { Text = "맞춤", VerticalAlignment = VerticalAlignment.Center });
        previewToolbar.Children.Add(_zoomModePicker);
        previewToolbar.Children.Add(new TextBlock { Text = "보기", VerticalAlignment = VerticalAlignment.Center });
        previewToolbar.Children.Add(_viewModePicker);
        previewToolbar.Children.Add(new TextBlock { Text = "품질", VerticalAlignment = VerticalAlignment.Center });
        previewToolbar.Children.Add(_qualityPicker);
        previewToolbar.Children.Add(_previousPage);
        previewToolbar.Children.Add(_currentPageInput);
        previewToolbar.Children.Add(_pageCountText);
        previewToolbar.Children.Add(_nextPage);

        var previewPane = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        previewPane.Children.Add(previewToolbar);
        Grid.SetRow(_preview, 1);
        previewPane.Children.Add(_preview);

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("430,*") };
        split.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#D6DCE7")),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = editor
        });
        Grid.SetColumn(previewPane, 1);
        split.Children.Add(previewPane);
        return split;
    }

    private async Task RenderAsync()
    {
        if (_demoPicker.SelectedItem is not DemoChoice demo)
            return;

        _renderButton.IsEnabled = false;
        _status.Text = "렌더링 중…";
        _status.Foreground = Brushes.SlateGray;
        try
        {
            var ppi = TypstRasterQualityPolicy.ResolvePpi(SelectedQualityMode, _view.Zoom);
            var document = await _renderController.RenderProjectAsync(
                demo.CreateProject(_source.Text ?? string.Empty),
                options: new TypstDocumentRenderOptions { Ppi = ppi });
            if (document is null) return;
            UpdatePageStatus();
            _status.Text = $"{demo.DisplayName} · {document.Pages.Count} page · {document.Ppi:F0} PPI · {document.Timing.TotalMilliseconds:F0} ms";
            _status.Foreground = new SolidColorBrush(Color.Parse("#087F5B"));
            Title = $"Typst.Renderer Avalonia Sample — {_status.Text} · Typst {document.TypstVersion}";
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _status.Foreground = Brushes.Crimson;
            Title = $"Typst.Renderer Avalonia Sample — Error: {exception.Message}";
        }
        finally
        {
            _renderButton.IsEnabled = !_renderController.IsRendering;
        }
    }

    private TypstRasterQualityMode SelectedQualityMode =>
        (_qualityPicker.SelectedItem as RasterQualityChoice)?.Mode ?? TypstRasterQualityMode.Automatic;

    private async Task ScheduleAutomaticQualityRefreshAsync()
    {
        if (!_opened || SelectedQualityMode != TypstRasterQualityMode.Automatic)
            return;
        var ppi = TypstRasterQualityPolicy.ResolvePpi(SelectedQualityMode, _view.Zoom);
        if (_view.Document is { } document && Math.Abs(document.Ppi - ppi) < 0.5f)
            return;

        CancelQualityRefresh();
        var cancellation = new CancellationTokenSource();
        _qualityRefresh = cancellation;
        try
        {
            await Task.Delay(180, cancellation.Token);
            if (!cancellation.IsCancellationRequested)
                await RenderAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_qualityRefresh, cancellation))
                _qualityRefresh = null;
            cancellation.Dispose();
        }
    }

    private void CancelQualityRefresh()
    {
        var cancellation = _qualityRefresh;
        _qualityRefresh = null;
        cancellation?.Cancel();
    }

    private void UpdatePageStatus()
    {
        var first = _view.PageCount == 0 ? 0 : _view.CurrentPageIndex + 1;
        var last = _view.ViewMode is TypstPageViewMode.FacingPages or TypstPageViewMode.ContinuousFacing
            ? Math.Min(_view.PageCount, first + 1)
            : first;
        _syncingPageInput = true;
        try
        {
            _currentPageInput.Text = Math.Max(1, first).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _currentPageInput.IsEnabled = _view.PageCount > 0;
        }
        finally { _syncingPageInput = false; }
        _pageCountText.Text = first == last
            ? $"/ {_view.PageCount}"
            : $"– {last} / {_view.PageCount}";
        _previousPage.IsEnabled = _view.CurrentPageIndex > 0;
        _nextPage.IsEnabled = _view.CurrentPageIndex +
            (_view.ViewMode is TypstPageViewMode.FacingPages or TypstPageViewMode.ContinuousFacing ? 2 : 1) < _view.PageCount;
    }

    private void NavigateFromPageInput()
    {
        if (_syncingPageInput || _view.PageCount == 0)
            return;
        if (int.TryParse(_currentPageInput.Text, out var pageNumber))
            _view.GoToPage(Math.Clamp(pageNumber, 1, _view.PageCount) - 1);
        UpdatePageStatus();
    }

    private void SelectDemo()
    {
        if (_demoPicker.SelectedItem is not DemoChoice demo)
            return;
        CancelQuotationRefresh();
        _quotationEditor.IsVisible = demo.IsLiveQuotation;
        _source.Text = demo.IsLiveQuotation ? BuildQuotationSource() : demo.Source;
        _description.Text = demo.Description;
    }

    private Control BuildQuotationEditor()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("92,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 5,
            ColumnSpacing = 8
        };
        var heading = new TextBlock
        {
            Text = "견적서 입력 필드",
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#16345F"))
        };
        Grid.SetColumnSpan(heading, 2);
        grid.Children.Add(heading);
        AddQuotationField(grid, 1, "공급받는자", _recipientName);
        AddQuotationField(grid, 2, "사업자번호", _registrationNumber);
        AddQuotationField(grid, 3, "담당자", _contactName);
        AddQuotationField(grid, 4, "연락처", _contactPhone);
        AddQuotationField(grid, 5, "이메일", _contactEmail);
        AddQuotationField(grid, 6, "주소", _recipientAddress);
        AddQuotationField(grid, 7, "견적명", _projectName);
        AddQuotationField(grid, 8, "작성일", _quoteDate);
        return grid;
    }

    private static void AddQuotationField(Grid grid, int row, string label, TextBox field)
    {
        var caption = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.SlateGray
        };
        Grid.SetRow(caption, row);
        grid.Children.Add(caption);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private IEnumerable<TextBox> QuotationTextBoxes()
    {
        yield return _recipientName;
        yield return _registrationNumber;
        yield return _contactName;
        yield return _contactPhone;
        yield return _contactEmail;
        yield return _recipientAddress;
        yield return _projectName;
        yield return _quoteDate;
    }

    private QuotationFields ReadQuotationFields() => new(
        _recipientName.Text ?? string.Empty,
        _registrationNumber.Text ?? string.Empty,
        _contactName.Text ?? string.Empty,
        _contactPhone.Text ?? string.Empty,
        _contactEmail.Text ?? string.Empty,
        _recipientAddress.Text ?? string.Empty,
        _projectName.Text ?? string.Empty,
        _quoteDate.Text ?? string.Empty);

    private string BuildQuotationSource() => QuotationTemplate.Build(ReadQuotationFields());

    private void QuotationFieldChanged()
    {
        if (_demoPicker.SelectedItem is not DemoChoice { IsLiveQuotation: true })
            return;
        _source.Text = BuildQuotationSource();
        _ = ScheduleQuotationRefreshAsync();
    }

    private async Task ScheduleQuotationRefreshAsync()
    {
        if (!_opened)
            return;
        CancelQuotationRefresh();
        var cancellation = new CancellationTokenSource();
        _quotationRefresh = cancellation;
        try
        {
            await Task.Delay(250, cancellation.Token);
            if (!cancellation.IsCancellationRequested)
                await RenderAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_quotationRefresh, cancellation))
                _quotationRefresh = null;
            cancellation.Dispose();
        }
    }

    private void CancelQuotationRefresh()
    {
        var cancellation = _quotationRefresh;
        _quotationRefresh = null;
        cancellation?.Cancel();
    }

    private static string ResolveNativeLibrary()
    {
        var configured = Environment.GetEnvironmentVariable("TYPST_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var fileName = OperatingSystem.IsWindows()
            ? "typst_dotnet_native.dll"
            : "libtypst_dotnet_native.so";
        var besideApp = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(besideApp))
            return besideApp;
        throw new FileNotFoundException(
            "Native runtime was not found. Build the Rust core or set TYPST_NATIVE_LIBRARY.",
            besideApp);
    }

    private sealed record RasterQualityChoice(TypstRasterQualityMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record DemoChoice(
        string Id,
        string DisplayName,
        string Description,
        TypstDemo? SharedDemo,
        bool IsLiveQuotation)
    {
        public DemoChoice(TypstDemo demo)
            : this(demo.Id, demo.DisplayName, demo.Description, demo, false) { }

        public static DemoChoice LiveQuotation { get; } = new(
            "live-quotation",
            "동적 견적서 (Avalonia)",
            "공급받는자 정보를 편집하면 Typst 원본과 견적서 미리보기가 자동으로 갱신됩니다.",
            null,
            true);

        public string Source => SharedDemo?.Source ?? QuotationTemplate.Build(QuotationTemplate.Defaults);

        public TypstProject CreateProject(string source) => SharedDemo?.CreateProject(source)
            ?? new TypstProjectBuilder()
                .WithMainFile("live-quotation.typ")
                .AddText("live-quotation.typ", source)
                .Build();

        public override string ToString() => DisplayName;
    }
}
