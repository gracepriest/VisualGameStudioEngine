#!/usr/bin/env python3
"""
newcom_gen.py — NewCOM component generator

Reads an annotated C++ header and generates:
  1. <name>_newcom.h   — NewCOM interface (vtable)
  2. <name>_newcom.cpp — Implementation shim + C API wrappers
  3. <name>.bas         — BasicLang binding module

Annotations:
  NEWCOM_COMPONENT(ClassName)
      NEWCOM_METHOD(retType, methodName, param1, param2, ...)
  NEWCOM_END

Improved type mapping:
  - const char* / char* → String (BasicLang) / const char* (C++)
  - int* / int32_t* → ByRef Integer (BasicLang) / int32_t* (C++)
  - void* / const void* → Integer (handle)
  - Default values: NEWCOM_METHOD(int, Foo, int x = 0)
"""

import re
import sys
import os

# ─── Type mapping: C++ → BasicLang ───────────────────────────────────────────
CPP_TO_BASICLANG = {
    "int": "Integer",
    "int32_t": "Integer",
    "int32": "Integer",
    "long": "Long",
    "int64_t": "Long",
    "int64": "Long",
    "float": "Single",
    "double": "Double",
    "bool": "Boolean",
    "void": "Sub",
    "char*": "String",
    "const char*": "String",
    "std::string": "String",
    "void*": "Integer",
    "const void*": "Integer",
}

# ─── Type mapping: C++ → C ABI ────────────────────────────────────────────────
CPP_TO_CABI = {
    "int": "int32_t",
    "int32_t": "int32_t",
    "int32": "int32_t",
    "long": "int64_t",
    "int64_t": "int64_t",
    "int64": "int64_t",
    "float": "float",
    "double": "double",
    "bool": "bool",
    "void": "void",
    "char*": "char*",
    "const char*": "const char*",
    "std::string": "const char*",
    "void*": "void*",
    "const void*": "const void*",
}

def is_pointer_type(t):
    return t.strip().endswith("*")

def is_byref_param(t):
    """Check if a C++ pointer param should be ByRef in BasicLang."""
    t = t.strip()
    # int*, int32_t*, etc. are ByRef; void*, char* are not
    if t in ("void*", "const void*", "char*", "const char*"):
        return False
    return t.endswith("*")

def cpp_to_cabi(t):
    t = t.strip()
    # Handle default values: "int x = 0" → "int x"
    if "=" in t:
        t = t.split("=")[0].strip()
    return CPP_TO_CABI.get(t, t)

def cpp_to_basiclang(t):
    t = t.strip()
    if "=" in t:
        t = t.split("=")[0].strip()
    if is_byref_param(t):
        base = t.rstrip("*").strip()
        bl_type = CPP_TO_BASICLANG.get(base, "Integer")
        return f"ByRef {bl_type}"
    return CPP_TO_BASICLANG.get(t, "Integer")

def get_default_value(t):
    """Extract default value from 'type name = value'."""
    t = t.strip()
    if "=" in t:
        return t.split("=", 1)[1].strip()
    return None

def parse_params(params_str):
    """Parse 'type name, type name = default, ...' into list of (type, name, default)."""
    if not params_str:
        return []
    params = []
    # Split by comma, but be careful with pointer types (no commas in simple types)
    parts = [p.strip() for p in params_str.split(',') if p.strip()]
    for part in parts:
        # Handle default value
        default_val = None
        if "=" in part:
            param_part, default_val = part.split("=", 1)
            param_part = param_part.strip()
            default_val = default_val.strip()
        else:
            param_part = part
        tokens = param_part.split()
        if len(tokens) >= 2:
            name = tokens[-1]
            ptype = ' '.join(tokens[:-1])
            params.append((ptype, name, default_val))
    return params

def parse_annotations(header_text):
    """Parse NEWCOM_COMPONENT annotations from header text."""
    components = []
    pattern = re.compile(
        r'NEWCOM_COMPONENT\s*\(\s*(\w+)\s*\)\s*\n(.*?)NEWCOM_END',
        re.DOTALL
    )
    for match in pattern.finditer(header_text):
        comp_name = match.group(1)
        body = match.group(2)
        methods = []
        method_pattern = re.compile(
            r'NEWCOM_METHOD\s*\(\s*(\w[\w\s\*]*?)\s*,\s*(\w+)\s*(?:,\s*(.*?))?\s*\)',
            re.DOTALL
        )
        for m in method_pattern.finditer(body):
            ret_type = m.group(1).strip()
            method_name = m.group(2).strip()
            params_str = m.group(3).strip() if m.group(3) else ""
            params = parse_params(params_str)
            methods.append({
                "name": method_name,
                "return": ret_type,
                "params": params,
            })
        components.append({
            "name": comp_name,
            "methods": methods,
        })
    return components

