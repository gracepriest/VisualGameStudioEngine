namespace VisualGameStudio.Core.Abstractions.Services;

public interface IRefactoringService
{
    Task<RenameResult> RenameSymbolAsync(string filePath, int line, int column, string newName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SymbolLocation>> FindAllReferencesAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<ExtractMethodResult> ExtractMethodAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, string methodName, CancellationToken cancellationToken = default);
    Task<InlineMethodResult> InlineMethodAsync(string filePath, int line, int column, bool removeDefinition = false, CancellationToken cancellationToken = default);
    Task<MethodInfo?> GetMethodInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<IntroduceVariableResult> IntroduceVariableAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, string variableName, string? variableType = null, bool replaceAll = false, CancellationToken cancellationToken = default);
    Task<InlineVariableResult> InlineVariableAsync(string filePath, int line, int column, InlineVariableOptions options, CancellationToken cancellationToken = default);
    Task<LocalVariableInfo?> GetVariableInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<ChangeSignatureResult> ChangeSignatureAsync(string filePath, int line, int column, SignatureChange change, CancellationToken cancellationToken = default);
    Task<MethodSignatureInfo?> GetSignatureInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<EncapsulateFieldResult> EncapsulateFieldAsync(string filePath, int line, int column, EncapsulateFieldOptions options, CancellationToken cancellationToken = default);
    Task<InlineFieldResult> InlineFieldAsync(string filePath, int line, int column, InlineFieldOptions options, CancellationToken cancellationToken = default);
    Task<FieldInfo?> GetFieldInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<MoveTypeToFileResult> MoveTypeToFileAsync(string filePath, int line, int column, MoveTypeToFileOptions options, CancellationToken cancellationToken = default);
    Task<TypeDefinitionInfo?> GetTypeInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<ExtractInterfaceResult> ExtractInterfaceAsync(string filePath, int line, int column, ExtractInterfaceOptions options, CancellationToken cancellationToken = default);
    Task<ClassMemberInfo?> GetClassMembersAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<GenerateConstructorResult> GenerateConstructorAsync(string filePath, int line, int column, GenerateConstructorOptions options, CancellationToken cancellationToken = default);
    Task<ClassFieldsInfo?> GetClassFieldsAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<ImplementInterfaceResult> ImplementInterfaceAsync(string filePath, int line, int column, ImplementInterfaceOptions options, CancellationToken cancellationToken = default);
    Task<ImplementableInterfacesInfo?> GetImplementableInterfacesAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<OverrideMethodResult> OverrideMethodAsync(string filePath, int line, int column, OverrideMethodOptions options, CancellationToken cancellationToken = default);
    Task<OverridableMethodsInfo?> GetOverridableMethodsAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<AddParameterResult> AddParameterAsync(string filePath, int line, int column, AddParameterOptions options, CancellationToken cancellationToken = default);
    Task<AddParameterInfo?> GetMethodForParameterAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<RemoveParameterResult> RemoveParameterAsync(string filePath, int line, int column, RemoveParameterOptions options, CancellationToken cancellationToken = default);
    Task<ReorderParametersResult> ReorderParametersAsync(string filePath, int line, int column, ReorderParametersOptions options, CancellationToken cancellationToken = default);
    Task<RenameParameterResult> RenameParameterAsync(string filePath, int line, int column, RenameParameterOptions options, CancellationToken cancellationToken = default);
    Task<ChangeParameterTypeResult> ChangeParameterTypeAsync(string filePath, int line, int column, ChangeParameterTypeOptions options, CancellationToken cancellationToken = default);
    Task<MakeParameterOptionalResult> MakeParameterOptionalAsync(string filePath, int line, int column, MakeParameterOptionalOptions options, CancellationToken cancellationToken = default);
    Task<MakeParameterRequiredResult> MakeParameterRequiredAsync(string filePath, int line, int column, MakeParameterRequiredOptions options, CancellationToken cancellationToken = default);
    Task<ConvertToNamedArgumentsResult> ConvertToNamedArgumentsAsync(string filePath, int line, int column, ConvertToNamedArgumentsOptions options, CancellationToken cancellationToken = default);
    Task<ConvertToPositionalArgumentsResult> ConvertToPositionalArgumentsAsync(string filePath, int line, int column, ConvertToPositionalArgumentsOptions options, CancellationToken cancellationToken = default);
    Task<CallSiteInfo?> GetCallSiteInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<ExtractConstantResult> ExtractConstantAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, ExtractConstantOptions options, CancellationToken cancellationToken = default);
    Task<LiteralInfo?> GetLiteralInfoAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken cancellationToken = default);
    Task<InlineConstantResult> InlineConstantAsync(string filePath, int line, int column, InlineConstantOptions options, CancellationToken cancellationToken = default);
    Task<ConstantInfo?> GetConstantInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<SafeDeleteResult> SafeDeleteAsync(string filePath, int line, int column, SafeDeleteOptions options, CancellationToken cancellationToken = default);
    Task<DeletableSymbolInfo?> GetDeletableSymbolInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<PullMembersUpResult> PullMembersUpAsync(string filePath, int line, int column, PullMembersUpOptions options, CancellationToken cancellationToken = default);
    Task<PullMembersUpInfo?> GetPullMembersUpInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<PushMembersDownResult> PushMembersDownAsync(string filePath, int line, int column, PushMembersDownOptions options, CancellationToken cancellationToken = default);
    Task<PushMembersDownInfo?> GetPushMembersDownInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<UseBaseTypeResult> UseBaseTypeAsync(string filePath, int line, int column, UseBaseTypeOptions options, CancellationToken cancellationToken = default);
    Task<UseBaseTypeInfo?> GetUseBaseTypeInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<ConvertToInterfaceResult> ConvertToInterfaceAsync(string filePath, int line, int column, ConvertToInterfaceOptions options, CancellationToken cancellationToken = default);
    Task<ConvertToInterfaceInfo?> GetConvertToInterfaceInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<InvertIfResult> InvertIfAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<InvertIfInfo?> GetInvertIfInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<ConvertToSelectCaseResult> ConvertToSelectCaseAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<ConvertToSelectCaseInfo?> GetConvertToSelectCaseInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<SplitDeclarationResult> SplitDeclarationAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<SplitDeclarationInfo?> GetSplitDeclarationInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<IntroduceFieldResult> IntroduceFieldAsync(string filePath, int line, int column, IntroduceFieldOptions options, CancellationToken cancellationToken = default);
    Task<IntroduceFieldInfo?> GetIntroduceFieldInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    Task<TextEdit[]> ApplyCodeActionAsync(CodeAction action, CancellationToken cancellationToken = default);

    // Surround With
    Task<SurroundWithResult> SurroundWithAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, SurroundWithType surroundType, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SurroundWithOption>> GetSurroundWithOptionsAsync(string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken cancellationToken = default);

    // Go To Definition
    Task<DefinitionResult> GoToDefinitionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    // Peek Definition
    Task<PeekDefinitionResult> PeekDefinitionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
}

