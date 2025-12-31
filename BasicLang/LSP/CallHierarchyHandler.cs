using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicLang.Compiler.AST;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles call hierarchy prepare requests
    /// </summary>
    public class CallHierarchyPrepareHandler : ICallHierarchyPrepareHandler
    {
        private readonly DocumentManager _documentManager;
        private CallHierarchyCapability _capability;

        public CallHierarchyPrepareHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public Guid Id { get; } = Guid.NewGuid();

        public void SetCapability(CallHierarchyCapability capability, ClientCapabilities clientCapabilities)
        {
            _capability = capability;
        }

        public Task<Container<CallHierarchyItem>?> Handle(CallHierarchyPrepareParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null || state.AST == null)
            {
                return Task.FromResult<Container<CallHierarchyItem>>(null);
            }

            // Get the word at the cursor position
            var word = state.GetWordAtPosition(request.Position.Line, request.Position.Character);
            if (string.IsNullOrEmpty(word))
            {
                return Task.FromResult<Container<CallHierarchyItem>>(null);
            }

            // Find the function/sub definition at this position
            var callableNode = FindCallableAtPosition(state, word);
            if (callableNode == null)
            {
                return Task.FromResult<Container<CallHierarchyItem>>(null);
            }

            var item = CreateCallHierarchyItem(state.Uri, callableNode);
            if (item == null)
            {
                return Task.FromResult<Container<CallHierarchyItem>>(null);
            }

            return Task.FromResult(new Container<CallHierarchyItem>(item));
        }

        private ASTNode FindCallableAtPosition(DocumentState state, string name)
        {
            foreach (var decl in state.AST.Declarations)
            {
                var callable = FindCallableInNode(decl, name);
                if (callable != null)
                    return callable;
            }
            return null;
        }

        private ASTNode FindCallableInNode(ASTNode node, string name)
        {
            switch (node)
            {
                case FunctionNode func when func.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                    return func;

                case SubroutineNode sub when sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                    return sub;

                case ClassNode cls:
                    foreach (var member in cls.Members)
                    {
                        var callable = FindCallableInNode(member, name);
                        if (callable != null)
                            return callable;
                    }
                    break;

                case ModuleNode module:
                    foreach (var member in module.Members)
                    {
                        var callable = FindCallableInNode(member, name);
                        if (callable != null)
                            return callable;
                    }
                    break;
            }

            return null;
        }

        private CallHierarchyItem CreateCallHierarchyItem(DocumentUri uri, ASTNode node)
        {
            switch (node)
            {
                case FunctionNode func:
                    var funcParams = string.Join(", ", func.Parameters.Select(p => $"{p.Name} As {p.Type?.Name ?? "Variant"}"));
                    return new CallHierarchyItem
                    {
                        Name = func.Name,
                        Kind = SymbolKind.Function,
                        Detail = $"({funcParams}) As {func.ReturnType?.Name ?? "Void"}",
                        Uri = uri,
                        Range = new LspRange(
                            new Position(func.Line - 1, 0),
                            new Position(func.Line + 10, 0)),
                        SelectionRange = new LspRange(
                            new Position(func.Line - 1, func.Column - 1),
                            new Position(func.Line - 1, func.Column - 1 + func.Name.Length))
                    };

                case SubroutineNode sub:
                    var subParams = string.Join(", ", sub.Parameters.Select(p => $"{p.Name} As {p.Type?.Name ?? "Variant"}"));
                    return new CallHierarchyItem
                    {
                        Name = sub.Name,
                        Kind = SymbolKind.Method,
                        Detail = $"({subParams})",
                        Uri = uri,
                        Range = new LspRange(
                            new Position(sub.Line - 1, 0),
                            new Position(sub.Line + 10, 0)),
                        SelectionRange = new LspRange(
                            new Position(sub.Line - 1, sub.Column - 1),
                            new Position(sub.Line - 1, sub.Column - 1 + sub.Name.Length))
                    };
            }

            return null;
        }

        public CallHierarchyRegistrationOptions GetRegistrationOptions(CallHierarchyCapability capability, ClientCapabilities clientCapabilities)
        {
            return new CallHierarchyRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }

    /// <summary>
    /// Handles incoming call hierarchy requests (find callers)
    /// </summary>
    public class CallHierarchyIncomingHandler : ICallHierarchyIncomingHandler
    {
        private readonly DocumentManager _documentManager;
        private CallHierarchyCapability _capability;

        public CallHierarchyIncomingHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public Guid Id { get; } = Guid.NewGuid();

        public void SetCapability(CallHierarchyCapability capability, ClientCapabilities clientCapabilities)
        {
            _capability = capability;
        }

        public Task<Container<CallHierarchyIncomingCall>?> Handle(CallHierarchyIncomingCallsParams request, CancellationToken cancellationToken)
        {
            var targetName = request.Item.Name;
            var incomingCalls = new List<CallHierarchyIncomingCall>();

            // Search through all open documents for callers
            foreach (var document in _documentManager.GetAllDocuments())
            {
                if (document.AST == null)
                    continue;

                var callers = FindCallersInDocument(document, targetName);
                foreach (var (caller, callSites) in callers)
                {
                    var fromItem = CreateCallHierarchyItem(document.Uri, caller);
                    if (fromItem != null)
                    {
                        incomingCalls.Add(new CallHierarchyIncomingCall
                        {
                            From = fromItem,
                            FromRanges = callSites.Select(cs => cs.Range).ToArray()
                        });
                    }
                }
            }

            if (incomingCalls.Count == 0)
            {
                return Task.FromResult<Container<CallHierarchyIncomingCall>>(null);
            }

            return Task.FromResult(new Container<CallHierarchyIncomingCall>(incomingCalls));
        }

        private List<(ASTNode Caller, List<CallSite> CallSites)> FindCallersInDocument(DocumentState state, string targetName)
        {
            var callers = new List<(ASTNode, List<CallSite>)>();

            foreach (var decl in state.AST.Declarations)
            {
                var callersInNode = FindCallersInNode(state, decl, targetName);
                callers.AddRange(callersInNode);
            }

            return callers;
        }

        private List<(ASTNode Caller, List<CallSite> CallSites)> FindCallersInNode(DocumentState state, ASTNode node, string targetName)
        {
            var callers = new List<(ASTNode, List<CallSite>)>();

            switch (node)
            {
                case FunctionNode func:
                    var funcCalls = FindCallsInBody(func.Body, targetName);
                    if (funcCalls.Count > 0)
                    {
                        callers.Add((func, funcCalls));
                    }
                    break;

                case SubroutineNode sub:
                    var subCalls = FindCallsInBody(sub.Body, targetName);
                    if (subCalls.Count > 0)
                    {
                        callers.Add((sub, subCalls));
                    }
                    break;

                case ClassNode cls:
                    foreach (var member in cls.Members)
                    {
                        var memberCallers = FindCallersInNode(state, member, targetName);
                        callers.AddRange(memberCallers);
                    }
                    break;

                case ModuleNode module:
                    foreach (var member in module.Members)
                    {
                        var memberCallers = FindCallersInNode(state, member, targetName);
                        callers.AddRange(memberCallers);
                    }
                    break;
            }

            return callers;
        }

        private List<CallSite> FindCallsInBody(BlockNode body, string targetName)
        {
            var calls = new List<CallSite>();

            if (body == null)
                return calls;

            foreach (var statement in body.Statements)
            {
                FindCallsInStatement(statement, targetName, calls);
            }

            return calls;
        }

        private void FindCallsInStatement(StatementNode statement, string targetName, List<CallSite> calls)
        {
            switch (statement)
            {
                case ExpressionStatementNode exprStmt:
                    FindCallsInExpression(exprStmt.Expression, targetName, calls);
                    break;

                case AssignmentStatementNode assign:
                    FindCallsInExpression(assign.Target, targetName, calls);
                    FindCallsInExpression(assign.Value, targetName, calls);
                    break;

                case IfStatementNode ifStmt:
                    FindCallsInExpression(ifStmt.Condition, targetName, calls);
                    FindCallsInBody(ifStmt.ThenBlock, targetName);
                    foreach (var (condition, block) in ifStmt.ElseIfClauses)
                    {
                        FindCallsInExpression(condition, targetName, calls);
                        FindCallsInBody(block, targetName);
                    }
                    FindCallsInBody(ifStmt.ElseBlock, targetName);
                    break;

                case ForLoopNode forLoop:
                    FindCallsInExpression(forLoop.Start, targetName, calls);
                    FindCallsInExpression(forLoop.End, targetName, calls);
                    FindCallsInExpression(forLoop.Step, targetName, calls);
                    FindCallsInBody(forLoop.Body, targetName);
                    break;

                case WhileLoopNode whileLoop:
                    FindCallsInExpression(whileLoop.Condition, targetName, calls);
                    FindCallsInBody(whileLoop.Body, targetName);
                    break;

                case DoLoopNode doLoop:
                    FindCallsInExpression(doLoop.Condition, targetName, calls);
                    FindCallsInBody(doLoop.Body, targetName);
                    break;

                case ForEachLoopNode forEach:
                    FindCallsInExpression(forEach.Collection, targetName, calls);
                    FindCallsInBody(forEach.Body, targetName);
                    break;

                case ReturnStatementNode returnStmt:
                    FindCallsInExpression(returnStmt.Value, targetName, calls);
                    break;

                case VariableDeclarationNode varDecl:
                    FindCallsInExpression(varDecl.Initializer, targetName, calls);
                    break;

                case BlockNode block:
                    foreach (var stmt in block.Statements)
                    {
                        FindCallsInStatement(stmt, targetName, calls);
                    }
                    break;
            }
        }

        private void FindCallsInExpression(ExpressionNode expression, string targetName, List<CallSite> calls)
        {
            if (expression == null)
                return;

            switch (expression)
            {
                case CallExpressionNode call:
                    // Check if this is a call to the target function
                    string calleeName = null;
                    if (call.Callee is IdentifierExpressionNode identifier)
                    {
                        calleeName = identifier.Name;
                    }
                    else if (call.Callee is MemberAccessExpressionNode member)
                    {
                        calleeName = member.MemberName;
                    }

                    if (!string.IsNullOrEmpty(calleeName) &&
                        calleeName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        calls.Add(new CallSite
                        {
                            Range = new LspRange(
                                new Position(call.Line - 1, call.Column - 1),
                                new Position(call.Line - 1, call.Column - 1 + calleeName.Length))
                        });
                    }

                    // Also search in arguments
                    foreach (var arg in call.Arguments)
                    {
                        FindCallsInExpression(arg, targetName, calls);
                    }
                    break;

                case BinaryExpressionNode binary:
                    FindCallsInExpression(binary.Left, targetName, calls);
                    FindCallsInExpression(binary.Right, targetName, calls);
                    break;

                case UnaryExpressionNode unary:
                    FindCallsInExpression(unary.Operand, targetName, calls);
                    break;

                case MemberAccessExpressionNode member:
                    FindCallsInExpression(member.Object, targetName, calls);
                    break;

                case ArrayAccessExpressionNode arrayAccess:
                    FindCallsInExpression(arrayAccess.Array, targetName, calls);
                    foreach (var index in arrayAccess.Indices)
                    {
                        FindCallsInExpression(index, targetName, calls);
                    }
                    break;

                case NewExpressionNode newExpr:
                    foreach (var arg in newExpr.Arguments)
                    {
                        FindCallsInExpression(arg, targetName, calls);
                    }
                    break;

                case CastExpressionNode cast:
                    FindCallsInExpression(cast.Expression, targetName, calls);
                    break;
            }
        }

        private CallHierarchyItem CreateCallHierarchyItem(DocumentUri uri, ASTNode node)
        {
            switch (node)
            {
                case FunctionNode func:
                    var funcParams = string.Join(", ", func.Parameters.Select(p => $"{p.Name} As {p.Type?.Name ?? "Variant"}"));
                    return new CallHierarchyItem
                    {
                        Name = func.Name,
                        Kind = SymbolKind.Function,
                        Detail = $"({funcParams}) As {func.ReturnType?.Name ?? "Void"}",
                        Uri = uri,
                        Range = new LspRange(
                            new Position(func.Line - 1, 0),
                            new Position(func.Line + 10, 0)),
                        SelectionRange = new LspRange(
                            new Position(func.Line - 1, func.Column - 1),
                            new Position(func.Line - 1, func.Column - 1 + func.Name.Length))
                    };

                case SubroutineNode sub:
                    var subParams = string.Join(", ", sub.Parameters.Select(p => $"{p.Name} As {p.Type?.Name ?? "Variant"}"));
                    return new CallHierarchyItem
                    {
                        Name = sub.Name,
                        Kind = SymbolKind.Method,
                        Detail = $"({subParams})",
                        Uri = uri,
                        Range = new LspRange(
                            new Position(sub.Line - 1, 0),
                            new Position(sub.Line + 10, 0)),
                        SelectionRange = new LspRange(
                            new Position(sub.Line - 1, sub.Column - 1),
                            new Position(sub.Line - 1, sub.Column - 1 + sub.Name.Length))
                    };
            }

            return null;
        }

        public CallHierarchyRegistrationOptions GetRegistrationOptions(CallHierarchyCapability capability, ClientCapabilities clientCapabilities)
        {
            return new CallHierarchyRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }

        private class CallSite
        {
            public LspRange Range { get; set; }
        }
    }

    /// <summary>
    /// Handles outgoing call hierarchy requests (find callees)
    /// </summary>
    public class CallHierarchyOutgoingHandler : ICallHierarchyOutgoingHandler
    {
        private readonly DocumentManager _documentManager;
        private CallHierarchyCapability _capability;

        public CallHierarchyOutgoingHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public Guid Id { get; } = Guid.NewGuid();

        public void SetCapability(CallHierarchyCapability capability, ClientCapabilities clientCapabilities)
        {
            _capability = capability;
        }

        public Task<Container<CallHierarchyOutgoingCall>?> Handle(CallHierarchyOutgoingCallsParams request, CancellationToken cancellationToken)
        {
            var sourceName = request.Item.Name;
            var sourceUri = request.Item.Uri;

            var state = _documentManager.GetDocument(sourceUri);
            if (state == null || state.AST == null)
            {
                return Task.FromResult<Container<CallHierarchyOutgoingCall>>(null);
            }

            // Find the source function/sub
            var sourceNode = FindCallableByName(state, sourceName);
            if (sourceNode == null)
            {
                return Task.FromResult<Container<CallHierarchyOutgoingCall>>(null);
            }

            // Find all callees from this function
            var callees = FindCalleesInNode(state, sourceNode);

            if (callees.Count == 0)
            {
                return Task.FromResult<Container<CallHierarchyOutgoingCall>>(null);
            }

            return Task.FromResult(new Container<CallHierarchyOutgoingCall>(callees));
        }

        private ASTNode FindCallableByName(DocumentState state, string name)
        {
            foreach (var decl in state.AST.Declarations)
            {
                var callable = FindCallableInNode(decl, name);
                if (callable != null)
                    return callable;
            }
            return null;
        }

        private ASTNode FindCallableInNode(ASTNode node, string name)
        {
            switch (node)
            {
                case FunctionNode func when func.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                    return func;

                case SubroutineNode sub when sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                    return sub;

                case ClassNode cls:
                    foreach (var member in cls.Members)
                    {
                        var callable = FindCallableInNode(member, name);
                        if (callable != null)
                            return callable;
                    }
                    break;

                case ModuleNode module:
                    foreach (var member in module.Members)
                    {
                        var callable = FindCallableInNode(member, name);
                        if (callable != null)
                            return callable;
                    }
                    break;
            }

            return null;
        }

        private List<CallHierarchyOutgoingCall> FindCalleesInNode(DocumentState state, ASTNode node)
        {
            var outgoingCalls = new List<CallHierarchyOutgoingCall>();
            BlockNode body = null;

            switch (node)
            {
                case FunctionNode func:
                    body = func.Body;
                    break;

                case SubroutineNode sub:
                    body = sub.Body;
                    break;
            }

            if (body == null)
                return outgoingCalls;

            // Find all calls in the body
            var callsByTarget = new Dictionary<string, List<CallSite>>(StringComparer.OrdinalIgnoreCase);
            FindAllCallsInBody(body, callsByTarget);

            // For each unique callee, create an outgoing call entry
            foreach (var (calleeName, callSites) in callsByTarget)
            {
                // Try to find the definition of the callee
                var calleeNode = FindCallableByName(state, calleeName);
                if (calleeNode != null)
                {
                    var toItem = CreateCallHierarchyItem(state.Uri, calleeNode);
                    if (toItem != null)
                    {
                        outgoingCalls.Add(new CallHierarchyOutgoingCall
                        {
                            To = toItem,
                            FromRanges = callSites.Select(cs => cs.Range).ToArray()
                        });
                    }
                }
            }

            return outgoingCalls;
        }

        private void FindAllCallsInBody(BlockNode body, Dictionary<string, List<CallSite>> callsByTarget)
        {
            if (body == null)
                return;

            foreach (var statement in body.Statements)
            {
                FindCallsInStatement(statement, callsByTarget);
            }
        }

        private void FindCallsInStatement(StatementNode statement, Dictionary<string, List<CallSite>> callsByTarget)
        {
            switch (statement)
            {
                case ExpressionStatementNode exprStmt:
                    FindCallsInExpression(exprStmt.Expression, callsByTarget);
                    break;

                case AssignmentStatementNode assign:
                    FindCallsInExpression(assign.Target, callsByTarget);
                    FindCallsInExpression(assign.Value, callsByTarget);
                    break;

                case IfStatementNode ifStmt:
                    FindCallsInExpression(ifStmt.Condition, callsByTarget);
                    FindAllCallsInBody(ifStmt.ThenBlock, callsByTarget);
                    foreach (var (condition, block) in ifStmt.ElseIfClauses)
                    {
                        FindCallsInExpression(condition, callsByTarget);
                        FindAllCallsInBody(block, callsByTarget);
                    }
                    FindAllCallsInBody(ifStmt.ElseBlock, callsByTarget);
                    break;

                case ForLoopNode forLoop:
                    FindCallsInExpression(forLoop.Start, callsByTarget);
                    FindCallsInExpression(forLoop.End, callsByTarget);
                    FindCallsInExpression(forLoop.Step, callsByTarget);
                    FindAllCallsInBody(forLoop.Body, callsByTarget);
                    break;

                case WhileLoopNode whileLoop:
                    FindCallsInExpression(whileLoop.Condition, callsByTarget);
                    FindAllCallsInBody(whileLoop.Body, callsByTarget);
                    break;

                case DoLoopNode doLoop:
                    FindCallsInExpression(doLoop.Condition, callsByTarget);
                    FindAllCallsInBody(doLoop.Body, callsByTarget);
                    break;

                case ForEachLoopNode forEach:
                    FindCallsInExpression(forEach.Collection, callsByTarget);
                    FindAllCallsInBody(forEach.Body, callsByTarget);
                    break;

                case ReturnStatementNode returnStmt:
                    FindCallsInExpression(returnStmt.Value, callsByTarget);
                    break;

                case VariableDeclarationNode varDecl:
                    FindCallsInExpression(varDecl.Initializer, callsByTarget);
                    break;

                case BlockNode block:
                    foreach (var stmt in block.Statements)
                    {
                        FindCallsInStatement(stmt, callsByTarget);
                    }
                    break;
            }
        }

        private void FindCallsInExpression(ExpressionNode expression, Dictionary<string, List<CallSite>> callsByTarget)
        {
            if (expression == null)
                return;

            switch (expression)
            {
                case CallExpressionNode call:
                    // Extract the callee name
                    string calleeName = null;
                    if (call.Callee is IdentifierExpressionNode identifier)
                    {
                        calleeName = identifier.Name;
                    }
                    else if (call.Callee is MemberAccessExpressionNode member)
                    {
                        calleeName = member.MemberName;
                    }

                    if (!string.IsNullOrEmpty(calleeName))
                    {
                        if (!callsByTarget.ContainsKey(calleeName))
                        {
                            callsByTarget[calleeName] = new List<CallSite>();
                        }

                        callsByTarget[calleeName].Add(new CallSite
                        {
                            Range = new LspRange(
                                new Position(call.Line - 1, call.Column - 1),
                                new Position(call.Line - 1, call.Column - 1 + calleeName.Length))
                        });
                    }

                    // Also search in arguments
                    foreach (var arg in call.Arguments)
                    {
                        FindCallsInExpression(arg, callsByTarget);
                    }
                    break;

                case BinaryExpressionNode binary:
                    FindCallsInExpression(binary.Left, callsByTarget);
                    FindCallsInExpression(binary.Right, callsByTarget);
                    break;

                case UnaryExpressionNode unary:
                    FindCallsInExpression(unary.Operand, callsByTarget);
                    break;

                case MemberAccessExpressionNode member:
                    FindCallsInExpression(member.Object, callsByTarget);
                    break;

                case ArrayAccessExpressionNode arrayAccess:
                    FindCallsInExpression(arrayAccess.Array, callsByTarget);
                    foreach (var index in arrayAccess.Indices)
                    {
                        FindCallsInExpression(index, callsByTarget);
                    }
                    break;

                case NewExpressionNode newExpr:
                    foreach (var arg in newExpr.Arguments)
                    {
                        FindCallsInExpression(arg, callsByTarget);
                    }
                    break;

                case CastExpressionNode cast:
                    FindCallsInExpression(cast.Expression, callsByTarget);
                    break;
            }
        }

        private CallHierarchyItem CreateCallHierarchyItem(DocumentUri uri, ASTNode node)
        {
            switch (node)
            {
                case FunctionNode func:
                    var funcParams = string.Join(", ", func.Parameters.Select(p => $"{p.Name} As {p.Type?.Name ?? "Variant"}"));
                    return new CallHierarchyItem
                    {
                        Name = func.Name,
                        Kind = SymbolKind.Function,
                        Detail = $"({funcParams}) As {func.ReturnType?.Name ?? "Void"}",
                        Uri = uri,
                        Range = new LspRange(
                            new Position(func.Line - 1, 0),
                            new Position(func.Line + 10, 0)),
                        SelectionRange = new LspRange(
                            new Position(func.Line - 1, func.Column - 1),
                            new Position(func.Line - 1, func.Column - 1 + func.Name.Length))
                    };

                case SubroutineNode sub:
                    var subParams = string.Join(", ", sub.Parameters.Select(p => $"{p.Name} As {p.Type?.Name ?? "Variant"}"));
                    return new CallHierarchyItem
                    {
                        Name = sub.Name,
                        Kind = SymbolKind.Method,
                        Detail = $"({subParams})",
                        Uri = uri,
                        Range = new LspRange(
                            new Position(sub.Line - 1, 0),
                            new Position(sub.Line + 10, 0)),
                        SelectionRange = new LspRange(
                            new Position(sub.Line - 1, sub.Column - 1),
                            new Position(sub.Line - 1, sub.Column - 1 + sub.Name.Length))
                    };
            }

            return null;
        }

        public CallHierarchyRegistrationOptions GetRegistrationOptions(CallHierarchyCapability capability, ClientCapabilities clientCapabilities)
        {
            return new CallHierarchyRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }

        private class CallSite
        {
            public LspRange Range { get; set; }
        }
    }
}
