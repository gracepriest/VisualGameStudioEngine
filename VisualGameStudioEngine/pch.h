// pch.h: This is a precompiled header file.
// Files listed below are compiled only once, improving build performance for future builds.
// This also affects IntelliSense performance, including code completion and many code browsing features.
// However, files listed here are ALL re-compiled if any one of them is updated between builds.
// Do not add files here that you will be updating frequently as this negates the performance advantage.

#ifndef PCH_H
#define PCH_H
// Define this BEFORE any Windows headers to exclude problematic functions
#define WIN32_LEAN_AND_MEAN
#define NOGDI          // Excludes GDI functions
#define NOUSER         // Excludes USER functions  
#define NOMINMAX       // Excludes min/max macros
// add headers that you want to pre-compile here
#include "raylib.h"
#include "platform.h"

#endif //PCH_H