public class RenameResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
}

public class FileEdit
{
    public string FilePath { get; set; } = "";
    public IReadOnlyList<TextEdit> Edits { get; set; } = Array.Empty<TextEdit>();
}

public class TextEdit
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string NewText { get; set; } = "";
}

public class SymbolLocation
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string Text { get; set; } = "";
    public SymbolLocationType Type { get; set; }
}

public enum SymbolLocationType
{
    Definition,
    Reference,
    Implementation
}

public class ExtractMethodResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FileEdit? FileEdit { get; set; }
}

public class InlineMethodResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int CallSitesInlined { get; set; }
    public bool DefinitionRemoved { get; set; }
}

public class MethodInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int DefinitionLine { get; set; }
    public int DefinitionEndLine { get; set; }
    public string Body { get; set; } = "";
    public string[] Parameters { get; set; } = Array.Empty<string>();
    public bool IsFunction { get; set; }
    public string? ReturnType { get; set; }
    public int CallSiteCount { get; set; }
    public IReadOnlyList<SymbolLocation> CallSites { get; set; } = Array.Empty<SymbolLocation>();
}

public class IntroduceVariableResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FileEdit? FileEdit { get; set; }
    public int OccurrencesReplaced { get; set; }
    public string VariableName { get; set; } = "";
    public string? InferredType { get; set; }
}

public class LocalVariableInfo
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string InitializerExpression { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int DeclarationLine { get; set; }
    public int DeclarationColumn { get; set; }
    public int DeclarationEndColumn { get; set; }
    public string DeclarationText { get; set; } = "";
    public List<SymbolLocation> Usages { get; set; } = new();
    public int UsageCount => Usages.Count;
    public string? ContainingMethod { get; set; }
    public bool IsParameter { get; set; }
    public bool IsField { get; set; }
    public bool HasInitializer { get; set; }
    public bool IsReassigned { get; set; }
}

public class InlineVariableOptions
{
    /// <summary>
    /// If true, removes the variable declaration after inlining.
    /// </summary>
    public bool RemoveDeclaration { get; set; } = true;

    /// <summary>
    /// If true, adds parentheses around the inlined expression when needed.
    /// </summary>
    public bool AddParenthesesIfNeeded { get; set; } = true;
}

public class InlineVariableResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FileEdit? FileEdit { get; set; }
    public int UsagesReplaced { get; set; }
    public bool DeclarationRemoved { get; set; }
    public string VariableName { get; set; } = "";
    public string InlinedExpression { get; set; } = "";
}

public class MethodSignatureInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int DefinitionLine { get; set; }
    public int DefinitionEndLine { get; set; }
    public bool IsFunction { get; set; }
    public string? ReturnType { get; set; }
    public List<MethodParameterInfo> Parameters { get; set; } = new();
    public int CallSiteCount { get; set; }
    public IReadOnlyList<SymbolLocation> CallSites { get; set; } = Array.Empty<SymbolLocation>();
}

public class MethodParameterInfo
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public bool IsByRef { get; set; }
    public bool IsOptional { get; set; }
    public string? DefaultValue { get; set; }
    public int OriginalIndex { get; set; }
}

public class SignatureChange
{
    public string? NewName { get; set; }
    public string? NewReturnType { get; set; }
    public List<ParameterChange> Parameters { get; set; } = new();
}

public class ParameterChange
{
    public ParameterChangeKind Kind { get; set; }
    public int OriginalIndex { get; set; } = -1;
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public bool IsByRef { get; set; }
    public bool IsOptional { get; set; }
    public string? DefaultValue { get; set; }
    public int NewIndex { get; set; }
}

public enum ParameterChangeKind
{
    Keep,
    Modify,
    Add,
    Remove
}

public class ChangeSignatureResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int CallSitesUpdated { get; set; }
}

public class FieldInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int DefinitionLine { get; set; }
    public string? Type { get; set; }
    public string? InitialValue { get; set; }
    public FieldAccessibility Accessibility { get; set; }
    public bool IsShared { get; set; }
    public bool IsReadOnly { get; set; }
    public int ReferenceCount { get; set; }
    public IReadOnlyList<SymbolLocation> References { get; set; } = Array.Empty<SymbolLocation>();
}

