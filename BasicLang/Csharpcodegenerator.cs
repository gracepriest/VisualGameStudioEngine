using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.CSharp
{
    /// <summary>
    /// C# Code Generator - wrapper around ImprovedCSharpCodeGenerator that implements ICodeGenerator
    /// </summary>
    public class CSharpCodeGenerator : ICodeGenerator
    {
        private readonly ImprovedCSharpCodeGenerator _generator;
        private readonly ITypeMapper _typeMapper;

        public string BackendName => "C#";
        public TargetPlatform Target => TargetPlatform.CSharp;
        public ITypeMapper TypeMapper => _typeMapper;

        public CSharpCodeGenerator(CodeGenOptions options = null)
        {
            _generator = new ImprovedCSharpCodeGenerator(options);
            _typeMapper = new CSharpTypeMapper();
        }

        public string Generate(IRModule module)
        {
            return _generator.Generate(module);
        }

        // Delegate visitor methods to internal generator
        public void Visit(IRFunction function) => _generator.Visit(function);
        public void Visit(BasicBlock block) => _generator.Visit(block);
        public void Visit(IRConstant constant) => _generator.Visit(constant);
        public void Visit(IRVariable variable) => _generator.Visit(variable);
        public void Visit(IRBinaryOp binaryOp) => _generator.Visit(binaryOp);
        public void Visit(IRUnaryOp unaryOp) => _generator.Visit(unaryOp);
        public void Visit(IRAssignment assignment) => _generator.Visit(assignment);
        public void Visit(IRLoad load) => _generator.Visit(load);
        public void Visit(IRStore store) => _generator.Visit(store);
        public void Visit(IRCall call) => _generator.Visit(call);
        public void Visit(IRReturn ret) => _generator.Visit(ret);
        public void Visit(IRBranch branch) => _generator.Visit(branch);
        public void Visit(IRConditionalBranch condBranch) => _generator.Visit(condBranch);
        public void Visit(IRPhi phi) => _generator.Visit(phi);
        public void Visit(IRAlloca alloca) => _generator.Visit(alloca);
        public void Visit(IRGetElementPtr gep) => _generator.Visit(gep);
        public void Visit(IRCast cast) => _generator.Visit(cast);
        public void Visit(IRCompare compare) => _generator.Visit(compare);
        public void Visit(IRSwitch switchInst) => _generator.Visit(switchInst);
        public void Visit(IRLabel label) => _generator.Visit(label);
        public void Visit(IRComment comment) => _generator.Visit(comment);
        public void Visit(IRArrayAlloc arrayAlloc) => _generator.Visit(arrayAlloc);
        public void Visit(IRArrayStore arrayStore) => _generator.Visit(arrayStore);
        public void Visit(IRAwait awaitInst) => _generator.Visit(awaitInst);
        public void Visit(IRYield yieldInst) => _generator.Visit(yieldInst);
        public void Visit(IRNewObject newObj) => _generator.Visit(newObj);
        public void Visit(IRInstanceMethodCall methodCall) => _generator.Visit(methodCall);
        public void Visit(IRBaseMethodCall baseCall) => _generator.Visit(baseCall);
        public void Visit(IRFieldAccess fieldAccess) => _generator.Visit(fieldAccess);
        public void Visit(IRFieldStore fieldStore) => _generator.Visit(fieldStore);
        public void Visit(IRTupleElement tupleElement) => _generator.Visit(tupleElement);
        public void Visit(IRTryCatch tryCatch) => _generator.Visit(tryCatch);
        public void Visit(IRInlineCode inlineCode) => _generator.Visit(inlineCode);
    }
}