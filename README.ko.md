# Cetz.Renderer

네이티브 SDK 위의 GUI 통합은 플랫폼별 의존성이 섞이지 않도록 계층을
분리합니다.

- `Cetz.Renderer.Core`: 렌더 결과를 GUI 공통 RGBA 문서/페이지로 변환합니다.
- `Cetz.Renderer.Avalonia`: 공통 문서를 표시하는 재사용 가능한 `CetzView`를 제공합니다.
- `Cetz.Renderer.Wpf`: 같은 문서를 DPI와 다중 페이지를 보존해 표시하는 WPF `CetzView`를 제공합니다.
- `samples/Cetz.Renderer.Avalonia.Sample`: 소스를 편집하고 즉시 실제 화면을 확인하는 예제입니다.
- `samples/Cetz.Renderer.Wpf.Sample`: 9개 공용 데모를 편집하고 스크롤 미리보기로 확인하는 WPF 예제입니다.
- `samples/Cetz.Renderer.Demo.Shared`: 모든 GUI 데모가 함께 사용하는 9개 내장 예제 카탈로그입니다.

```powershell
dotnet run --project samples/Cetz.Renderer.Avalonia.Sample
```

Avalonia 데모 상단 드롭다운에서 예제를 선택하면 공용 인메모리 프로젝트가
소스 편집기와 미리보기에 즉시 로드됩니다. 로컬 import와 SVG 자산이 필요한
예제도 동일한 카탈로그를 통해 제공됩니다.

WPF 어댑터는 `net8.0-windows`를 대상으로 하며 별도 런타임 패키지 의존성이
없습니다. Core의 premultiplied RGBA를 WPF의 Pbgra32로 변환하고, 원본 PPI를
유지해 DIP 크기를 계산합니다. `CetzView`를 `ScrollViewer` 안에 배치하면 줌,
페이지 간격, 가로/세로 다중 페이지 스크롤을 사용할 수 있습니다.

```powershell
dotnet run --project samples/Cetz.Renderer.Wpf.Sample
```

원격 데스크톱이나 캡처 환경에서 WPF 하드웨어 합성 화면을 기록할 수 없다면
`-- --software-rendering` 옵션을 추가할 수 있습니다.

Typst 0.14.2와 CeTZ 0.5.2를 프로세스 내부에서 렌더링하는 프로덕션 지향 .NET 8
SDK입니다. SVG, PNG, PDF, premultiplied RGBA8 결과를 관리 메모리로 반환하며
`typst.exe`, `cetz-render`, Node, Sharp 프로세스를 실행하지 않습니다.

Windows x64에서는 `Cetz.Renderer.Native.win-x64`, Linux x64에서는
`Cetz.Renderer.Native.linux-x64` 패키지 하나만 참조합니다. RID 패키지가 같은
버전의 `Cetz.Renderer`를 자동으로 가져오며 네이티브 파일은
`runtimes/{rid}/native/`에 배치됩니다.

`CetzProjectBuilder`로 여러 `.typ` 파일과 이미지 같은 바이너리 파일을 메모리에
구성한 뒤 `RenderProject`로 렌더링할 수 있습니다. `RenderFile`, `RenderSource`,
`RenderProject`에는 각각 비동기 API가 있습니다. 결과는 `ReadOnlyMemory<byte>`,
`OpenRead()`, `WriteToDirectory`와 `WriteToDirectoryAsync`로 사용할 수 있습니다.

한 렌더러 인스턴스의 호출은 직렬화되며 여러 인스턴스는 병렬로 사용할 수
있습니다. 취소 토큰은 네이티브 호출을 기다리는 동안의 취소를 보장하지만 이미
시작된 Typst 컴파일을 중단하지는 않습니다.

시스템 글꼴 검색은 기본 비활성화입니다. 메모리 글꼴과 글꼴 디렉터리는 생성 시
검증합니다. `BaseDirectory`는 상대 import의 파일 fallback이며, 신뢰할 수 없는
문서에는 `RestrictToDirectory`를 설정해 네이티브 파일 시스템 루트를 제한합니다.

패키지는 항상 내장 CeTZ 0.5.2와 oxifmt 1.0.0을 먼저 확인합니다.
`CacheThenNetwork`, `CacheOnly`, `EmbeddedOnly` 모드가 제공됩니다. 일반 NuGet
사용자는 `NativeLibraryPath`를 설정하지 않으며, 이 옵션은 개발과 진단용입니다.
