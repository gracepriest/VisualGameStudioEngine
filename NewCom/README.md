# NewCOM — Cross-Platform Component Model

## Overview

NewCOM is a lightweight, cross-platform component model inspired by COM but without Windows dependencies. It provides:

- **Vtable-based interfaces** (like COM's IUnknown)
- **Reference counting** (AddRef/Release)
- **C ABI factory functions** (universal calling convention)
- **Manifest-based discovery** (no registry)
- **Language-agnostic** — any language with FFI can consume components

## Quick Start

### 1. Define a component (annotated C++)

```cpp
// greeter.h
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

Generates:
- `greeter_newcom.h` — vtable interface + C ABI declarations
- `greeter_newcom.cpp` — implementation shim + ref counting + factory
- `greeter.bas` — BasicLang binding module

### 3. Implement the component

Edit `greeter_newcom.cpp` and replace the `// TODO: implement` stubs with real code.

### 4. Compile as shared library

```bash
cl /LD /EHsc /I"NewCom" greeter_newcom.cpp /Fe:greeter_newcom.dll
```

### 5. Use from BasicLang

```basiclang
Import "greeter.bas"

Sub Main()
    Dim g As New Greeter()
    Console.WriteLine(g.SayHello("World"))
    g.Release()
End Sub
```

### 6. Use from C++

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

```
┌─────────────────────────────────────────────────────┐
│  Component (written once in C++)                     │
│  greeter.cpp: vtable + ref counting + factory       │
└──────────────────────┬──────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────┐
│  NewCOM ABI (newcom_abi.h)                          │
│  - C calling convention (cdecl)                     │
│  - INewUnknown base (QI/AddRef/Release)             │
│  - Vtable layout (function pointers)                │
│  - Manifest-based discovery                         │
└──┬──────────────────────┬───────────────────────────┘
   │                      │
   ▼                      ▼
┌────────────┐      ┌────────────┐
│ C++ backend│      │ BasicLang  │
│ (direct    │      │ .NET backend│
│  vtable    │      │ (P/Invoke)  │
│  calls)    │      │             │
└────────────┘      └────────────┘
```

## ABI Specification

### Base Interface

```cpp
struct INewUnknown {
    const INewUnknownVtbl* vtbl;
};

struct INewUnknownVtbl {
    int32_t  (*QueryInterface)(INewUnknown* self, const NewComGUID* iid, void** out);
    uint32_t (*AddRef)(INewUnknown* self);
    uint32_t (*Release)(INewUnknown* self);
};
```

### Type System

| NewCOM | C++ | BasicLang | .NET |
|---|---|---|---|
| int32 | int32_t | Integer | int |
| int64 | int64_t | Long | long |
| float | float | Single | float |
| double | double | Double | double |
| bool | bool | Boolean | bool |
| string | const char* | String | string |
| ptr | void* | Integer | IntPtr |

### Error Codes

- `NEWCOM_OK` (0) — success
- `NEWCOM_E_NOINTERFACE` (-1) — interface not supported
- `NEWCOM_E_FAIL` (-2) — general failure
- `NEWCOM_E_OUTOFMEMORY` (-3) — out of memory
- `NEWCOM_E_INVALIDARG` (-4) — invalid argument

### Component Discovery

Components are discovered via manifest files (`newcom.json`):
```json
{
    "name": "Greeter",
    "clsid": "87654321-4321-4321-fedc-ba9876543210",
    "library": "greeter_newcom.dll",
    "factory": "Greeter_Create"
}
```

Search paths: `./components/`, `~/.newcom/`, `$NEWCOM_PATH/`

## Creating Components from BasicLang

BasicLang classes can be exported as NewCOM components (via IL2CPP native compilation):

```basiclang
' MyComponent.bas
Class MyComponent
    Public Function GetValue() As Integer
        Return 42
    End Function
End Class
```

Compile with `--target=il2cpp --output-type=component` to produce a native shared library with NewCOM factory.

## Creating Components from C++

Any C++ class can be wrapped as a NewCOM component using the generator tool or manually:

```cpp
// mycomponent.h
#include "newcom_abi.h"

NEWCOM_COMPONENT(MyComponent)
    NEWCOM_METHOD(int, GetValue)
NEWCOM_END
```

## Integration with the IDE

- **Templates:** NewCOM project templates in `Templates/Projects/`
- **Library references:** Add `greeter_newcom.dll` to project references
- **IntelliSense:** Component interfaces appear in autocomplete
- **Build integration:** Generator runs automatically during build

## Directory Structure

```
NewCom/
├── newcom_abi.h              # Universal ABI header
├── tools/
│   └── newcom_gen.py         # Annotation-based generator
└── examples/
    ├── greeter/              # Hand-written example
    │   ├── greeter.h
    │   ├── greeter.cpp
    │   ├── test_greeter.cpp
    │   └── greeter.bas
    └── gns/                  # GameNetworkingSockets wrapper
        ├── gns.h             # Annotated header
        ├── gnsconnection_newcom.h/.cpp/.bas  # Generated
        └── gnsserver_newcom.h/.cpp/.bas      # Generated
```
