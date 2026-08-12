namespace Cetz.Renderer.Core;

public enum CetzZoomMode
{
    Custom,
    FitWidth,
    FitPage
}

public enum CetzPageViewMode
{
    ContinuousSingle,
    ContinuousFacing,
    SinglePage,
    FacingPages
}

/// <summary>Common contract implemented by every GUI document control.</summary>
public interface ICetzDocumentView
{
    CetzRenderedDocument? Document { get; }
    double Zoom { get; }
    CetzZoomMode ZoomMode { get; }
    CetzPageViewMode ViewMode { get; }
    double PageSpacing { get; }
    int CurrentPageIndex { get; }
    int PageCount { get; }
    CetzDocumentViewLayout Layout { get; }

    void SetDocument(CetzRenderedDocument document);
    void SetZoom(double zoom);
    void SetZoomMode(CetzZoomMode mode);
    void SetViewMode(CetzPageViewMode mode);
    void SetViewport(double width, double height);
    void SetPageSpacing(double pageSpacing);
    void GoToPage(int pageIndex);
    bool TrackCurrentPage(int pageIndex);
    bool MoveNext();
    bool MovePrevious();
    void ReleaseDocument();
}
