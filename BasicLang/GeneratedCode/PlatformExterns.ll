; ModuleID = 'PlatformExterns'
source_filename = "PlatformExterns.bas"
target datalayout = "e-m:w-i64:64-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-windows-msvc"

; Format strings for printf
@.fmt.int = private unnamed_addr constant [4 x i8] c"%d\0A\00"
@.fmt.long = private unnamed_addr constant [5 x i8] c"%ld\0A\00"
@.fmt.double = private unnamed_addr constant [4 x i8] c"%f\0A\00"
@.fmt.str = private unnamed_addr constant [4 x i8] c"%s\0A\00"
@.fmt.0 = private unnamed_addr constant [4 x i8] c"%d\0A\00"

; String constants
@.str.0 = private unnamed_addr constant [22 x i8] c"Hello from BasicLang!\00"
@.str.1 = private unnamed_addr constant [17 x i8] c"Message result: \00"

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

define void @Main() {
entry:
  %result.addr = alloca i32
  store i32 0, i32* %result.addr

  %t0 = call i32 @@printf(i8* getelementptr inbounds ([22 x i8], [22 x i8]* @.str.0, i64 0, i64 0))
  store i32 %t0, i32* %result.addr
  call i32 @puts(i8* getelementptr inbounds ([17 x i8], [17 x i8]* @.str.1, i64 0, i64 0))
  %t1 = load i32, i32* %result.addr
  call i32 (i8*, ...) @printf(i8* getelementptr inbounds ([4 x i8], [4 x i8]* @.fmt.0, i64 0, i64 0), i32 %t1)
  ret void
}