public enum FieldAccessibility
{
    Public,
    Private,
    Protected,
    Friend
}

public class EncapsulateFieldOptions
{
    public string PropertyName { get; set; } = "";
    public string FieldName { get; set; } = "";
    public bool GenerateGetter { get; set; } = true;
    public bool GenerateSetter { get; set; } = true;
    public FieldAccessibility PropertyAccessibility { get; set; } = FieldAccessibility.Public;
    public FieldAccessibility FieldAccessibility { get; set; } = FieldAccessibility.Private;
    public bool UpdateReferences { get; set; } = true;
}

public class EncapsulateFieldResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int ReferencesUpdated { get; set; }
    public string PropertyName { get; set; } = "";
    public string FieldName { get; set; } = "";
}

public class InlineFieldOptions
{
    /// <summary>
    /// If true, removes the field declaration after inlining.
    /// </summary>
    public bool RemoveDeclaration { get; set; } = true;

    /// <summary>
    /// If true, adds parentheses around the inlined expression when needed.
    /// </summary>
    public bool AddParenthesesIfNeeded { get; set; } = true;

    /// <summary>
    /// If true, inlines the field across all files in the project.
    /// </summary>
    public bool InlineAcrossFiles { get; set; } = true;
}

public class InlineFieldResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int UsagesReplaced { get; set; }
    public bool DeclarationRemoved { get; set; }
    public string FieldName { get; set; } = "";
    public string InlinedExpression { get; set; } = "";
}

public class CodeAction
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public CodeActionKind Kind { get; set; }
    public object? Data { get; set; }
}

public enum CodeActionKind
{
    QuickFix,
    Refactor,
    RefactorExtract,
    RefactorInline,
    RefactorRewrite,
    Source,
    SourceOrganizeImports,
    SourceFixAll
}

public class TypeDefinitionInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public TypeDefinitionKind Kind { get; set; }
    public string? Namespace { get; set; }
    public TypeAccessibility Accessibility { get; set; }
    public bool IsPartial { get; set; }
    public string FullDefinition { get; set; } = "";
    public List<string> Imports { get; set; } = new();
    public string SuggestedFileName { get; set; } = "";
}

public enum TypeDefinitionKind
{
    Class,
    Module,
    Interface,
    Enum,
    Structure
}

public enum TypeAccessibility
{
    Public,
    Private,
    Protected,
    Friend,
    NotSpecified
}

public class MoveTypeToFileOptions
{
    public string NewFileName { get; set; } = "";
    public string? TargetDirectory { get; set; }
    public bool IncludeImports { get; set; } = true;
    public bool RemoveFromOriginalFile { get; set; } = true;
    public bool AddToProject { get; set; } = true;
}

public class MoveTypeToFileResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string NewFilePath { get; set; } = "";
    public FileEdit? OriginalFileEdit { get; set; }
    public string NewFileContent { get; set; } = "";
}

public class ClassMemberInfo
{
    public string ClassName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? Namespace { get; set; }
    public TypeAccessibility Accessibility { get; set; }
    public List<ExtractableMember> Members { get; set; } = new();
    public List<string> ExistingInterfaces { get; set; } = new();
}

public class ExtractableMember
{
    public string Name { get; set; } = "";
    public ExtractableMemberKind Kind { get; set; }
    public string Signature { get; set; } = "";
    public string? ReturnType { get; set; }
    public List<string> Parameters { get; set; } = new();
    public MemberAccessibility Accessibility { get; set; }
    public bool IsShared { get; set; }
    public bool IsOverridable { get; set; }
    public int Line { get; set; }
    public bool IsSelected { get; set; } = true;
}

public enum ExtractableMemberKind
{
    Sub,
    Function,
    Property,
    Event
}

public enum MemberAccessibility
{
    Public,
    Private,
    Protected,
    Friend
}

public class ExtractInterfaceOptions
{
    public string InterfaceName { get; set; } = "";
    public string? FileName { get; set; }
    public bool CreateInSameFile { get; set; } = false;
    public bool ImplementInterface { get; set; } = true;
    public List<string> SelectedMembers { get; set; } = new();
    public InterfaceAccessibility Accessibility { get; set; } = InterfaceAccessibility.Public;
}

public enum InterfaceAccessibility
{
    Public,
    Friend
}

public class ExtractInterfaceResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string InterfaceName { get; set; } = "";
    public string? NewFilePath { get; set; }
    public string? NewFileContent { get; set; }
    public FileEdit? OriginalFileEdit { get; set; }
    public int MembersExtracted { get; set; }
}

public class ClassFieldsInfo
{
    public string ClassName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int ClassStartLine { get; set; }
    public int ClassEndLine { get; set; }
    public string? Namespace { get; set; }
    public List<ConstructorFieldInfo> Fields { get; set; } = new();
    public List<ConstructorFieldInfo> Properties { get; set; } = new();
    public List<ExistingConstructorInfo> ExistingConstructors { get; set; } = new();
    public int InsertionLine { get; set; }
}

public class ConstructorFieldInfo
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsShared { get; set; }
    public FieldAccessibility Accessibility { get; set; }
    public int Line { get; set; }
    public bool HasInitializer { get; set; }
    public string? ParameterName { get; set; }
    public bool IsSelected { get; set; } = true;
}

public class ExistingConstructorInfo
{
    public int Line { get; set; }
    public List<string> ParameterTypes { get; set; } = new();
    public string Signature { get; set; } = "";
}

