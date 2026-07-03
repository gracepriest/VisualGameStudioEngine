import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    State,
    TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;
let outputChannel: vscode.OutputChannel;
let extCtx: vscode.ExtensionContext | undefined;
let fileWatchers: vscode.FileSystemWatcher[] = [];
let restartInProgress = false;

function disposeFileWatchers() {
    for (const watcher of fileWatchers) {
        try {
            watcher.dispose();
        } catch { }
    }
    fileWatchers = [];
}

export function activate(context: vscode.ExtensionContext) {
    extCtx = context;
    outputChannel = vscode.window.createOutputChannel('BasicLang');
    context.subscriptions.push(outputChannel);
    outputChannel.appendLine('BasicLang extension activating...');

    // Register commands
    context.subscriptions.push(
        vscode.commands.registerCommand('basiclang.restartServer', restartServer),
        vscode.commands.registerCommand('basiclang.showOutput', () => outputChannel.show()),
        vscode.commands.registerCommand('basiclang.build', buildProject),
        vscode.commands.registerCommand('basiclang.run', runProject)
    );

    // Register debug adapter (BasicLang.exe --debug-adapter, DAP on stdio)
    context.subscriptions.push(
        vscode.debug.registerDebugAdapterDescriptorFactory('basiclang', {
            createDebugAdapterDescriptor(_session) {
                const exe = findBasicLangExe(extCtx!);
                if (!exe) {
                    vscode.window.showErrorMessage('BasicLang compiler not found. Set basiclang.languageServerPath in settings.');
                    return undefined;
                }
                return new vscode.DebugAdapterExecutable(exe, ['--debug-adapter']);
            }
        }),
        vscode.debug.registerDebugConfigurationProvider('basiclang', {
            resolveDebugConfiguration(_folder, config) {
                // Fill in a missing/empty configuration (e.g. F5 with no launch.json)
                if (!config.type && !config.request && !config.name) {
                    config.type = 'basiclang';
                    config.request = 'launch';
                    config.name = 'BasicLang: Launch';
                }
                if (!config.program) {
                    const editor = vscode.window.activeTextEditor;
                    if (editor && /\.(bas|bl)$/i.test(editor.document.uri.fsPath)) {
                        config.program = editor.document.uri.fsPath;
                    } else {
                        config.program = '${workspaceFolder}/Program.bas';
                    }
                }
                return config;
            }
        })
    );

    // Register task provider for tasks.json entries of type 'basiclang'
    context.subscriptions.push(
        vscode.tasks.registerTaskProvider('basiclang', {
            provideTasks: () => makeDefaultTasks(),
            resolveTask: (task) => makeTaskFromDefinition(task.definition as any, task.scope)
        })
    );

    // React to configuration changes that affect the language server
    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration(async (e) => {
            if (!e.affectsConfiguration('basiclang')) {
                return;
            }
            if (e.affectsConfiguration('basiclang.languageServerPath')) {
                // Server executable changed - restart immediately
                outputChannel.appendLine('basiclang.languageServerPath changed, restarting language server...');
                await restartServer();
            } else {
                const choice = await vscode.window.showInformationMessage(
                    'BasicLang settings changed. Restart the language server to apply them?',
                    'Restart'
                );
                if (choice === 'Restart') {
                    await restartServer();
                }
            }
        })
    );

    // Start the language server
    startLanguageServer(context);

    outputChannel.appendLine('BasicLang extension activated');
}

async function makeDefaultTasks(): Promise<vscode.Task[]> {
    const exePath = extCtx ? findBasicLangExe(extCtx) : undefined;
    if (!exePath) {
        return [];
    }

    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (!workspaceFolder) {
        return [];
    }

    const projectFiles = await vscode.workspace.findFiles('**/*.blproj', null, 1);
    const projectFile = projectFiles.length > 0 ? projectFiles[0].fsPath : '';

    const tasks: vscode.Task[] = [];
    for (const taskName of ['build', 'run']) {
        const args = projectFile ? [taskName, projectFile] : [taskName];
        const task = new vscode.Task(
            { type: 'basiclang', task: taskName },
            workspaceFolder,
            taskName === 'build' ? 'Build' : 'Run',
            'BasicLang',
            new vscode.ShellExecution(exePath, args),
            '$basiclang'
        );
        tasks.push(task);
    }
    return tasks;
}

