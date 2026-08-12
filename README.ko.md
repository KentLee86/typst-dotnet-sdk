# Cetz.Renderer

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