def gen_interface(comp):
    """Generate the NewCOM interface header."""
    name = comp["name"]
    lines = []
    lines.append(f"// {name.lower()}_newcom.h — NewCOM component interface")
    lines.append(f"#pragma once")
    lines.append(f'#include "newcom_abi.h"')
    lines.append(f"")
    lines.append(f"// Interface: I{name}")
    lines.append(f"struct I{name};")
    lines.append(f"")
    lines.append(f"struct I{name}Vtbl {{")
    lines.append(f"    // INewUnknown")
    lines.append(f"    int32_t  (*QueryInterface)(INewUnknown* self, const NewComGUID* iid, void** out);")
    lines.append(f"    uint32_t (*AddRef)(INewUnknown* self);")
    lines.append(f"    uint32_t (*Release)(INewUnknown* self);")
    for m in comp["methods"]:
        ret = cpp_to_cabi(m["return"])
        params = ", ".join(f"{cpp_to_cabi(t)} {n}" for t, n, _ in m["params"])
        if params:
            lines.append(f"    {ret} (*{m['name']})(I{name}* self, {params});")
        else:
            lines.append(f"    {ret} (*{m['name']})(I{name}* self);")
    lines.append(f"}};")
    lines.append(f"")
    lines.append(f"struct I{name} {{")
    lines.append(f"    const I{name}Vtbl* vtbl;")
    lines.append(f"}};")
    lines.append(f"")
    lines.append(f'extern "C" NEWCOM_EXPORT I{name}* {name}_Create();')
    lines.append(f"")
    # C API wrappers
    for m in comp["methods"]:
        ret = cpp_to_cabi(m["return"])
        params = ", ".join(f"{cpp_to_cabi(t)} {n}" for t, n, _ in m["params"])
        if m["return"] == "void":
            if params:
                lines.append(f'extern "C" NEWCOM_EXPORT void {name}_{m["name"]}(void* h, {params});')
            else:
                lines.append(f'extern "C" NEWCOM_EXPORT void {name}_{m["name"]}(void* h);')
        else:
            if params:
                lines.append(f'extern "C" NEWCOM_EXPORT {ret} {name}_{m["name"]}(void* h, {params});')
            else:
                lines.append(f'extern "C" NEWCOM_EXPORT {ret} {name}_{m["name"]}(void* h);')
    lines.append(f'extern "C" NEWCOM_EXPORT uint32_t {name}_Release(void* h);')
    lines.append(f"")
    return "\n".join(lines)