public class GenerateConstructorOptions
{
    public List<string> SelectedFields { get; set; } = new();
    public List<string> SelectedProperties { get; set; } = new();
    public bool GenerateNullChecks { get; set; } = false;
    public bool CallBaseConstructor { get; set; } = false;
    public ConstructorAccessibility Accessibility { get; set; } = ConstructorAccessibility.Public;
}

public enum ConstructorAccessibility
{
    Public,
    Private,
    Protected,
    Friend
}

public class GenerateConstructorResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FileEdit? FileEdit { get; set; }
    public int ParameterCount { get; set; }
    public string GeneratedCode { get; set; } = "";
}

public class ImplementableInterfacesInfo
{
    public string ClassName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int ClassStartLine { get; set; }
    public int ClassEndLine { get; set; }
    public string? Namespace { get; set; }
    public List<ImplementableInterface> Interfaces { get; set; } = new();
    public List<string> ExistingMembers { get; set; } = new();
    public int InsertionLine { get; set; }
}

public class ImplementableInterface
{
    public string Name { get; set; } = "";
    public string? FilePath { get; set; }
    public int Line { get; set; }
    public List<InterfaceMemberInfo> Members { get; set; } = new();
    public bool IsFullyImplemented { get; set; }
    public int UnimplementedCount { get; set; }
}

public class InterfaceMemberInfo
{
    public string Name { get; set; } = "";
    public InterfaceMemberKind Kind { get; set; }
    public string Signature { get; set; } = "";
    public string? ReturnType { get; set; }
    public List<InterfaceParameterInfo> Parameters { get; set; } = new();
    public bool HasGetter { get; set; }
    public bool HasSetter { get; set; }
    public bool IsImplemented { get; set; }
    public bool IsSelected { get; set; } = true;
}

public class InterfaceParameterInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsByRef { get; set; }
    public bool IsOptional { get; set; }
    public string? DefaultValue { get; set; }
}

public enum InterfaceMemberKind
{
    Sub,
    Function,
    Property,
    Event
}

public class ImplementInterfaceOptions
{
    public string InterfaceName { get; set; } = "";
    public List<string> SelectedMembers { get; set; } = new();
    public bool GenerateExplicitImplementation { get; set; } = false;
    public bool ThrowNotImplementedException { get; set; } = true;
    public bool InsertRegion { get; set; } = true;
}

public class ImplementInterfaceResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FileEdit? FileEdit { get; set; }
    public int MembersImplemented { get; set; }
    public string GeneratedCode { get; set; } = "";
}

public class OverridableMethodsInfo
{
    public string ClassName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int ClassStartLine { get; set; }
    public int ClassEndLine { get; set; }
    public string? BaseClassName { get; set; }
    public string? BaseClassFilePath { get; set; }
    public List<OverridableMethod> Methods { get; set; } = new();
    public List<string> ExistingOverrides { get; set; } = new();
    public int InsertionLine { get; set; }
}

public class OverridableMethod
{
    public string Name { get; set; } = "";
    public OverridableMethodKind Kind { get; set; }
    public string Signature { get; set; } = "";
    public string? ReturnType { get; set; }
    public List<OverridableParameterInfo> Parameters { get; set; } = new();
    public string? DeclaringClass { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsOverridden { get; set; }
    public bool IsSelected { get; set; } = true;
    public int SourceLine { get; set; }
}

public class OverridableParameterInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsByRef { get; set; }
    public bool IsOptional { get; set; }
    public string? DefaultValue { get; set; }
}

public enum OverridableMethodKind
{
    Sub,
    Function,
    Property
}

public class OverrideMethodOptions
{
    public List<string> SelectedMethods { get; set; } = new();
    public bool CallBaseMethod { get; set; } = true;
    public bool InsertRegion { get; set; } = true;
}

public class OverrideMethodResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FileEdit? FileEdit { get; set; }
    public int MethodsOverridden { get; set; }
    public string GeneratedCode { get; set; } = "";
}

public class AddParameterInfo
{
    public string MethodName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int DefinitionLine { get; set; }
    public int DefinitionEndLine { get; set; }
    public bool IsFunction { get; set; }
    public string? ReturnType { get; set; }
    public List<ExistingParameterInfo> ExistingParameters { get; set; } = new();
    public int CallSiteCount { get; set; }
    public IReadOnlyList<SymbolLocation> CallSites { get; set; } = Array.Empty<SymbolLocation>();
    public string? ContainingType { get; set; }
    public string Signature { get; set; } = "";
}

public class ExistingParameterInfo
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public bool IsByRef { get; set; }
    public bool IsOptional { get; set; }
    public string? DefaultValue { get; set; }
    public int Index { get; set; }
}

public class AddParameterOptions
{
    public string ParameterName { get; set; } = "";
    public string ParameterType { get; set; } = "Object";
    public bool IsByRef { get; set; } = false;
    public bool IsOptional { get; set; } = false;
    public string? DefaultValue { get; set; }
    public int InsertPosition { get; set; } = -1; // -1 means at end
    public string? CallSiteValue { get; set; } // Value to use at call sites
    public bool UpdateCallSites { get; set; } = true;
}

public class AddParameterResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int CallSitesUpdated { get; set; }
    public string NewSignature { get; set; } = "";
}

public class RemoveParameterOptions
{
    public List<int> ParameterIndices { get; set; } = new();
    public bool UpdateCallSites { get; set; } = true;
}

public class RemoveParameterResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int CallSitesUpdated { get; set; }
    public int ParametersRemoved { get; set; }
    public string NewSignature { get; set; } = "";
}

public class ReorderParametersOptions
{
    /// <summary>
    /// The new order of parameter indices. For example, [2, 0, 1] means
    /// the third parameter becomes first, first becomes second, second becomes third.
    /// </summary>
    public List<int> NewOrder { get; set; } = new();
    public bool UpdateCallSites { get; set; } = true;
}

