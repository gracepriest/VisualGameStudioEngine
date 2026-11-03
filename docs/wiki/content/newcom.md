title: NewCOM
lede: Cross-platform component model — COM without Windows. Vtable-based interfaces, reference counting, C ABI factories.
---
## What is NewCOM?

NewCOM is a lightweight, cross-platform component model inspired by COM but without Windows dependencies. It provides:
- **Vtable-based interfaces** (like COM's IUnknown)
- **Reference counting** (AddRef/Release)
- **C ABI factory functions** (universal calling convention)
- **Manifest-based discovery** (no registry)
- **Language-agnostic** — any language with FFI can consume components

## Quick Start

### 1. Define a component (annotated C++)

```cpp
#include "newcom_abi.h"

NEWCOM_COMPONENT(Greeter)
    NEWCOM_METHOD(int, SayHello, const char* name, char** outMessage)
    NEWCOM_METHOD(int, GetCount, int* outCount)
NEWCOM_END
```

### 2. Generate the wrapper

```bash
python newcom_gen.py greeter.h -o output/
```

### 3. Implement and compile

Edit the generated `_newcom.cpp` to replace `// TODO: implement` stubs, then compile as a shared library.

### 4. Use from BasicLang

```basiclang
Import "greeter.bas"

Sub Main()
    Dim g As New Greeter()
    Console.WriteLine(g.SayHello("World"))
    g.Release()
End Sub
```

### 5. Use from C++

```cpp
#include "greeter_newcom.h"
int main() {
    auto g = Greeter_Create();
    char* msg = nullptr;
    g->vtbl->SayHello(g, "World", &msg);
    std::cout << msg << std::endl;
    free(msg);
    g->vtbl->Release(g);
    return 0;
}
```

## Architecture

NewCOM builds on the same vtable infrastructure as IL2CPP's virtual dispatch. The key components:

- **ABI header** (`newcom_abi.h`): Universal contract with INewUnknown, GUID, error codes
- **Generator tool** (`newcom_gen.py`): Reads annotated C++ headers, generates interface + implementation + BasicLang binding
- **Manifest loader** (`newcom_loader.h/.cpp`): Reads `newcom.json`, loads DLLs via `LoadLibrary`/`dlopen`

## IDE Integration

- **Project template**: `NewComComponent` in VS 2022
- **Build pipeline**: MSBuild target `NewComGenerate` runs the generator before compilation
- **Project properties**: `NewComEnabled`, `NewComHeader` in `.blproj`

## GameNetworkingSockets Wrapper

A complete NewCOM wrapper for Valve's GameNetworkingSockets is provided in `NewCom/examples/gns/`:
- 3 components: GNSConnection, GNSServer, GNSClient
- Annotated header + generated interfaces + hand-written implementation
- Stub implementations (replace with real GNS calls by defining `HAVE_GNS`)
- BasicLang bindings for all three components

## Related

- [Backends](#/backends) — IL2CPP backend
- [C++ interop](#/cpp-interop) — Native C++ integration
- [Architecture](#/architecture) — Overall system design
