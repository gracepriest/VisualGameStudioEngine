import * as vscode from 'vscode';
import * as path from 'path';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;
let outputChannel: vscode.OutputChannel;

export function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel('BasicLang');
    outputChannel.appendLine('BasicLang extension activating...');

    // Register commands
    context.subscriptions.push(
        vscode.commands.registerCommand('basiclang.restartServer', restartServer),
        vscode.commands.registerCommand('basiclang.showOutput', () => outputChannel.show()),
        vscode.commands.registerCommand('basiclang.build', buildProject),
        vscode.commands.registerCommand('basiclang.run', runProject)
    );

    // Start the language server
    startLanguageServer(context);

    outputChannel.appendLine('BasicLang extension activated');
}

async function startLanguageServer(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration('basiclang');
    let serverPath = config.get<string>('languageServerPath');

    // If no path specified, try to find the server
    if (!serverPath) {
        // Try various common locations
        const possiblePaths = [
            // Bundled with extension
            path.join(context.extensionPath, 'server', 'BasicLang.exe'),
            path.join(context.extensionPath, 'server', 'BasicLang'),
            // System path
            'BasicLang',
            // Common installation paths
            path.join(process.env.LOCALAPPDATA || '', 'BasicLang', 'BasicLang.exe'),
            path.join(process.env.PROGRAMFILES || '', 'BasicLang', 'BasicLang.exe'),
            '/usr/local/bin/BasicLang',
            '/usr/bin/BasicLang'
        ];

        for (const p of possiblePaths) {
            try {
                const fs = require('fs');
                if (fs.existsSync(p)) {
                    serverPath = p;
                    break;
                }
            } catch { }
        }
    }

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
            args: ['lsp'],
            transport: TransportKind.stdio
        },
        debug: {
            command: serverPath,
            args: ['lsp', '--debug'],
            transport: TransportKind.stdio
        }
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [
            { scheme: 'file', language: 'basiclang' },
            { scheme: 'untitled', language: 'basiclang' }
        ],
        synchronize: {
            fileEvents: [
                vscode.workspace.createFileSystemWatcher('**/*.bl'),
                vscode.workspace.createFileSystemWatcher('**/*.bas'),
                vscode.workspace.createFileSystemWatcher('**/*.blproj')
            ]
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
        outputChannel.appendLine(`Failed to start language server: ${error}`);
        vscode.window.showErrorMessage(`Failed to start BasicLang language server: ${error}`);
    }
}

async function restartServer() {
    outputChannel.appendLine('Restarting BasicLang language server...');

    if (client) {
        await client.stop();
        client = undefined;
    }

    const context = (global as any).extensionContext;
    if (context) {
        await startLanguageServer(context);
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

    outputChannel.appendLine(`Building: ${projectFile}`);
    outputChannel.show();

    const task = new vscode.Task(
        { type: 'basiclang', task: 'build' },
        workspaceFolder,
        'Build',
        'BasicLang',
        new vscode.ShellExecution('BasicLang', ['build', projectFile]),
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

    outputChannel.appendLine(`Running: ${projectFile}`);
    outputChannel.show();

    const task = new vscode.Task(
        { type: 'basiclang', task: 'run' },
        workspaceFolder,
        'Run',
        'BasicLang',
        new vscode.ShellExecution('BasicLang', ['run', projectFile]),
        '$basiclang'
    );

    await vscode.tasks.executeTask(task);
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    outputChannel.appendLine('BasicLang extension deactivating...');
    return client.stop();
}
