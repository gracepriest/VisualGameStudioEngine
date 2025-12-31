; ModuleID = 'SumSquares'
source_filename = "SumSquares.bas"
target datalayout = "e-m:w-i64:64-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-windows-msvc"

; Format strings for printf
@.fmt.int = private unnamed_addr constant [4 x i8] c"%d\0A\00"
@.fmt.long = private unnamed_addr constant [5 x i8] c"%ld\0A\00"
@.fmt.double = private unnamed_addr constant [4 x i8] c"%f\0A\00"
@.fmt.str = private unnamed_addr constant [4 x i8] c"%s\0A\00"
@.fmt.0 = private unnamed_addr constant [4 x i8] c"%d\0A\00"

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

define i32 @Square(i32 %x) {
entry:
  %x.addr = alloca i32
  store i32 %x, i32* %x.addr

  %t1 = load i32, i32* %x.addr
  %t2 = load i32, i32* %x.addr
  %t0 = mul i32 %t1, %t2
  ret i32 %t0
}

define i32 @SumSquares(i32 %n) {
entry:
  %sum.addr = alloca i32
  store i32 0, i32* %sum.addr
  %i.addr = alloca i32
  store i32 0, i32* %i.addr
  %n.addr = alloca i32
  store i32 %n, i32* %n.addr

  store i32 0, i32* %sum.addr
  store i32 1, i32* %i.addr
  br label %for_cond
for_cond:
  %t1 = load i32, i32* %i.addr
  %t2 = load i32, i32* %n.addr
  %t0 = icmp sle i32 %t1, %t2
  br i1 %t0, label %for_body, label %for_end
for_body:
  %t3 = call i32 @Square(i32 1)
  store i32 %t3, i32* %sum.addr
  br label %for_inc
for_inc:
  store i32 2, i32* %i.addr
  br label %for_cond
for_end:
  ret i32 0
}

define void @Main() {
entry:
  %result.addr = alloca i32
  store i32 0, i32* %result.addr

  %t0 = call i32 @SumSquares(i32 5)
  store i32 %t0, i32* %result.addr
  %t1 = load i32, i32* %result.addr
  call i32 (i8*, ...) @printf(i8* getelementptr inbounds ([4 x i8], [4 x i8]* @.fmt.0, i64 0, i64 0), i32 %t1)
  ret void
}