public class ReorderParametersResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int CallSitesUpdated { get; set; }
    public string NewSignature { get; set; } = "";
}

public class RenameParameterOptions
{
    /// <summary>
    /// The index of the parameter to rename (0-based).
    /// </summary>
    public int ParameterIndex { get; set; }

    /// <summary>
    /// The new name for the parameter.
    /// </summary>
    public string NewName { get; set; } = "";
}

public class RenameParameterResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int ReferencesUpdated { get; set; }
    public string NewSignature { get; set; } = "";
}

public class ChangeParameterTypeOptions
{
    /// <summary>
    /// The index of the parameter to change (0-based).
    /// </summary>
    public int ParameterIndex { get; set; }

    /// <summary>
    /// The new type for the parameter.
    /// </summary>
    public string NewType { get; set; } = "";
}

public class ChangeParameterTypeResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public string NewSignature { get; set; } = "";
}

public class MakeParameterOptionalOptions
{
    /// <summary>
    /// The index of the parameter to make optional (0-based).
    /// </summary>
    public int ParameterIndex { get; set; }

    /// <summary>
    /// The default value for the parameter.
    /// </summary>
    public string DefaultValue { get; set; } = "";

    /// <summary>
    /// Whether to remove the argument from call sites that pass the default value.
    /// </summary>
    public bool RemoveDefaultArgumentsFromCallSites { get; set; } = false;
}

public class MakeParameterOptionalResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int CallSitesUpdated { get; set; }
    public string NewSignature { get; set; } = "";
}

public class MakeParameterRequiredOptions
{
    /// <summary>
    /// The index of the parameter to make required (0-based).
    /// </summary>
    public int ParameterIndex { get; set; }

    /// <summary>
    /// The value to insert at call sites that omit this argument.
    /// </summary>
    public string CallSiteValue { get; set; } = "";
}

public class MakeParameterRequiredResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int CallSitesUpdated { get; set; }
    public string NewSignature { get; set; } = "";
}

public class CallSiteInfo
{
    public string MethodName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string OriginalCall { get; set; } = "";
    public List<CallSiteArgumentInfo> Arguments { get; set; } = new();
    public bool HasNamedArguments { get; set; }
    public string? ContainingMethod { get; set; }
}

public class CallSiteArgumentInfo
{
    public int Index { get; set; }
    public string ParameterName { get; set; } = "";
    public string? ParameterType { get; set; }
    public string Value { get; set; } = "";
    public bool IsNamed { get; set; }
    public bool IsSelected { get; set; } = true;
}

public class ConvertToNamedArgumentsOptions
{
    /// <summary>
    /// Indices of arguments to convert to named. If empty, converts all.
    /// </summary>
    public List<int> ArgumentIndices { get; set; } = new();

    /// <summary>
    /// If true, converts all arguments. If false, only converts specified indices.
    /// </summary>
    public bool ConvertAll { get; set; } = true;
}

public class ConvertToNamedArgumentsResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FileEdit? FileEdit { get; set; }
    public string NewCallSite { get; set; } = "";
    public int ArgumentsConverted { get; set; }
}

public class ConvertToPositionalArgumentsOptions
{
    /// <summary>
    /// Indices of named arguments to convert to positional. If empty, converts all.
    /// </summary>
    public List<int> ArgumentIndices { get; set; } = new();

    /// <summary>
    /// If true, converts all named arguments. If false, only converts specified indices.
    /// </summary>
    public bool ConvertAll { get; set; } = true;
}

public class ConvertToPositionalArgumentsResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FileEdit? FileEdit { get; set; }
    public string NewCallSite { get; set; } = "";
    public int ArgumentsConverted { get; set; }
}

public class LiteralInfo
{
    public string Value { get; set; } = "";
    public LiteralType Type { get; set; }
    public string FilePath { get; set; } = "";
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string? ContainingType { get; set; }
    public string? ContainingMethod { get; set; }
    public int OccurrenceCount { get; set; }
    public List<SymbolLocation> Occurrences { get; set; } = new();
    public string SuggestedName { get; set; } = "";
    public string InferredType { get; set; } = "";
}

public enum LiteralType
{
    Integer,
    Long,
    Single,
    Double,
    Decimal,
    String,
    Char,
    Boolean,
    Nothing,
    Date
}

public class ExtractConstantOptions
{
    /// <summary>
    /// The name for the new constant.
    /// </summary>
    public string ConstantName { get; set; } = "";

    /// <summary>
    /// The type for the constant. If null, type is inferred.
    /// </summary>
    public string? ConstantType { get; set; }

    /// <summary>
    /// Accessibility of the constant (Public, Private, etc.)
    /// </summary>
    public ConstantAccessibility Accessibility { get; set; } = ConstantAccessibility.Private;

    /// <summary>
    /// If true, replaces all occurrences of the same literal value.
    /// </summary>
    public bool ReplaceAllOccurrences { get; set; } = true;

    /// <summary>
    /// If true, creates a Shared constant (class-level). If false, creates a local Const.
    /// </summary>
    public bool CreateAsShared { get; set; } = true;
}

public enum ConstantAccessibility
{
    Public,
    Private,
    Protected,
    Friend
}

public class ExtractConstantResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FileEdit? FileEdit { get; set; }
    public string ConstantName { get; set; } = "";
    public string ConstantValue { get; set; } = "";
    public string ConstantType { get; set; } = "";
    public int OccurrencesReplaced { get; set; }
}

