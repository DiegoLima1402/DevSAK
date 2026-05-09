# Using Microsoft.WindowsAppSDK.ML via NuGet

The `Microsoft.WindowsAppSDK.ML` package brings Windows ML to your project as part of the Windows
App SDK. To use it correctly, follow these steps:

## Framework-Dependent Deployment (Recommended)

Windows ML is recommended to be used as a _framework-dependent_ component. This means your app should either:

-   **Reference the main Windows App SDK NuGet package:**
    -   Add a reference to `Microsoft.WindowsAppSDK` (recommended). This will automatically include
        `Microsoft.WindowsAppSDK.ML` as a transitive dependency.
-   **Or, reference both ML and WindowsAppSDK Runtime directly:**
    -   Add references to both `Microsoft.WindowsAppSDK.ML` **and**
        `Microsoft.WindowsAppSDK.Runtime`.

> **Note:** If you only reference `Microsoft.WindowsAppSDK.ML` without
> `Microsoft.WindowsAppSDK.Runtime`, your app will not deploy or run correctly.

## Self-Contained Deployment

For applications that need to bundle ONNX Runtime dependencies locally without relying on framework packages, enable self-contained mode:

```xml
<PropertyGroup>
  <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
</PropertyGroup>
```

> **Tip:** If ONNX Runtime binaries are not being copied to your output directory correctly, ensure you have an explicit reference to `Microsoft.WindowsAppSDK.Base` in your project, as this package contains the necessary build targets for binary deployment.

In self-contained mode, ONNX Runtime binaries are deployed alongside your application:

```
MyApp/
├── MyApp.exe
├── Microsoft.Windows.AI.MachineLearning.dll
├── onnxruntime.dll
├── onnxruntime_providers_shared.dll
└── DirectML.dll
```

This mode is useful for:
- Applications that cannot depend on framework packages
- Scenarios requiring specific ONNX Runtime versions
- Simplified deployment without framework dependencies

## Unpackaged Applications

If your app is unpackaged (not MSIX), add the following property to your project to enable the
Windows App SDK bootstrapper:

```xml
<WindowsPackageType>None</WindowsPackageType>
```

This is a temporary requirement and will be auto-detected in future releases.

## Execution Provider Enumeration

Windows ML supports enumerating available execution providers through the PackageExtensionCatalog API. However, this functionality has specific requirements and limitations:

### Required Capabilities

For execution provider enumeration to work in packaged applications, your app manifest must declare the `packageQuery` capability:

```xml
<Package>
  <Capabilities>
    <uap4:Capability Name="packageQuery" />
  </Capabilities>
</Package>
```

### Execution Provider Enumeration Limitations

- **AppContainer Applications**: Execution provider enumeration will return empty results if the `packageQuery` capability is not declared in the app manifest
- **LowIL Applications**: Execution provider enumeration is not available and will return empty results
- **Insufficient Privileges**: If the required privileges are not held, execution provider enumeration will return empty results rather than failing.

### Unpackaged AppContainer Processes

For unpackaged applications running in AppContainer mode, the `packageQuery` capability must be granted to the process token when creating the AppContainer. This is done by including the capability SID in the AppContainer creation:

```cpp
// When creating an AppContainer for an unpackaged process
PSID packageQuerySid = nullptr;
ConvertStringSidToSid(L"S-1-15-3-1024-1962849891-688487262-3571417821-3628679630-802580238-1922556387-206211640-3335523193", &packageQuerySid);

SID_AND_ATTRIBUTES capabilities[] = {
    { packageQuerySid, SE_GROUP_ENABLED }
};

// Include the capabilities when creating the AppContainer token
// This allows the unpackaged AppContainer process to use PackageExtensionCatalog APIs
```

The SID `S-1-15-3-1024-1962849891-688487262-3571417821-3628679630-802580238-1922556387-206211640-3335523193` corresponds to the `packageQuery` capability.

### Fallback Behavior

When execution provider enumeration is not available or returns empty results, Windows ML will gracefully fall back to using built-in providers, e.g. the CPU execution provider.

For applications that require specific execution providers, consider brokering and explicitly configuring them rather than relying on automatic enumeration.

## Using ONNX Runtime Headers

The ONNX Runtime headers are included in a `winml` subdirectory to avoid conflicts with other versions of ONNX Runtime. When using these headers in your code, include them with the subdirectory prefix:

```cpp
#include <winml/onnxruntime_c_api.h>
#include <winml/onnxruntime_cxx_api.h>
```

### Using ONNX Runtime headers without a prefix

If your existing code uses the headers without the `winml/` prefix and you cannot update your includes, you can enable this behavior by setting the `WinMLEnableDefaultOrtHeaderIncludePath` property in your project:

