using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Cetz.Renderer;

internal sealed class NativeApi : IDisposable
{
    private readonly IntPtr _library;
    private bool _disposed;

    private NativeApi(IntPtr library)
    {
        _library = library;
        AbiVersion = Bind<AbiVersionDelegate>("cetz_dotnet_abi_version");
        BuilderCreate = Bind<BuilderCreateDelegate>("cetz_dotnet_builder_create");
        BuilderAddFont = Bind<BuilderAddFontDelegate>("cetz_dotnet_builder_add_font");
        BuilderBuild = Bind<BuilderBuildDelegate>("cetz_dotnet_builder_build");
        BuilderDestroy = Bind<DestroyDelegate>("cetz_dotnet_builder_destroy");
        ProjectCreate = Bind<ProjectCreateDelegate>("cetz_dotnet_project_create");
        ProjectAddText = Bind<ProjectAddDelegate>("cetz_dotnet_project_add_text");
        ProjectAddBinary = Bind<ProjectAddDelegate>("cetz_dotnet_project_add_binary");
        ProjectDestroy = Bind<DestroyDelegate>("cetz_dotnet_project_destroy");
        RenderProject = Bind<RenderProjectDelegate>("cetz_dotnet_render_project");
        ResultCount = Bind<ResultCountDelegate>("cetz_dotnet_result_count");
        ResultGet = Bind<ResultGetDelegate>("cetz_dotnet_result_get");
        ResultDestroy = Bind<DestroyDelegate>("cetz_dotnet_result_destroy");
        RendererDestroy = Bind<DestroyDelegate>("cetz_dotnet_renderer_destroy");
        StringFree = Bind<DestroyDelegate>("cetz_dotnet_string_free");
    }

    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    internal AbiVersionDelegate AbiVersion { get; }
    internal BuilderCreateDelegate BuilderCreate { get; }
    internal BuilderAddFontDelegate BuilderAddFont { get; }
    internal BuilderBuildDelegate BuilderBuild { get; }
    internal DestroyDelegate BuilderDestroy { get; }
    internal ProjectCreateDelegate ProjectCreate { get; }
    internal ProjectAddDelegate ProjectAddText { get; }
    internal ProjectAddDelegate ProjectAddBinary { get; }
    internal DestroyDelegate ProjectDestroy { get; }
    internal RenderProjectDelegate RenderProject { get; }
    internal ResultCountDelegate ResultCount { get; }
    internal ResultGetDelegate ResultGet { get; }
    internal DestroyDelegate ResultDestroy { get; }
    internal DestroyDelegate RendererDestroy { get; }
    internal DestroyDelegate StringFree { get; }

    internal static NativeApi Load(string? overridePath)
    {
        IntPtr library;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            library = NativeLibrary.Load(Path.GetFullPath(overridePath));
        }
        else
        {
            library = NativeLibrary.Load("cetz_dotnet_native", Assembly.GetExecutingAssembly(),
                DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories);
        }
        try { return new NativeApi(library); }
        catch { NativeLibrary.Free(library); throw; }
    }

    private T Bind<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    internal string TakeString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return string.Empty;
        try { return Marshal.PtrToStringUTF8(pointer) ?? string.Empty; }
        finally { StringFree(pointer); }
    }

    internal void ThrowIfError(int status, IntPtr errorPointer)
    {
        if (status == 0)
        {
            if (errorPointer != IntPtr.Zero) StringFree(errorPointer);
            return;
        }
        var raw = TakeString(errorPointer);
        NativeError? error = null;
        try { error = JsonSerializer.Deserialize<NativeError>(raw, JsonOptions); } catch (JsonException) { }
        var kind = error?.Kind ?? StatusKind(status);
        var message = error?.Message ?? (string.IsNullOrWhiteSpace(raw) ? $"Native operation failed with status {status}." : raw);
        throw kind switch
        {
            "configuration" => new CetzConfigurationException(message),
            "compilation" => new CetzCompilationException(message, MapDiagnostics(error?.Diagnostics)),
            _ => new CetzRenderException(message, kind)
        };
    }

    private static string StatusKind(int status) => status switch
    {
        1 => "invalid_argument", 2 => "configuration", 3 => "compilation",
        4 => "export", 5 => "panic", _ => "native"
    };

    private static IReadOnlyList<CetzDiagnostic> MapDiagnostics(IEnumerable<NativeDiagnostic>? diagnostics)
        => diagnostics?.Select(item => new CetzDiagnostic(
            item.Severity == "error" ? CetzDiagnosticSeverity.Error : CetzDiagnosticSeverity.Warning,
            item.Message, item.File, item.Line, item.Column, item.Hints)).ToArray()
            ?? Array.Empty<CetzDiagnostic>();

    public void Dispose()
    {
        if (_disposed) return;
        NativeLibrary.Free(_library);
        _disposed = true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint AbiVersionDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int BuilderCreateDelegate(IntPtr json, out IntPtr builder, out IntPtr error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int BuilderAddFontDelegate(IntPtr builder, IntPtr data, nuint length, out IntPtr error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int BuilderBuildDelegate(IntPtr builder, out IntPtr renderer, out IntPtr error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ProjectCreateDelegate(IntPtr mainPath, out IntPtr project, out IntPtr error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ProjectAddDelegate(IntPtr project, IntPtr path, IntPtr data, nuint length, out IntPtr error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RenderProjectDelegate(IntPtr renderer, IntPtr project, IntPtr requestJson, out IntPtr result, out IntPtr resultJson, out IntPtr error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nuint ResultCountDelegate(IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ResultGetDelegate(IntPtr result, nuint index, out IntPtr data, out nuint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DestroyDelegate(IntPtr handle);

    private sealed class NativeError
    {
        public string Kind { get; set; } = "native";
        public string Message { get; set; } = "Native operation failed.";
        public List<NativeDiagnostic> Diagnostics { get; set; } = [];
    }
}