public class ConstantInfo
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Type { get; set; }
    public string FilePath { get; set; } = "";
    public int DefinitionLine { get; set; }
    public int DefinitionColumn { get; set; }
    public string Accessibility { get; set; } = "Private";
    public bool IsShared { get; set; }
    public string? ContainingType { get; set; }
    public int ReferenceCount { get; set; }
    public List<SymbolLocation> References { get; set; } = new();
}

public class InlineConstantOptions
{
    /// <summary>
    /// If true, removes the constant declaration after inlining.
    /// </summary>
    public bool RemoveDeclaration { get; set; } = true;

    /// <summary>
    /// If true, inlines all references. If false, only inlines the selected reference.
    /// </summary>
    public bool InlineAllReferences { get; set; } = true;
}

public class InlineConstantResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FileEdit? FileEdit { get; set; }
    public string ConstantName { get; set; } = "";
    public string InlinedValue { get; set; } = "";
    public int ReferencesInlined { get; set; }
    public bool DeclarationRemoved { get; set; }
}

public class DeletableSymbolInfo
{
    public string Name { get; set; } = "";
    public DeletableSymbolKind Kind { get; set; }
    public string FilePath { get; set; } = "";
    public int DefinitionLine { get; set; }
    public int DefinitionEndLine { get; set; }
    public int DefinitionColumn { get; set; }
    public int DefinitionEndColumn { get; set; }
    public string Accessibility { get; set; } = "Private";
    public string? Type { get; set; }
    public string? ContainingType { get; set; }
    public string? ContainingMethod { get; set; }
    public string DeclarationText { get; set; } = "";
    public int UsageCount { get; set; }
    public List<SymbolUsage> Usages { get; set; } = new();
    public bool CanSafelyDelete => UsageCount == 0;
    public string WarningMessage { get; set; } = "";
}

public enum DeletableSymbolKind
{
    LocalVariable,
    Field,
    Constant,
    Property,
    Sub,
    Function,
    Class,
    Module,
    Interface,
    Enum,
    Structure,
    Parameter,
    Event,
    Delegate
}

public class SymbolUsage
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string ContextLine { get; set; } = "";
    public SymbolUsageKind Kind { get; set; }
    public string? ContainingMethod { get; set; }
    public string? ContainingType { get; set; }
}

public enum SymbolUsageKind
{
    Reference,
    Assignment,
    Call,
    Inheritance,
    Implementation,
    TypeReference,
    Parameter
}

public class SafeDeleteOptions
{
    /// <summary>
    /// If true, deletes the symbol even if there are usages (unsafe).
    /// </summary>
    public bool ForceDelete { get; set; } = false;

    /// <summary>
    /// If true, also deletes related symbols (e.g., property backing field).
    /// </summary>
    public bool DeleteRelatedSymbols { get; set; } = true;

    /// <summary>
    /// If true, comments out usages instead of leaving them as errors.
    /// </summary>
    public bool CommentOutUsages { get; set; } = false;

    /// <summary>
    /// If true, searches for usages in all project files.
    /// </summary>
    public bool SearchInAllFiles { get; set; } = true;
}

public class SafeDeleteResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public string SymbolName { get; set; } = "";
    public DeletableSymbolKind SymbolKind { get; set; }
    public int UsagesRemaining { get; set; }
    public int UsagesCommentedOut { get; set; }
    public bool WasForced { get; set; }
    public List<string> DeletedSymbols { get; set; } = new();
}

public class PullMembersUpInfo
{
    /// <summary>
    /// The source class/type containing the members to pull up
    /// </summary>
    public string SourceTypeName { get; set; } = "";

    /// <summary>
    /// Full declaration of the source type
    /// </summary>
    public string SourceTypeDeclaration { get; set; } = "";

    /// <summary>
    /// File path of the source type
    /// </summary>
    public string SourceFilePath { get; set; } = "";

    /// <summary>
    /// Line number where source type is defined
    /// </summary>
    public int SourceTypeLine { get; set; }

    /// <summary>
    /// Available destinations (base classes and implemented interfaces)
    /// </summary>
    public List<PullMembersUpDestination> Destinations { get; set; } = new();

    /// <summary>
    /// Members that can be pulled up
    /// </summary>
    public List<PullableMember> Members { get; set; } = new();
}

public class PullMembersUpDestination
{
    public string Name { get; set; } = "";
    public PullDestinationKind Kind { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int EndLine { get; set; }
    public bool IsInSameFile { get; set; }
    public string Declaration { get; set; } = "";
}

public enum PullDestinationKind
{
    BaseClass,
    Interface
}

public class PullableMember
{
    public string Name { get; set; } = "";
    public PullableMemberKind Kind { get; set; }
    public string Accessibility { get; set; } = "";
    public string? ReturnType { get; set; }
    public string Signature { get; set; } = "";
    public string FullDeclaration { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOverride { get; set; }
    public bool IsShared { get; set; }
    public List<string> Parameters { get; set; } = new();

    /// <summary>
    /// Whether this member already exists in the destination (for interfaces)
    /// </summary>
    public bool ExistsInDestination { get; set; }
}

public enum PullableMemberKind
{
    Sub,
    Function,
    Property,
    Field,
    Constant,
    Event
}

public class PullMembersUpOptions
{
    /// <summary>
    /// The destination to pull members to (base class or interface name)
    /// </summary>
    public string DestinationName { get; set; } = "";

    /// <summary>
    /// The kind of destination
    /// </summary>
    public PullDestinationKind DestinationKind { get; set; }

    /// <summary>
    /// Names of members to pull up
    /// </summary>
    public List<string> MemberNames { get; set; } = new();

