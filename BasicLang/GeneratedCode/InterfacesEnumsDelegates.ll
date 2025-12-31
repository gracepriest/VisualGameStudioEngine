; ModuleID = 'InterfacesEnumsDelegates'
source_filename = "InterfacesEnumsDelegates.bas"
target datalayout = "e-m:w-i64:64-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-windows-msvc"

; Enum constants
@Color_Red = constant i32 0
@Color_Green = constant i32 5
@Color_Blue = constant i32 6
@DayOfWeek_Sunday = constant i32 0
@DayOfWeek_Monday = constant i32 1
@DayOfWeek_Tuesday = constant i32 2
@DayOfWeek_Wednesday = constant i32 3
@DayOfWeek_Thursday = constant i32 4
@DayOfWeek_Friday = constant i32 5
@DayOfWeek_Saturday = constant i32 6

; Class struct types
%class.Circle = type { double }

; Delegate types (function pointers)
%delegate.MathOperation = type i32 (i32, i32)*
%delegate.EventHandler = type void (i8*, i8*)*

; Interface vtable types
%vtable.IShape = type { double (i8*)*, double (i8*)* }

; Format strings for printf
@.fmt.int = private unnamed_addr constant [4 x i8] c"%d\0A\00"
@.fmt.long = private unnamed_addr constant [5 x i8] c"%ld\0A\00"
@.fmt.double = private unnamed_addr constant [4 x i8] c"%f\0A\00"
@.fmt.str = private unnamed_addr constant [4 x i8] c"%s\0A\00"
@.fmt.0 = private unnamed_addr constant [4 x i8] c"%d\0A\00"

; String constants
@.str.0 = private unnamed_addr constant [14 x i8] c"Circle area: \00"
@.str.1 = private unnamed_addr constant [19 x i8] c"Circle perimeter: \00"

; External function declarations
declare i32 @printf(i8*, ...)
declare i32 @puts(i8*)
declare i32 @scanf(i8*, ...)
declare double @sqrt(double)
declare double @pow(double, double)
declare double @sin(double)
declare double @cos(double)
declare double @tan(double)
declare double @log(double)
declare double @exp(double)
declare double @floor(double)
declare double @ceil(double)
declare double @fabs(double)
declare i32 @rand()
declare void @srand(i32)
declare i64 @time(i64*)

; String functions
declare i64 @strlen(i8*)
declare i8* @strcpy(i8*, i8*)
declare i8* @strcat(i8*, i8*)
declare i8* @malloc(i64)
declare void @free(i8*)

; String concatenation helper
define i8* @__concat_strings(i8* %s1, i8* %s2) {
entry:
  %len1 = call i64 @strlen(i8* %s1)
  %len2 = call i64 @strlen(i8* %s2)
  %total = add i64 %len1, %len2
  %total1 = add i64 %total, 1
  %buf = call i8* @malloc(i64 %total1)
  call i8* @strcpy(i8* %buf, i8* %s1)
  call i8* @strcat(i8* %buf, i8* %s2)
  ret i8* %buf
}

; Class methods
define %class.Circle* @Circle_ctor(double %radius) {
entry:
  %this = alloca %class.Circle
  %radius.addr = alloca double
  store double %radius, double* %radius.addr
  %t0 = load double, double* %radius.addr
  store double %t0, double* %_radius.addr
  ret void
  ret %class.Circle* %this
}

define double @Circle_GetArea(%class.Circle* %this) {
entry:
  %t0 = fmul double 3.1415899999999999, %_radius
  %t1 = fmul double %t0, %_radius
  ret double %t1
}

define double @Circle_GetPerimeter(%class.Circle* %this) {
entry:
  %t0 = fmul double 2, 3.1415899999999999
  %t1 = fmul double %t0, %_radius
  ret double %t1
}

define void @Main() {
entry:
  %circle.addr = alloca Circle
  store Circle zeroinitializer, Circle* %circle.addr

  %t0 = call %class.Circle* @Circle_ctor(double 5)
  store Circle %t0, Circle* %circle.addr
  %t1 = load Circle, Circle* %circle.addr
  %t2 = call double @Circle_GetArea(%class.Circle* %t1)
  %t3 = call i8* @__concat_strings(i8* getelementptr inbounds ([14 x i8], [14 x i8]* @.str.0, i64 0, i64 0), i8* %t2)
  call i32 (i8*, ...) @printf(i8* getelementptr inbounds ([4 x i8], [4 x i8]* @.fmt.0, i64 0, i64 0), i8* %t3)
  %t4 = load Circle, Circle* %circle.addr
  %t5 = call double @Circle_GetPerimeter(%class.Circle* %t4)
  %t6 = call i8* @__concat_strings(i8* getelementptr inbounds ([19 x i8], [19 x i8]* @.str.1, i64 0, i64 0), i8* %t5)
  call i32 (i8*, ...) @printf(i8* getelementptr inbounds ([4 x i8], [4 x i8]* @.fmt.0, i64 0, i64 0), i8* %t6)
  ret void
}

