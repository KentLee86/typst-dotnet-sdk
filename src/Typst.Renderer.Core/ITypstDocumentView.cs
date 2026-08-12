namespace Typst.Renderer.Core;

public enum TypstZoomMode
{
    Custom,
    FitWidth,
    FitPage
}

public enum TypstPageViewMode
{
    ContinuousSingle,
    ContinuousFacing,
    SinglePage,
    FacingPages
}

/// <summary>Common contract implemented by every GUI document control.</summary>
public interface ITypstDocumentView
{
    TypstRenderedDocument? Document { get; }
    double Zoom { get; }
    TypstZoomMode ZoomMode { get; }
    TypstPageViewMode ViewMode { get; }
    double PageSpacing { get; }
    int CurrentPageIndex { get; }
    int PageCount { get; }
    TypstDocumentViewLayout Layout { get; }

    void SetDocument(TypstRenderedDocument document);
    void SetZoom(double zoom);
    void SetZoomMode(TypstZoomMode mode);
    void SetViewMode(TypstPageViewMode mode);
    void SetViewport(double width, double height);
    void SetPageSpacing(double pageSpacing);
    void GoToPage(int pageIndex);
    bool TrackCurrentPage(int pageIndex);
    bool MoveNext();
    bool MovePrevious();
    void ReleaseDocument();
}
