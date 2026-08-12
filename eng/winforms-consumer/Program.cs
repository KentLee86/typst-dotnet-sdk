using Cetz.Renderer.WinForms;

Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
using var view = new CetzView { Zoom = 1.25, PageSpacing = 18 };
if (!view.AutoScroll || view.Zoom != 1.25)
    throw new InvalidOperationException("WinForms package control initialization failed.");
