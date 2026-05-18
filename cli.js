#!/usr/bin/env node
const { spawn } = require('child_process');
const path = require('path');

const projectPath = path.join(__dirname, 'MonitorApp', 'MonitorApp.csproj');

console.log('Starting PC Component Monitoring...');

const child = spawn('dotnet', ['run', '--project', projectPath], {
    stdio: 'inherit',
    windowsHide: false
});

child.on('error', (err) => {
    console.error('Failed to start the application. Make sure .NET SDK is installed.');
    console.error(err);
    process.exit(1);
});
