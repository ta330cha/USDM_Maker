import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtempSync, mkdirSync, copyFileSync, writeFileSync, readFileSync, readdirSync, existsSync, statSync, rmSync, realpathSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const launcher = fileURLToPath(new URL('../../../run-usdm.sh', import.meta.url));

function fixture(action) {
    const directory = realpathSync(mkdtempSync(join(tmpdir(), 'usdm launcher test ')));
    const repo = join(directory, 'repository with spaces');
    const fakeBin = join(directory, 'fake bin');
    const projectDirectory = join(repo, 'USDM_MindMap_Editor/src/USDM_MindMap_Editor.Web');
    mkdirSync(projectDirectory, { recursive: true });
    mkdirSync(fakeBin);
    copyFileSync(launcher, join(repo, 'run-usdm.sh'));
    writeFileSync(join(projectDirectory, 'USDM_MindMap_Editor.Web.csproj'), '<Project />');
    writeFileSync(join(fakeBin, 'dotnet'), `#!/bin/bash
printf 'APP_STDOUT\\n'
printf 'APP_STDERR\\n' >&2
printf 'ENV=%s/%s\\n' "$DOTNET_ENVIRONMENT" "$ASPNETCORE_ENVIRONMENT"
printf 'CWD=%s\\n' "$PWD"
printf 'ARG=%s\\n' "$@"
exit "$USDM_LAUNCHER_TEST_EXIT"
`, { mode: 0o755 });
    const run = (args = [], extraEnv = {}) => spawnSync('/bin/bash', [join(repo, 'run-usdm.sh'), ...args], {
        cwd: tmpdir(), encoding: 'utf8',
        env: { ...process.env, PATH: `${fakeBin}:/usr/bin:/bin`, CDPATH: '/', USDM_LAUNCHER_TEST_EXIT: '0', ...extraEnv }
    });
    try { action({ directory, repo, projectDirectory, run, logs: join(directory, 'logs') }); }
    finally { rmSync(directory, { recursive: true, force: true }); }
}

test('normal launch resolves a spaced path independently of cwd and uses local Development HTTP', () => fixture(({ run, projectDirectory, logs }) => {
    const result = run();
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /APP_STDOUT/);
    assert.match(result.stderr, /APP_STDERR/);
    assert.ok(result.stdout.includes(`CWD=${projectDirectory}`));
    assert.match(result.stdout, /ENV=Development\/Development/);
    assert.match(result.stdout, /ARG=--no-launch-profile/);
    assert.match(result.stdout, /ARG=http:\/\/127\.0\.0\.1:5223/);
    assert.equal(existsSync(logs), false);
}));

test('log mode saves both streams in distinct private files and retains existing logs', () => fixture(({ run, logs }) => {
    mkdirSync(logs);
    writeFileSync(join(logs, 'existing.log'), 'keep this');
    for (const flag of ['--log', '--logs']) {
        const result = run([flag]);
        assert.equal(result.status, 0, result.stderr);
        assert.ok(result.stdout.includes(`ログ保存先: ${logs}/`));
        assert.doesNotMatch(result.stdout + result.stderr, /APP_STDOUT|APP_STDERR/);
    }
    const files = readdirSync(logs).filter(name => name !== 'existing.log');
    assert.equal(files.length, 2);
    for (const name of files) {
        assert.match(name, /^usdm-\d{8}-\d{6}-\d+\.log$/);
        const contents = readFileSync(join(logs, name), 'utf8');
        assert.match(contents, /APP_STDOUT/);
        assert.match(contents, /APP_STDERR/);
        assert.match(contents, /起動 URL=http:\/\/127\.0\.0\.1:5223/);
        assert.equal(statSync(join(logs, name)).mode & 0o777, 0o600);
    }
    assert.equal(readFileSync(join(logs, 'existing.log'), 'utf8'), 'keep this');
}));

test('dotnet failure status survives normal and file-log modes', () => fixture(({ run, logs }) => {
    for (const args of [[], ['--log']]) {
        const result = run(args, { USDM_LAUNCHER_TEST_EXIT: '23' });
        assert.equal(result.status, 23);
    }
    assert.equal(readdirSync(logs).length, 1);
}));

test('custom port and no-build are passed through without launching a profile', () => fixture(({ run }) => {
    const result = run(['--port', '05230', '--no-build']);
    assert.equal(result.status, 0);
    assert.match(result.stdout, /ARG=--no-build/);
    assert.match(result.stdout, /ARG=http:\/\/127\.0\.0\.1:5230/);
}));

test('help and invalid arguments do not launch dotnet or create logs', () => fixture(({ run, logs }) => {
    assert.equal(run(['--help']).status, 0);
    for (const args of [['--port'], ['--port', '0'], ['--port', '65536'], ['--port', '999999999999999999'],
        ['--port', '1.5'], ['--port', '-1'], ['--port', 'abc'], ['--unknown'], ['--log', '--port', 'bad']]) {
        const result = run(args);
        assert.equal(result.status, 2, `${args}: ${result.stderr}`);
        assert.doesNotMatch(result.stdout, /APP_STDOUT/);
    }
    assert.equal(existsSync(logs), false);
}));

test('an unusable log directory prevents application startup', () => fixture(({ run, logs }) => {
    writeFileSync(logs, 'not a directory');
    const result = run(['--log']);
    assert.notEqual(result.status, 0);
    assert.doesNotMatch(result.stdout + result.stderr, /APP_STDOUT/);
    assert.equal(readFileSync(logs, 'utf8'), 'not a directory');
}));