def gen_implementation(comp):
    """Generate the implementation shim."""
    name = comp["name"]
    lines = []
    lines.append(f"// {name.lower()}_newcom.cpp — NewCOM implementation")
    lines.append(f'#include "{name.lower()}_newcom.h"')
    lines.append(f"#include <cstring>")
    lines.append(f"#include <cstdlib>")
    lines.append(f"#include <atomic>")
    lines.append(f"")
    lines.append(f"// ─── Implementation struct ────────────────────────────────────────────────────")
    lines.append(f"struct {name}Impl {{")
    lines.append(f"    const I{name}Vtbl* vtbl;")
    lines.append(f"    std::atomic<uint32_t> refCount;")
    lines.append(f"")
    lines.append(f"    {name}Impl() : refCount(1), vtbl(nullptr) {{}}")
    lines.append(f"}};")
    lines.append(f"")
    lines.append(f"// ─── Vtable function implementations ─────────────────────────────────────────")
    lines.append(f"static int32_t {name}_QueryInterface(INewUnknown* self, const NewComGUID* iid, void** out) {{")
    lines.append(f"    *out = self;")
    lines.append(f"    reinterpret_cast<{name}Impl*>(self)->refCount++;")
    lines.append(f"    return NEWCOM_OK;")
    lines.append(f"}}")
    lines.append(f"")
    lines.append(f"static uint32_t {name}_AddRef(INewUnknown* self) {{")
    lines.append(f"    return ++reinterpret_cast<{name}Impl*>(self)->refCount;")
    lines.append(f"}}")
    lines.append(f"")
    lines.append(f"static uint32_t {name}_Release(INewUnknown* self) {{")
    lines.append(f"    auto impl = reinterpret_cast<{name}Impl*>(self);")
    lines.append(f"    uint32_t c = --impl->refCount;")
    lines.append(f"    if (c == 0) delete impl;")
    lines.append(f"    return c;")
    lines.append(f"}}")
    lines.append(f"")
    for m in comp["methods"]:
        ret = cpp_to_cabi(m["return"])
        params = ", ".join(f"{cpp_to_cabi(t)} {n}" for t, n, _ in m["params"])
        if params:
            lines.append(f"static {ret} {name}_{m['name']}(I{name}* self, {params}) {{")
        else:
            lines.append(f"static {ret} {name}_{m['name']}(I{name}* self) {{")
        lines.append(f"    // TODO: implement {m['name']}")
        if m["return"] != "void":
            if m["return"] in ("int", "int32_t", "int32", "long", "int64_t", "int64"):
                lines.append(f"    return 0;")
            elif m["return"] in ("float", "double", "Single", "Double"):
                lines.append(f"    return 0.0;")
            elif m["return"] in ("bool", "Boolean"):
                lines.append(f"    return false;")
            elif m["return"] in ("char*", "const char*", "std::string", "String"):
                lines.append(f"    return nullptr;")
            else:
                lines.append(f"    return 0;")
        lines.append(f"}}")
        lines.append(f"")
    lines.append(f"// ─── Static vtable instance ──────────────────────────────────────────────────")
    lines.append(f"static const I{name}Vtbl g_vtbl = {{")
    lines.append(f"    {name}_QueryInterface,")
    lines.append(f"    {name}_AddRef,")
    lines.append(f"    {name}_Release,")
    for m in comp["methods"]:
        lines.append(f"    {name}_{m['name']},")
    lines.append(f"}};")
    lines.append(f"")
    lines.append(f"// ─── Factory (C ABI) ─────────────────────────────────────────────────────────")
    lines.append(f'extern "C" I{name}* {name}_Create() {{')
    lines.append(f"    auto impl = new {name}Impl();")
    lines.append(f"    impl->vtbl = &g_vtbl;")
    lines.append(f"    return reinterpret_cast<I{name}*>(impl);")
    lines.append(f"}}")
    lines.append(f"")
    lines.append(f"// ─── C API wrappers (for BasicLang P/Invoke) ────────────────────────────────")
    for m in comp["methods"]:
        ret = cpp_to_cabi(m["return"])
        params = ", ".join(f"{cpp_to_cabi(t)} {n}" for t, n, _ in m["params"])
        if m["return"] == "void":
            if params:
                lines.append(f'extern "C" void {name}_{m["name"]}(void* h, {params}) {{')
            else:
                lines.append(f'extern "C" void {name}_{m["name"]}(void* h) {{')
        else:
            if params:
                lines.append(f'extern "C" {ret} {name}_{m["name"]}(void* h, {params}) {{')
            else:
                lines.append(f'extern "C" {ret} {name}_{m["name"]}(void* h) {{')
        param_names = ", ".join(n for _, n, _ in m["params"])
        if m["return"] == "void":
            if param_names:
                lines.append(f"    reinterpret_cast<I{name}*>(h)->vtbl->{m['name']}(reinterpret_cast<I{name}*>(h), {param_names});")
            else:
                lines.append(f"    reinterpret_cast<I{name}*>(h)->vtbl->{m['name']}(reinterpret_cast<I{name}*>(h));")
        else:
            if param_names:
                lines.append(f"    return reinterpret_cast<I{name}*>(h)->vtbl->{m['name']}(reinterpret_cast<I{name}*>(h), {param_names});")
            else:
                lines.append(f"    return reinterpret_cast<I{name}*>(h)->vtbl->{m['name']}(reinterpret_cast<I{name}*>(h));")
        lines.append(f"}}")
        lines.append(f"")
    lines.append(f'extern "C" uint32_t {name}_AddRef(void* h) {{')
    lines.append(f"    return reinterpret_cast<I{name}*>(h)->vtbl->AddRef(reinterpret_cast<INewUnknown*>(h));")
    lines.append(f"}}")
    lines.append(f"")
    lines.append(f'extern "C" NEWCOM_EXPORT uint32_t {name}_Release(void* h) {{')
    lines.append(f"    return reinterpret_cast<I{name}*>(h)->vtbl->Release(reinterpret_cast<INewUnknown*>(h));")
    lines.append(f"}}")
    lines.append(f"")
    return "\n".join(lines)