function makeTaskFromDefinition(
    definition: { type: string; task: string; project?: string },
    scope: vscode.WorkspaceFolder | vscode.TaskScope | undefined
): vscode.Task | undefined {
    if (!definition || !definition.task) {
        return undefined;
    }

    const exePath = extCtx ? findBasicLangExe(extCtx) : undefined;
    if (!exePath) {
        return undefined;
    }

    const args = definition.project ? [definition.task, definition.project] : [definition.task];
    return new vscode.Task(
        definition,
        scope ?? vscode.TaskScope.Workspace,
        definition.task,
        'BasicLang',
        new vscode.ShellExecution(exePath, args),
        '$basiclang'
    );
}

function isFile(p: string): boolean {
    try {
        return fs.statSync(p).isFile();
    } catch {
        return false;
    }
}

export function findBasicLangExe(context: vscode.ExtensionContext): string | undefined {
    const config = vscode.workspace.getConfiguration('basiclang');

    // 1. Explicit setting
    const configuredPath = config.get<string>('languageServerPath');
    if (configuredPath && isFile(configuredPath)) {
        return configuredPath;
    }

    // 2. Bundled with extension
    const possiblePaths = [
        path.join(context.extensionPath, 'server', 'BasicLang.exe'),
        path.join(context.extensionPath, 'server', 'BasicLang'),
        // 3. Common installation paths
        path.join(process.env.LOCALAPPDATA || '', 'BasicLang', 'BasicLang.exe'),
        path.join(process.env.PROGRAMFILES || '', 'BasicLang', 'BasicLang.exe')
    ];

    for (const p of possiblePaths) {
        if (isFile(p)) {
            return p;
        }
    }

    // 4. Scan PATH
    for (const dir of (process.env.PATH || '').split(path.delimiter)) {
        if (!dir) {
            continue;
        }
        for (const name of ['BasicLang.exe', 'BasicLang']) {
            const candidate = path.join(dir, name);
            if (isFile(candidate)) {
                return candidate;
            }
        }
    }

    // 5. Unix install locations
    for (const p of ['/usr/local/bin/BasicLang', '/usr/bin/BasicLang']) {
        if (isFile(p)) {
            return p;
        }
    }

    return undefined;
}

async function startLanguageServer(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration('basiclang');
    const serverPath = findBasicLangExe(context);

    if (!serverPath) {
        outputChannel.appendLine('BasicLang language server not found. Please configure basiclang.languageServerPath');
        vscode.window.showWarningMessage(
            'BasicLang language server not found. Some features may not work. ' +
            'Please install BasicLang or configure the path in settings.'
        );
        return;
    }

    outputChannel.appendLine(`Starting BasicLang language server: ${serverPath}`);

    const serverOptions: ServerOptions = {
        run: {
            command: serverPath,
            args: ['--lsp'],
            transport: TransportKind.stdio
        },
        debug: {
            command: serverPath,
            args: ['--lsp'],
            transport: TransportKind.stdio
        }
    };

    // Dispose any watchers left over from a previous client before creating new ones
    disposeFileWatchers();
    fileWatchers = [
        vscode.workspace.createFileSystemWatcher('**/*.bl'),
        vscode.workspace.createFileSystemWatcher('**/*.bas'),
        vscode.workspace.createFileSystemWatcher('**/*.blproj')
    ];

    const clientOptions: LanguageClientOptions = {
        documentSelector: [
            { scheme: 'file', language: 'basiclang' },
            { scheme: 'untitled', language: 'basiclang' }
        ],
        synchronize: {
            fileEvents: fileWatchers
        },
        outputChannel: outputChannel,
        initializationOptions: {
            enableSemanticHighlighting: config.get<boolean>('enableSemanticHighlighting', true),
            enableInlayHints: config.get<boolean>('enableInlayHints', true),
            enableCodeLens: config.get<boolean>('enableCodeLens', true)
        }
    };

    client = new LanguageClient(
        'basiclang',
        'BasicLang Language Server',
        serverOptions,
        clientOptions
    );

    try {
        await client.start();
        outputChannel.appendLine('BasicLang language server started successfully');
    } catch (error) {
        // Discard the dead client so restart/deactivate don't trip over it
        const failedClient = client;
        client = undefined;
        try {
            await failedClient?.dispose();
        } catch { }
        disposeFileWatchers();
        outputChannel.appendLine(`Failed to start language server: ${error}`);
        vscode.window.showErrorMessage(
            'Failed to start the BasicLang language server. See the BasicLang output channel for details.'
        );
    }
}

