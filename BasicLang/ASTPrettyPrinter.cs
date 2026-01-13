using System;
using System.Text;
using BasicLang.Compiler.AST;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Visitor that pretty-prints the AST
    /// </summary>
    public class ASTPrettyPrinter : IASTVisitor
    {
        private readonly StringBuilder _output;
        private int _indent;
        
        public ASTPrettyPrinter()
        {
            _output = new StringBuilder();
            _indent = 0;
        }
        
        public string GetOutput()
        {
            return _output.ToString();
        }
        
        private void WriteLine(string text)
        {
            _output.AppendLine(new string(' ', _indent * 2) + text);
        }
        
        private void Indent()
        {
            _indent++;
        }
        
        private void Unindent()
        {
            _indent--;
        }
        
        public void Visit(ProgramNode node)
        {
            WriteLine("Program:");
            Indent();
            foreach (var declaration in node.Declarations)
            {
                declaration.Accept(this);
            }
            Unindent();
        }
        
        public void Visit(SubroutineNode node)
        {
            WriteLine($"{node.Access} Sub {node.Name}({node.Parameters.Count} parameters)");
            
            if (node.Parameters.Count > 0)
            {
                Indent();
                WriteLine("Parameters:");
                Indent();
                foreach (var param in node.Parameters)
                {
                    param.Accept(this);
                }
                Unindent();
                Unindent();
            }
            
            if (node.Body != null)
            {
                Indent();
                WriteLine("Body:");
                Indent();
                node.Body.Accept(this);
                Unindent();
                Unindent();
            }
        }
        
        public void Visit(FunctionNode node)
        {
            WriteLine($"{node.Access} Function {node.Name}({node.Parameters.Count} parameters) -> {node.ReturnType}");
            
            if (node.Parameters.Count > 0)
            {
                Indent();
                WriteLine("Parameters:");
                Indent();
                foreach (var param in node.Parameters)
                {
                    param.Accept(this);
                }
                Unindent();
                Unindent();
            }
            
            if (node.Body != null)
            {
                Indent();
                WriteLine("Body:");
                Indent();
                node.Body.Accept(this);
                Unindent();
                Unindent();
            }
        }
        
        public void Visit(ClassNode node)
        {
            var line = $"Class {node.Name}";
            
            if (node.GenericParameters.Count > 0)
            {
                line += $"<{string.Join(", ", node.GenericParameters)}>";
            }
            
            if (node.BaseClass != null)
            {
                line += $" : {node.BaseClass}";
            }
            
            if (node.Interfaces.Count > 0)
            {
                line += $", {string.Join(", ", node.Interfaces)}";
            }
            
            WriteLine(line);
            
            Indent();
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }
            Unindent();
        }
        
        public void Visit(StructureNode node)
        {
            WriteLine($"Structure {node.Name}");
            Indent();
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }
            Unindent();
        }

        public void Visit(UnionNode node)
        {
            WriteLine($"Union {node.Name}");
            Indent();
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }
            Unindent();
        }

        public void Visit(TypeNode node)
        {
            WriteLine($"Type {node.Name}");
            Indent();
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }
            Unindent();
        }
        
        public void Visit(InterfaceNode node)
        {
            WriteLine($"Interface {node.Name}");
            Indent();
            foreach (var method in node.Methods)
            {
                method.Accept(this);
            }
            Unindent();
        }

        public void Visit(EnumNode node)
        {
            var underlyingType = node.UnderlyingType != null ? $" As {node.UnderlyingType.Name}" : "";
            WriteLine($"Enum {node.Name}{underlyingType}");
            Indent();
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }
            Unindent();
        }

        public void Visit(EnumMemberNode node)
        {
            var value = node.Value != null ? " = <expr>" : "";
            WriteLine($"{node.Name}{value}");
        }

        public void Visit(ModuleNode node)
        {
            WriteLine($"Module {node.Name}");
            Indent();
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }
            Unindent();
        }
        
        public void Visit(NamespaceNode node)
        {
            WriteLine($"Namespace {node.Name}");
            Indent();
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }
            Unindent();
        }
        
        public void Visit(VariableDeclarationNode node)
        {
            var line = $"{node.Access} Var {node.Name} : {node.Type}";

            if (node.Initializer != null)
            {
                line += " = ";
                WriteLine(line);
                Indent();
                node.Initializer.Accept(this);
                Unindent();
            }
            else
            {
                WriteLine(line);
            }
        }

        public void Visit(TupleDeconstructionNode node)
        {
            var vars = string.Join(", ", node.Variables.Select(v =>
                v.Type != null ? $"{v.Name} As {v.Type}" : v.Name));
            WriteLine($"Dim ({vars}) = ");
            Indent();
            node.Initializer.Accept(this);
            Unindent();
        }

        public void Visit(ConstantDeclarationNode node)
        {
            WriteLine($"Const {node.Name} : {node.Type} = ");
            Indent();
            node.Value.Accept(this);
            Unindent();
        }
        
        public void Visit(TypeDefineNode node)
        {
            WriteLine($"TypeDefine {node.AliasName} = {node.BaseType}");
        }
        
        public void Visit(ParameterNode node)
        {
            var line = $"{node.Name} : {node.Type}";
            
            if (node.DefaultValue != null)
            {
                line += " = ";
                WriteLine(line);
                Indent();
                node.DefaultValue.Accept(this);
                Unindent();
            }
            else
            {
                WriteLine(line);
            }
        }
        
        public void Visit(BlockNode node)
        {
            WriteLine("Block:");
            Indent();
            foreach (var statement in node.Statements)
            {
                statement.Accept(this);
            }
            Unindent();
        }
        
        public void Visit(IfStatementNode node)
        {
            WriteLine("If:");
            Indent();
            WriteLine("Condition:");
            Indent();
            node.Condition.Accept(this);
            Unindent();
            
            WriteLine("Then:");
            Indent();
            node.ThenBlock.Accept(this);
            Unindent();
            
            foreach (var (condition, block) in node.ElseIfClauses)
            {
                WriteLine("ElseIf:");
                Indent();
                WriteLine("Condition:");
                Indent();
                condition.Accept(this);
                Unindent();
                
                WriteLine("Then:");
                Indent();
                block.Accept(this);
                Unindent();
                Unindent();
            }
            
            if (node.ElseBlock != null)
            {
                WriteLine("Else:");
                Indent();
                node.ElseBlock.Accept(this);
                Unindent();
            }
            
            Unindent();
        }
        
        public void Visit(SelectStatementNode node)
        {
            WriteLine("Select:");
            Indent();
            WriteLine("Expression:");
            Indent();
            node.Expression.Accept(this);
            Unindent();
            
            foreach (var caseClause in node.Cases)
            {
                caseClause.Accept(this);
            }
            Unindent();
        }
        
        public void Visit(CaseClauseNode node)
        {
            if (node.IsElse)
            {
                WriteLine("Case Else:");
            }
            else
            {
                WriteLine("Case:");
                Indent();
                foreach (var value in node.Values)
                {
                    value.Accept(this);
                }
                Unindent();
            }
            
            Indent();
            node.Body.Accept(this);
            Unindent();
        }
        
        public void Visit(ForLoopNode node)
        {
            WriteLine($"For {node.Variable} = ");
            Indent();
            
            WriteLine("Start:");
            Indent();
            node.Start.Accept(this);
            Unindent();
            
            WriteLine("End:");
            Indent();
            node.End.Accept(this);
            Unindent();
            
            if (node.Step != null)
            {
                WriteLine("Step:");
                Indent();
                node.Step.Accept(this);
                Unindent();
            }
            
            WriteLine("Body:");
            Indent();
            node.Body.Accept(this);
            Unindent();
            
            Unindent();
        }
        
        public void Visit(WhileLoopNode node)
        {
            WriteLine("While:");
            Indent();
            
            WriteLine("Condition:");
            Indent();
            node.Condition.Accept(this);
            Unindent();
            
            WriteLine("Body:");
            Indent();
            node.Body.Accept(this);
            Unindent();
            
            Unindent();
        }
        
        public void Visit(DoLoopNode node)
        {
            WriteLine($"Do{(node.IsWhile ? " While" : "")}:");
            Indent();
            
            WriteLine("Body:");
            Indent();
            node.Body.Accept(this);
            Unindent();
            
            if (node.Condition != null)
            {
                WriteLine("Condition:");
                Indent();
                node.Condition.Accept(this);
                Unindent();
            }
            
            Unindent();
        }
        
        public void Visit(ForEachLoopNode node)
        {
            WriteLine($"ForEach {node.Variable} : {node.VariableType} In:");
            Indent();

            WriteLine("Collection:");
            Indent();
            node.Collection.Accept(this);
            Unindent();

            WriteLine("Body:");
            Indent();
            node.Body.Accept(this);
            Unindent();

            Unindent();
        }

        public void Visit(WithStatementNode node)
        {
            WriteLine("With:");
            Indent();

            WriteLine("Object:");
            Indent();
            node.Object.Accept(this);
            Unindent();

            WriteLine("Body:");
            Indent();
            node.Body.Accept(this);
            Unindent();

            Unindent();
        }

        public void Visit(ImplicitWithMemberNode node)
        {
            WriteLine($"ImplicitWithMember: .{node.MemberName}");
        }

        public void Visit(TryStatementNode node)
        {
            WriteLine("Try:");
            Indent();
            
            WriteLine("Body:");
            Indent();
            node.TryBlock.Accept(this);
            Unindent();
            
            foreach (var catchClause in node.CatchClauses)
            {
                catchClause.Accept(this);
            }
            
            Unindent();
        }
        
        public void Visit(CatchClauseNode node)
        {
            WriteLine($"Catch {node.ExceptionVariable} : {node.ExceptionType}");
            Indent();
            node.Body.Accept(this);
            Unindent();
        }

        public void Visit(ThrowStatementNode node)
        {
            WriteLine("Throw:");
            if (node.Exception != null)
            {
                Indent();
                node.Exception.Accept(this);
                Unindent();
            }
        }

        public void Visit(ReturnStatementNode node)
        {
            WriteLine("Return:");
            if (node.Value != null)
            {
                Indent();
                node.Value.Accept(this);
                Unindent();
            }
        }

        public void Visit(ExitStatementNode node)
        {
            WriteLine($"Exit {node.Kind}");
        }

        public void Visit(AssignmentStatementNode node)
        {
            WriteLine($"Assignment ({node.Operator}):");
            Indent();
            
            WriteLine("Target:");
            Indent();
            node.Target.Accept(this);
            Unindent();
            
            WriteLine("Value:");
            Indent();
            node.Value.Accept(this);
            Unindent();
            
            Unindent();
        }
        
        public void Visit(ExpressionStatementNode node)
        {
            WriteLine("ExpressionStatement:");
            Indent();
            node.Expression.Accept(this);
            Unindent();
        }
        
        public void Visit(BinaryExpressionNode node)
        {
            WriteLine($"BinaryOp ({node.Operator}):");
            Indent();
            
            WriteLine("Left:");
            Indent();
            node.Left.Accept(this);
            Unindent();
            
            WriteLine("Right:");
            Indent();
            node.Right.Accept(this);
            Unindent();
            
            Unindent();
        }
        
        public void Visit(UnaryExpressionNode node)
        {
            WriteLine($"UnaryOp ({node.Operator}, {(node.IsPostfix ? "Postfix" : "Prefix")}):");
            Indent();
            node.Operand.Accept(this);
            Unindent();
        }
        
        public void Visit(LiteralExpressionNode node)
        {
            WriteLine($"Literal: {node.Value} ({node.LiteralType})");
        }

        public void Visit(InterpolatedStringNode node)
        {
            WriteLine($"InterpolatedString ({node.Parts.Count} parts):");
            Indent();
            foreach (var part in node.Parts)
            {
                if (part is string text)
                {
                    WriteLine($"Text: \"{text}\"");
                }
                else if (part is ExpressionNode expr)
                {
                    WriteLine("Expression:");
                    Indent();
                    expr.Accept(this);
                    Unindent();
                }
            }
            Unindent();
        }

        public void Visit(IdentifierExpressionNode node)
        {
            WriteLine($"Identifier: {node.Name}");
        }
        
        public void Visit(MemberAccessExpressionNode node)
        {
            WriteLine($"MemberAccess: .{node.MemberName}");
            Indent();
            WriteLine("Object:");
            Indent();
            node.Object.Accept(this);
            Unindent();
            Unindent();
        }
        
        public void Visit(CallExpressionNode node)
        {
            WriteLine($"Call ({node.Arguments.Count} arguments):");
            Indent();
            
            WriteLine("Callee:");
            Indent();
            node.Callee.Accept(this);
            Unindent();
            
            if (node.Arguments.Count > 0)
            {
                WriteLine("Arguments:");
                Indent();
                foreach (var arg in node.Arguments)
                {
                    arg.Accept(this);
                }
                Unindent();
            }
            
            Unindent();
        }
        
        public void Visit(ArrayAccessExpressionNode node)
        {
            WriteLine($"ArrayAccess ({node.Indices.Count} indices):");
            Indent();
            
            WriteLine("Array:");
            Indent();
            node.Array.Accept(this);
            Unindent();
            
            WriteLine("Indices:");
            Indent();
            foreach (var index in node.Indices)
            {
                index.Accept(this);
            }
            Unindent();
            
            Unindent();
        }
        
        public void Visit(NewExpressionNode node)
        {
            WriteLine($"New {node.Type} ({node.Arguments.Count} arguments)");
            
            if (node.Arguments.Count > 0)
            {
                Indent();
                WriteLine("Arguments:");
                Indent();
                foreach (var arg in node.Arguments)
                {
                    arg.Accept(this);
                }
                Unindent();
                Unindent();
            }
        }
        
        public void Visit(CastExpressionNode node)
        {
            WriteLine($"Cast to {node.TargetType}:");
            Indent();
            node.Expression.Accept(this);
            Unindent();
        }
        
        public void Visit(TemplateDeclarationNode node)
        {
            WriteLine($"Template <{string.Join(", ", node.TypeParameters)}>");
            Indent();
            node.Declaration.Accept(this);
            Unindent();
        }
        
        public void Visit(DelegateDeclarationNode node)
        {
            var returnType = node.ReturnType != null ? $" -> {node.ReturnType}" : "";
            WriteLine($"Delegate {node.Name}({node.Parameters.Count} parameters){returnType}");
        }
        
        public void Visit(ExtensionMethodNode node)
        {
            WriteLine($"Extension for {node.ExtendedType}:");
            Indent();
            node.Method.Accept(this);
            Unindent();
        }

        public void Visit(ExternDeclarationNode node)
        {
            var kind = node.IsFunction ? "Function" : "Sub";
            var returnType = node.ReturnType != null ? $" As {node.ReturnType}" : "";
            WriteLine($"Extern {kind} {node.Name}({node.Parameters.Count} parameters){returnType}");
            Indent();
            foreach (var kvp in node.PlatformImplementations)
            {
                WriteLine($"{kvp.Key}: \"{kvp.Value}\"");
            }
            Unindent();
        }

        public void Visit(UsingDirectiveNode node)
        {
            WriteLine($"Using {node.Namespace}");
        }
        
        public void Visit(ImportDirectiveNode node)
        {
            WriteLine($"Import {node.Module}");
        }

        public void Visit(ConstructorNode node)
        {
            var access = node.Access != AccessModifier.Public ? $"{node.Access} " : "";
            WriteLine($"{access}Sub New({node.Parameters.Count} parameters)");
            Indent();
            if (node.BaseConstructorArgs.Count > 0)
            {
                WriteLine($"MyBase.New({node.BaseConstructorArgs.Count} arguments)");
            }
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }
            Unindent();
        }

        public void Visit(PropertyNode node)
        {
            var access = node.Access != AccessModifier.Public ? $"{node.Access} " : "";
            var modifiers = "";
            if (node.IsStatic) modifiers += "Shared ";
            if (node.IsReadOnly) modifiers += "ReadOnly ";
            if (node.IsWriteOnly) modifiers += "WriteOnly ";
            var propertyType = node.PropertyType != null ? $" As {node.PropertyType}" : "";
            WriteLine($"{access}{modifiers}Property {node.Name}{propertyType}");
            Indent();
            if (node.Getter != null)
            {
                WriteLine("Get");
                Indent();
                node.Getter.Accept(this);
                Unindent();
            }
            if (node.Setter != null)
            {
                WriteLine("Set");
                Indent();
                node.Setter.Accept(this);
                Unindent();
            }
            Unindent();
        }

        public void Visit(MyBaseExpressionNode node)
        {
            WriteLine("MyBase");
        }

        public void Visit(LambdaExpressionNode node)
        {
            var kind = node.IsFunction ? "Function" : "Sub";
            WriteLine($"Lambda {kind}({node.Parameters.Count} parameters):");
            Indent();

            if (node.Parameters.Count > 0)
            {
                WriteLine("Parameters:");
                Indent();
                foreach (var param in node.Parameters)
                {
                    param.Accept(this);
                }
                Unindent();
            }

            if (node.Body != null)
            {
                WriteLine("Expression Body:");
                Indent();
                node.Body.Accept(this);
                Unindent();
            }
            else if (node.StatementBody != null)
            {
                WriteLine("Statement Body:");
                Indent();
                node.StatementBody.Accept(this);
                Unindent();
            }

            Unindent();
        }

        public void Visit(CollectionInitializerNode node)
        {
            WriteLine($"CollectionInitializer ({node.Elements.Count} elements):");
            Indent();
            foreach (var element in node.Elements)
            {
                element.Accept(this);
            }
            Unindent();
        }

        public void Visit(TupleLiteralNode node)
        {
            WriteLine($"TupleLiteral ({node.Elements.Count} elements):");
            Indent();
            for (int i = 0; i < node.Elements.Count; i++)
            {
                if (i < node.ElementNames.Count && !string.IsNullOrEmpty(node.ElementNames[i]))
                {
                    WriteLine($"{node.ElementNames[i]}:");
                    Indent();
                    node.Elements[i].Accept(this);
                    Unindent();
                }
                else
                {
                    node.Elements[i].Accept(this);
                }
            }
            Unindent();
        }

        public void Visit(OperatorDeclarationNode node)
        {
            var modifiers = new List<string>();
            if (node.IsShared) modifiers.Add("Shared");
            if (node.IsWidening) modifiers.Add("Widening");
            if (node.IsNarrowing) modifiers.Add("Narrowing");
            var modStr = modifiers.Count > 0 ? string.Join(" ", modifiers) + " " : "";

            WriteLine($"{modStr}Operator {node.OperatorSymbol}({node.Parameters.Count} params) As {node.ReturnType}:");
            Indent();

            WriteLine("Parameters:");
            Indent();
            foreach (var param in node.Parameters)
            {
                param.Accept(this);
            }
            Unindent();

            if (node.Body != null)
            {
                WriteLine("Body:");
                Indent();
                node.Body.Accept(this);
                Unindent();
            }

            Unindent();
        }

        public void Visit(EventDeclarationNode node)
        {
            WriteLine($"{node.Access} Event {node.Name} As {node.EventType}");
        }

        public void Visit(RaiseEventStatementNode node)
        {
            WriteLine($"RaiseEvent {node.EventName}({node.Arguments.Count} args):");
            Indent();
            foreach (var arg in node.Arguments)
            {
                arg.Accept(this);
            }
            Unindent();
        }

        public void Visit(AddHandlerStatementNode node)
        {
            WriteLine("AddHandler:");
            Indent();
            WriteLine("Event:");
            Indent();
            node.EventExpression?.Accept(this);
            Unindent();
            WriteLine("Handler:");
            Indent();
            node.HandlerExpression?.Accept(this);
            Unindent();
            Unindent();
        }

        public void Visit(RemoveHandlerStatementNode node)
        {
            WriteLine("RemoveHandler:");
            Indent();
            WriteLine("Event:");
            Indent();
            node.EventExpression?.Accept(this);
            Unindent();
            WriteLine("Handler:");
            Indent();
            node.HandlerExpression?.Accept(this);
            Unindent();
            Unindent();
        }

        public void Visit(TypePatternNode node)
        {
            WriteLine($"TypePattern: {node.MatchType}");
            if (!string.IsNullOrEmpty(node.VariableName))
            {
                Indent();
                WriteLine($"Binding: {node.VariableName}");
                Unindent();
            }
            if (node.WhenGuard != null)
            {
                Indent();
                WriteLine("When:");
                Indent();
                node.WhenGuard.Accept(this);
                Unindent();
                Unindent();
            }
        }

        public void Visit(ConstantPatternNode node)
        {
            WriteLine("ConstantPattern:");
            Indent();
            node.Value?.Accept(this);
            Unindent();
            if (node.WhenGuard != null)
            {
                Indent();
                WriteLine("When:");
                Indent();
                node.WhenGuard.Accept(this);
                Unindent();
                Unindent();
            }
        }

        public void Visit(RangePatternNode node)
        {
            WriteLine("RangePattern:");
            Indent();
            WriteLine("Lower:");
            Indent();
            node.LowerBound?.Accept(this);
            Unindent();
            WriteLine("Upper:");
            Indent();
            node.UpperBound?.Accept(this);
            Unindent();
            Unindent();
            if (node.WhenGuard != null)
            {
                Indent();
                WriteLine("When:");
                Indent();
                node.WhenGuard.Accept(this);
                Unindent();
                Unindent();
            }
        }

        public void Visit(ComparisonPatternNode node)
        {
            WriteLine($"ComparisonPattern: {node.Operator}");
            Indent();
            node.Value?.Accept(this);
            Unindent();
            if (node.WhenGuard != null)
            {
                Indent();
                WriteLine("When:");
                Indent();
                node.WhenGuard.Accept(this);
                Unindent();
                Unindent();
            }
        }

        public void Visit(NothingPatternNode node)
        {
            WriteLine("NothingPattern (null)");
            if (node.WhenGuard != null)
            {
                Indent();
                WriteLine("When:");
                Indent();
                node.WhenGuard.Accept(this);
                Unindent();
                Unindent();
            }
        }

        public void Visit(OrPatternNode node)
        {
            WriteLine("OrPattern:");
            Indent();
            foreach (var alt in node.Alternatives)
            {
                alt.Accept(this);
            }
            Unindent();
            if (node.WhenGuard != null)
            {
                Indent();
                WriteLine("When:");
                Indent();
                node.WhenGuard.Accept(this);
                Unindent();
                Unindent();
            }
        }

        public void Visit(TuplePatternNode node)
        {
            WriteLine("TuplePattern:");
            Indent();
            foreach (var elem in node.Elements)
            {
                elem.Accept(this);
            }
            Unindent();
            if (node.WhenGuard != null)
            {
                Indent();
                WriteLine("When:");
                Indent();
                node.WhenGuard.Accept(this);
                Unindent();
                Unindent();
            }
        }

        public void Visit(BindingPatternNode node)
        {
            WriteLine($"BindingPattern: {node.VariableName}");
            if (node.WhenGuard != null)
            {
                Indent();
                WriteLine("When:");
                Indent();
                node.WhenGuard.Accept(this);
                Unindent();
                Unindent();
            }
        }

        public void Visit(AwaitExpressionNode node)
        {
            WriteLine("Await:");
            Indent();
            node.Expression?.Accept(this);
            Unindent();
        }

        public void Visit(YieldStatementNode node)
        {
            if (node.IsBreak)
            {
                WriteLine("Yield Break");
            }
            else
            {
                WriteLine("Yield Return:");
                Indent();
                node.Value?.Accept(this);
                Unindent();
            }
        }

        public void Visit(LinqQueryExpressionNode node)
        {
            WriteLine("LINQ Query:");
            Indent();
            foreach (var clause in node.Clauses)
            {
                switch (clause)
                {
                    case FromClause from:
                        WriteLine($"From {from.VariableName} In");
                        Indent();
                        from.Collection?.Accept(this);
                        Unindent();
                        break;
                    case WhereClause where:
                        WriteLine("Where");
                        Indent();
                        where.Condition?.Accept(this);
                        Unindent();
                        break;
                    case SelectClause select:
                        WriteLine("Select");
                        Indent();
                        select.Selector?.Accept(this);
                        Unindent();
                        break;
                    case OrderByClause orderBy:
                        WriteLine($"Order By {(orderBy.Descending ? "Descending" : "Ascending")}");
                        Indent();
                        orderBy.KeySelector?.Accept(this);
                        Unindent();
                        break;
                    default:
                        WriteLine(clause.GetType().Name);
                        break;
                }
            }
            Unindent();
        }

        public void Visit(InlineCodeNode node)
        {
            WriteLine($"Inline Code ({node.Language}):");
            Indent();
            foreach (var line in node.Code.Split('\n'))
            {
                WriteLine(line.TrimEnd());
            }
            Unindent();
        }

        public void Visit(PreprocessorDefineNode node)
        {
            WriteLine($"#Define {node.Name}" + (node.Value != null ? $" = {node.Value}" : ""));
        }

        public void Visit(PreprocessorUndefineNode node)
        {
            WriteLine($"#Undefine {node.Name}");
        }

        public void Visit(PreprocessorIfNode node)
        {
            WriteLine("#If:");
            Indent();
            WriteLine("Condition:");
            Indent();
            node.Condition?.Accept(this);
            Unindent();
            WriteLine("Then:");
            Indent();
            foreach (var stmt in node.ThenBody)
            {
                stmt.Accept(this);
            }
            Unindent();

            foreach (var elseIf in node.ElseIfClauses)
            {
                WriteLine("#ElseIf:");
                Indent();
                WriteLine("Condition:");
                Indent();
                elseIf.Condition?.Accept(this);
                Unindent();
                WriteLine("Body:");
                Indent();
                foreach (var stmt in elseIf.Body)
                {
                    stmt.Accept(this);
                }
                Unindent();
                Unindent();
            }

            if (node.ElseBody.Count > 0)
            {
                WriteLine("#Else:");
                Indent();
                foreach (var stmt in node.ElseBody)
                {
                    stmt.Accept(this);
                }
                Unindent();
            }
            Unindent();
        }

        public void Visit(PreprocessorIncludeNode node)
        {
            WriteLine($"#Include \"{node.FilePath}\"");
        }

        public void Visit(PreprocessorConstNode node)
        {
            WriteLine($"#Const {node.Name}:");
            Indent();
            node.Value?.Accept(this);
            Unindent();
        }

        public void Visit(PreprocessorRegionNode node)
        {
            WriteLine($"#Region {node.Name}:");
            Indent();
            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }
            Unindent();
            WriteLine("#End Region");
        }

        public void Visit(DeclareNode node)
        {
            var declType = node.IsFunction ? "Function" : "Sub";
            var sb = new System.Text.StringBuilder();
            sb.Append($"Declare {declType} {node.Name}");

            if (node.Convention != CallingConvention.Default)
            {
                sb.Append($" {node.Convention}");
            }

            sb.Append($" Lib \"{node.LibraryName}\"");

            if (!string.IsNullOrEmpty(node.AliasName))
            {
                sb.Append($" Alias \"{node.AliasName}\"");
            }

            sb.Append(" (");

            for (int i = 0; i < node.Parameters.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var param = node.Parameters[i];
                sb.Append($"{param.Name} As {param.Type?.Name ?? "Object"}");
            }

            sb.Append(")");

            if (node.IsFunction && node.ReturnType != null)
            {
                sb.Append($" As {node.ReturnType.Name}");
            }

            WriteLine(sb.ToString());
        }
    }
}