def gen_basiclang(comp):
    """Generate BasicLang binding module."""
    name = comp["name"]
    lines = []
    lines.append(f"' {name.lower()}.bas — BasicLang binding for NewCOM {name} component")
    lines.append(f"")
    lines.append(f'Declare Function {name}_Create Lib "{name.lower()}_newcom" () As Integer')
    for m in comp["methods"]:
        ret = cpp_to_basiclang(m["return"])
        params = ", ".join(f"{n} As {cpp_to_basiclang(t)}" for t, n, _ in m["params"])
        if m["return"] == "void":
            if params:
                lines.append(f'Declare Sub {name}_{m["name"]} Lib "{name.lower()}_newcom" (h As Integer, {params})')
            else:
                lines.append(f'Declare Sub {name}_{m["name"]} Lib "{name.lower()}_newcom" (h As Integer)')
        else:
            if params:
                lines.append(f'Declare Function {name}_{m["name"]} Lib "{name.lower()}_newcom" (h As Integer, {params}) As {ret}')
            else:
                lines.append(f'Declare Function {name}_{m["name"]} Lib "{name.lower()}_newcom" (h As Integer) As {ret}')
    lines.append(f'Declare Function {name}_AddRef Lib "{name.lower()}_newcom" (h As Integer) As Integer')
    lines.append(f'Declare Sub {name}_Release Lib "{name.lower()}_newcom" (h As Integer)')
    lines.append(f"")
    lines.append(f"Class {name}")
    lines.append(f"    Private _handle As Integer")
    lines.append(f"")
    lines.append(f"    Public Sub New()")
    lines.append(f"        _handle = {name}_Create()")
    lines.append(f"    End Sub")
    lines.append(f"")
    for m in comp["methods"]:
        ret = cpp_to_basiclang(m["return"])
        params = ", ".join(f"{n} As {cpp_to_basiclang(t)}" for t, n, _ in m["params"])
        if m["return"] == "void":
            lines.append(f"    Public Sub {m['name']}({params})")
            if params:
                pnames = ", ".join(n for _, n, _ in m["params"])
                lines.append(f"        {name}_{m['name']}(_handle, {pnames})")
            else:
                lines.append(f"        {name}_{m['name']}(_handle)")
            lines.append(f"    End Sub")
        else:
            lines.append(f"    Public Function {m['name']}({params}) As {ret}")
            if params:
                pnames = ", ".join(n for _, n, _ in m["params"])
                lines.append(f"        Return {name}_{m['name']}(_handle, {pnames})")
            else:
                lines.append(f"        Return {name}_{m['name']}(_handle)")
            lines.append(f"    End Function")
        lines.append(f"")
    lines.append(f"    Public Sub Close()")
    lines.append(f"        {name}_Release(_handle)")
    lines.append(f"        _handle = 0")
    lines.append(f"    End Sub")
    lines.append(f"End Class")
    lines.append(f"")
    return "\n".join(lines)

def main():
    if len(sys.argv) < 2:
        print("Usage: newcom_gen.py <input.h> [-o output_dir]")
        sys.exit(1)

    input_path = sys.argv[1]
    output_dir = "."
    if "-o" in sys.argv:
        output_dir = sys.argv[sys.argv.index("-o") + 1]

    os.makedirs(output_dir, exist_ok=True)

    with open(input_path, encoding="utf-8") as f:
        text = f.read()

    components = parse_annotations(text)
    if not components:
        print("No NEWCOM_COMPONENT annotations found.")
        sys.exit(1)

    for comp in components:
        name = comp["name"]
        lower = name.lower()

        header = gen_interface(comp)
        with open(os.path.join(output_dir, f"{lower}_newcom.h"), "w", encoding="utf-8") as f:
            f.write(header)
        print(f"Generated {lower}_newcom.h")

        impl = gen_implementation(comp)
        with open(os.path.join(output_dir, f"{lower}_newcom.cpp"), "w", encoding="utf-8") as f:
            f.write(impl)
        print(f"Generated {lower}_newcom.cpp")

        binding = gen_basiclang(comp)
        with open(os.path.join(output_dir, f"{lower}.bas"), "w", encoding="utf-8") as f:
            f.write(binding)
        print(f"Generated {lower}.bas")

    print(f"\nDone! {len(components)} component(s) generated.")

if __name__ == "__main__":
    main()