```xml
<PropertyGroup>
  <WinMLEnableDefaultOrtHeaderIncludePath>true</WinMLEnableDefaultOrtHeaderIncludePath>
</PropertyGroup>
```

### Passthrough Mode for Redistributed Components (e.g. dynamic link libraries)

For redistributed components that are consumed by other applications where ONNX Runtime initialization is handled externally, enable passthrough mode:

```xml
<PropertyGroup>
  <WindowsAppSDKMLPassthroughOnnxRuntime>true</WindowsAppSDKMLPassthroughOnnxRuntime>
</PropertyGroup>
```

Passthrough mode is designed for scenarios where:
- Your component is redistributed and consumed by other binaries
- ONNX Runtime is expected to be loaded by the host application (whether framework-dependent, self-contained, or custom initialization)
- The initialization pattern is not known at build time
- You need to provide ONNX Runtime access without managing the runtime lifecycle

In passthrough mode, Windows ML assumes ONNX Runtime (`onnxruntime.dll`) is already loaded in the current process and uses `GetModuleHandle` to access the already-loaded library instead of attempting its own initialization or dependency resolution.

If your project is consuming Windows ML from a DLL, the Windows App SDK bootstrapping logic is not
enabled by default. The expectation is that the application consuming the DLL should enable
bootstrapping if needed. In the case that you want to enable it within the DLL itself, you will need
to ensure the following is set in your project properties:

```xml
<WindowsAppSdkBootstrapInitialize>true</WindowsAppSdkBootstrapInitialize>
```

## API Usage

### Automatic Initialization

The package implements automatic ONNX Runtime initialization through a custom `OrtGetApiBase`
implementation. This takes care of the initialization details and provides transparent access to the
ONNX Runtime binaries shipping with Windows ML without requiring developers to link against
onnxruntime.lib. See `include\WindowsMLAutoInitializer.cpp`.

## Manual Initialization Control

For applications requiring explicit control over initialization sequences or those not utilizing
ONNX Runtime APIs, auto-initialization can be disabled via MSBuild property:

```xml
<PropertyGroup>
  <!-- Disable Windows ML auto-initialization only -->
  <DisableWindowsAppSDKMLAutoInitialize>true</DisableWindowsAppSDKMLAutoInitialize>
</PropertyGroup>
```

## Native ONNX Runtime Linking (Advanced/Edge-Case Scenario)

For advanced C++ applications where the auto-initializer pattern is not feasible and you need to directly link against the ONNX Runtime import library, you can enable native linking:

```xml
<PropertyGroup>
  <WindowsMLNativeLinkOnnxRuntime>true</WindowsMLNativeLinkOnnxRuntime>
</PropertyGroup>
```

> **⚠️ Important:** This is an edge-case scenario for advanced users. Most applications should use the standard auto-initialization approach described above.

When `WindowsMLNativeLinkOnnxRuntime` is set to `true`:

- **Auto-initialization is automatically disabled** - no need to set `DisableWindowsAppSDKMLAutoInitialize`
- **Platform-specific import library is linked** - automatically selects the correct `onnxruntime.lib` for your target platform (x64, ARM64, ARM64EC)
- **ONNX Runtime headers are available** - use `#include <winml/onnxruntime_c_api.h>` or `#include <winml/onnxruntime_cxx_api.h>`

### Critical Dependency Requirements

**The consuming binary must ensure the correct `onnxruntime.dll` is available in the proper search order:**

1. **For packaged applications**: Explicitly reference the Windows App SDK framework package to ensure ONNX Runtime DLLs are available in the package
2. **For unpackaged applications**: Carefully manage DLL search order to ensure the correct ONNX Runtime version is loaded first
3. **Self-contained deployment**: Ensure ONNX Runtime DLLs are deployed alongside your application

**Failure to meet these requirements will result in runtime linking errors or incompatible DLL loading.**

This mode is **only** recommended for:
- Applications with complex initialization sequences that cannot use auto-initialization
- Custom ONNX Runtime integration scenarios requiring precise lifecycle control
- Edge cases where the standard auto-initializer pattern conflicts with application architecture

## Build and Run

Build your project as usual. The Windows ML APIs will be available for use.

## More Information

-   See the main
    [Windows ML documentation](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/overview)
    for API usage.
-   For more on Windows App SDK, see the
    [WindowsAppSDK docs](https://github.com/microsoft/WindowsAppSDK/tree/main/docs).
-   For more on framework-dependent deployment see
    [WindowsAppSDK deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview)
-   For sample projects, check the `Samples` directory or visit
    [WindowsAppSDK-Samples](https://github.com/microsoft/WindowsAppSDK-Samples).