    /// <summary>
    /// If true, make pulled methods abstract in base class (only for classes)
    /// </summary>
    public bool MakeAbstract { get; set; }

    /// <summary>
    /// If true, keep the implementation in the derived class and add Overrides
    /// </summary>
    public bool KeepImplementation { get; set; } = true;

    /// <summary>
    /// If pulling to interface, whether to add signature only
    /// </summary>
    public bool AddSignatureOnly { get; set; } = true;
}

public class PullMembersUpResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public string DestinationName { get; set; } = "";
    public PullDestinationKind DestinationKind { get; set; }
    public List<string> PulledMembers { get; set; } = new();
    public int MembersPulled { get; set; }
}

public class PushMembersDownInfo
{
    /// <summary>
    /// The source base class/type containing the members to push down
    /// </summary>
    public string SourceTypeName { get; set; } = "";

    /// <summary>
    /// Full declaration of the source type
    /// </summary>
    public string SourceTypeDeclaration { get; set; } = "";

    /// <summary>
    /// File path of the source type
    /// </summary>
    public string SourceFilePath { get; set; } = "";

    /// <summary>
    /// Line number where source type is defined
    /// </summary>
    public int SourceTypeLine { get; set; }

    /// <summary>
    /// Available destinations (derived classes)
    /// </summary>
    public List<PushMembersDownDestination> Destinations { get; set; } = new();

    /// <summary>
    /// Members that can be pushed down
    /// </summary>
    public List<PushableMember> Members { get; set; } = new();
}

public class PushMembersDownDestination
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int EndLine { get; set; }
    public bool IsInSameFile { get; set; }
    public string Declaration { get; set; } = "";

    /// <summary>
    /// Whether this derived class already has its own implementation of certain members
    /// </summary>
    public List<string> ExistingOverrides { get; set; } = new();
}

public class PushableMember
{
    public string Name { get; set; } = "";
    public PushableMemberKind Kind { get; set; }
    public string Accessibility { get; set; } = "";
    public string? ReturnType { get; set; }
    public string Signature { get; set; } = "";
    public string FullDeclaration { get; set; } = "";
    public string Body { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOverridable { get; set; }
    public bool IsShared { get; set; }
    public List<string> Parameters { get; set; } = new();
}

public enum PushableMemberKind
{
    Sub,
    Function,
    Property,
    Field,
    Constant,
    Event
}

public class PushMembersDownOptions
{
    /// <summary>
    /// Names of derived classes to push members to (empty = all derived classes)
    /// </summary>
    public List<string> DestinationNames { get; set; } = new();

    /// <summary>
    /// Names of members to push down
    /// </summary>
    public List<string> MemberNames { get; set; } = new();

    /// <summary>
    /// If true, remove the member from the base class after pushing
    /// </summary>
    public bool RemoveFromBase { get; set; } = true;

    /// <summary>
    /// If true, make the member abstract in base class instead of removing
    /// </summary>
    public bool MakeAbstractInBase { get; set; }

    /// <summary>
    /// If true, mark pushed members as Overrides in derived classes
    /// </summary>
    public bool MarkAsOverrides { get; set; }
}

public class PushMembersDownResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public List<string> DestinationNames { get; set; } = new();
    public List<string> PushedMembers { get; set; } = new();
    public int MembersPushed { get; set; }
    public int DestinationsUpdated { get; set; }
}

public class UseBaseTypeInfo
{
    /// <summary>
    /// The symbol (variable, parameter, field) at cursor position
    /// </summary>
    public string SymbolName { get; set; } = "";

    /// <summary>
    /// The current type of the symbol
    /// </summary>
    public string CurrentType { get; set; } = "";

    /// <summary>
    /// Kind of symbol (Variable, Parameter, Field, Property, ReturnType)
    /// </summary>
    public UseBaseTypeSymbolKind SymbolKind { get; set; }

    /// <summary>
    /// The declaration line containing the symbol
    /// </summary>
    public string Declaration { get; set; } = "";

    /// <summary>
    /// Line number of the declaration
    /// </summary>
    public int DeclarationLine { get; set; }

    /// <summary>
    /// Available base types to change to
    /// </summary>
    public List<BaseTypeCandidate> BaseTypes { get; set; } = new();

    /// <summary>
    /// File path containing the symbol
    /// </summary>
    public string FilePath { get; set; } = "";
}

public enum UseBaseTypeSymbolKind
{
    Variable,
    Parameter,
    Field,
    Property,
    ReturnType
}

public class BaseTypeCandidate
{
    /// <summary>
    /// Name of the base type
    /// </summary>
    public string TypeName { get; set; } = "";

    /// <summary>
    /// Whether this is an interface
    /// </summary>
    public bool IsInterface { get; set; }

    /// <summary>
    /// Whether this is a base class
    /// </summary>
    public bool IsBaseClass { get; set; }

    /// <summary>
    /// Description of why this type might be suitable
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Members that would still be accessible after the change
    /// </summary>
    public List<string> AccessibleMembers { get; set; } = new();

    /// <summary>
    /// Members that would be lost after the change
    /// </summary>
    public List<string> LostMembers { get; set; } = new();
}

public class UseBaseTypeOptions
{
    /// <summary>
    /// The base type to change to
    /// </summary>
    public string NewTypeName { get; set; } = "";

    /// <summary>
    /// Whether to update all occurrences in the file
    /// </summary>
    public bool UpdateAllOccurrences { get; set; } = true;
}

public class UseBaseTypeResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public string OriginalType { get; set; } = "";
    public string NewType { get; set; } = "";
    public int OccurrencesUpdated { get; set; }
}