async function restartServer() {
    if (restartInProgress) {
        outputChannel.appendLine('Language server restart already in progress, ignoring request.');
        return;
    }
    restartInProgress = true;

    try {
        outputChannel.appendLine('Restarting BasicLang language server...');

        const oldClient = client;
        client = undefined;
        if (oldClient) {
            try {
                if (oldClient.state === State.Running) {
                    await oldClient.stop();
                } else {
                    // Starting/StartFailed/Stopped: stop() would throw, discard instead
                    await oldClient.dispose();
                }
            } catch (error) {
                outputChannel.appendLine(`Error while stopping language server (discarding client): ${error}`);
            }
        }
        disposeFileWatchers();

        if (extCtx) {
            await startLanguageServer(extCtx);
        }
    } finally {
        restartInProgress = false;
    }
}

async function buildProject() {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        vscode.window.showWarningMessage('No active editor');
        return;
    }

    const workspaceFolder = vscode.workspace.getWorkspaceFolder(editor.document.uri);
    if (!workspaceFolder) {
        vscode.window.showWarningMessage('No workspace folder');
        return;
    }

    // Find project file
    const projectFiles = await vscode.workspace.findFiles('**/*.blproj', null, 1);
    const projectFile = projectFiles.length > 0
        ? projectFiles[0].fsPath
        : editor.document.uri.fsPath;

    const exePath = extCtx ? findBasicLangExe(extCtx) : undefined;
    if (!exePath) {
        vscode.window.showErrorMessage('BasicLang compiler not found. Set basiclang.languageServerPath in settings.');
        return;
    }

    outputChannel.appendLine(`Building: ${projectFile}`);
    outputChannel.show();

    const task = new vscode.Task(
        { type: 'basiclang', task: 'build' },
        workspaceFolder,
        'Build',
        'BasicLang',
        new vscode.ShellExecution(exePath, ['build', projectFile]),
        '$basiclang'
    );

    await vscode.tasks.executeTask(task);
}

async function runProject() {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        vscode.window.showWarningMessage('No active editor');
        return;
    }

    const workspaceFolder = vscode.workspace.getWorkspaceFolder(editor.document.uri);
    if (!workspaceFolder) {
        vscode.window.showWarningMessage('No workspace folder');
        return;
    }

    // Find project file
    const projectFiles = await vscode.workspace.findFiles('**/*.blproj', null, 1);
    const projectFile = projectFiles.length > 0
        ? projectFiles[0].fsPath
        : editor.document.uri.fsPath;

    const exePath = extCtx ? findBasicLangExe(extCtx) : undefined;
    if (!exePath) {
        vscode.window.showErrorMessage('BasicLang compiler not found. Set basiclang.languageServerPath in settings.');
        return;
    }

    outputChannel.appendLine(`Running: ${projectFile}`);
    outputChannel.show();

    const task = new vscode.Task(
        { type: 'basiclang', task: 'run' },
        workspaceFolder,
        'Run',
        'BasicLang',
        new vscode.ShellExecution(exePath, ['run', projectFile]),
        '$basiclang'
    );

    await vscode.tasks.executeTask(task);
}

export async function deactivate(): Promise<void> {
    disposeFileWatchers();
    if (!client) {
        return;
    }
    outputChannel.appendLine('BasicLang extension deactivating...');
    const oldClient = client;
    client = undefined;
    try {
        if (oldClient.state === State.Running) {
            await oldClient.stop();
        } else {
            await oldClient.dispose();
        }
    } catch { }
}