public class ConvertToInterfaceInfo
{
    /// <summary>
    /// Name of the class to convert
    /// </summary>
    public string ClassName { get; set; } = "";

    /// <summary>
    /// Full class declaration line
    /// </summary>
    public string ClassDeclaration { get; set; } = "";

    /// <summary>
    /// File path containing the class
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Line number where the class is defined
    /// </summary>
    public int ClassLine { get; set; }

    /// <summary>
    /// End line of the class
    /// </summary>
    public int ClassEndLine { get; set; }

    /// <summary>
    /// Suggested interface name (I + ClassName)
    /// </summary>
    public string SuggestedInterfaceName { get; set; } = "";

    /// <summary>
    /// Public members that can be included in the interface
    /// </summary>
    public List<InterfaceMemberCandidate> Members { get; set; } = new();

    /// <summary>
    /// Whether the class already implements any interfaces
    /// </summary>
    public List<string> ExistingInterfaces { get; set; } = new();
}

public class InterfaceMemberCandidate
{
    public string Name { get; set; } = "";
    public InterfaceMemberKind Kind { get; set; }
    public string Signature { get; set; } = "";
    public string? ReturnType { get; set; }
    public List<string> Parameters { get; set; } = new();
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    /// <summary>
    /// The interface signature (without implementation details)
    /// </summary>
    public string InterfaceSignature { get; set; } = "";
}

public class ConvertToInterfaceOptions
{
    /// <summary>
    /// Name for the new interface
    /// </summary>
    public string InterfaceName { get; set; } = "";

    /// <summary>
    /// Names of members to include in the interface
    /// </summary>
    public List<string> MemberNames { get; set; } = new();

    /// <summary>
    /// Whether to make the class implement the new interface
    /// </summary>
    public bool ImplementInterface { get; set; } = true;

    /// <summary>
    /// Whether to create the interface in a separate file
    /// </summary>
    public bool CreateInSeparateFile { get; set; }

    /// <summary>
    /// File path for the new interface (if CreateInSeparateFile is true)
    /// </summary>
    public string? InterfaceFilePath { get; set; }

    /// <summary>
    /// Whether to add the interface above the class in the same file
    /// </summary>
    public bool AddAboveClass { get; set; } = true;
}

public class ConvertToInterfaceResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public string InterfaceName { get; set; } = "";
    public string? InterfaceFilePath { get; set; }
    public int MembersIncluded { get; set; }
}

#region Invert If

public class InvertIfInfo
{
    public string OriginalCondition { get; set; } = "";
    public string InvertedCondition { get; set; } = "";
    public string IfBranch { get; set; } = "";
    public string ElseBranch { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string FilePath { get; set; } = "";
    public string Preview { get; set; } = "";
}

public class InvertIfResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
}

#endregion

#region Convert to Select Case

public class ConvertToSelectCaseInfo
{
    public string TestExpression { get; set; } = "";
    public List<IfElseIfBranch> Branches { get; set; } = new();
    public string? ElseBranch { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string FilePath { get; set; } = "";
    public string Preview { get; set; } = "";
}

public class IfElseIfBranch
{
    public string Condition { get; set; } = "";
    public string CaseValue { get; set; } = "";
    public string Body { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public class ConvertToSelectCaseResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int CasesCreated { get; set; }
}

#endregion

#region Split Declaration

public class SplitDeclarationInfo
{
    public string VariableName { get; set; } = "";
    public string VariableType { get; set; } = "";
    public string InitializerExpression { get; set; } = "";
    public string DeclarationLine { get; set; } = "";
    public int Line { get; set; }
    public string FilePath { get; set; } = "";
    public string PreviewDeclaration { get; set; } = "";
    public string PreviewAssignment { get; set; } = "";
}

public class SplitDeclarationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
}

#endregion

#region Introduce Field

public class IntroduceFieldInfo
{
    public string VariableName { get; set; } = "";
    public string VariableType { get; set; } = "";
    public string? InitializerExpression { get; set; }
    public string SuggestedFieldName { get; set; } = "";
    public int VariableLine { get; set; }
    public string ClassName { get; set; } = "";
    public int ClassStartLine { get; set; }
    public string FilePath { get; set; } = "";
}

public class IntroduceFieldOptions
{
    public string FieldName { get; set; } = "";
    public FieldAccessibility Accessibility { get; set; } = FieldAccessibility.Private;
    public bool InitializeInConstructor { get; set; }
    public bool InitializeInline { get; set; } = true;
    public bool RemoveLocalVariable { get; set; } = true;
}

public class IntroduceFieldResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public string FieldName { get; set; } = "";
}

#endregion

#region Surround With

public enum SurroundWithType
{
    IfThen,
    IfThenElse,
    ForNext,
    ForEach,
    WhileWend,
    DoLoopWhile,
    DoLoopUntil,
    DoWhileLoop,
    DoUntilLoop,
    TryCatch,
    TryCatchFinally,
    SelectCase,
    Region,
    With
}

public class SurroundWithOption
{
    public SurroundWithType Type { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Preview { get; set; } = "";
}

public class SurroundWithResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<FileEdit> FileEdits { get; set; } = Array.Empty<FileEdit>();
    public int CursorLine { get; set; }
    public int CursorColumn { get; set; }
}

#endregion

#region Go To Definition

public class DefinitionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string? SymbolName { get; set; }
    public SymbolKind SymbolKind { get; set; }
    public string? Preview { get; set; }
}

public class PeekDefinitionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FilePath { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? SymbolName { get; set; }
    public SymbolKind SymbolKind { get; set; }
    public string? SourceCode { get; set; }
}

#endregion
