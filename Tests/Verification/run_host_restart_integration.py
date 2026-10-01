#!/usr/bin/env python3
"""Exercise actual POSIX single-file hosts, terminal restarts, and HTTP self-update.

Release metadata comes from GitHub; a local download proxy supplies the rebuilt
new executable so the test never installs an older, unpatched public binary.
The installation's configuration and component directories are preserved.
"""
import argparse
import errno
import fcntl
import hashlib
import http.server
import json
import os
from pathlib import Path
import pty
import secrets
import select
import shutil
import signal
import struct
import subprocess
import tempfile
import termios
import threading
import time
import urllib.request
import zipfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--old-publish', type=Path, required=True)
    parser.add_argument('--new-publish', type=Path, required=True)
    parser.add_argument('--install-dir', type=Path, required=True)
    parser.add_argument('--old-version', default='0.9.4')
    parser.add_argument('--new-version', default='0.9.5')
    parser.add_argument('--port', type=int, default=8001)
    args = parser.parse_args()
    root = Path(tempfile.mkdtemp(prefix='ShiroBot.HostRestart.'))
    install = args.install_dir.resolve()
    install.mkdir(parents=True, exist_ok=True)
    executable = install / 'ShiroBot'
    if executable.exists():
        shutil.copy2(executable, root / 'original-ShiroBot')
    latest = args.new_publish.resolve() / 'ShiroBot'
    package = root / 'host.zip'
    with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.write(latest, 'ShiroBot')
    downloads = []

    class Proxy(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            if not self.path.endswith('shirobot-host-osx-arm64-framework-dependent.zip'):
                self.send_error(404)
                return
            downloads.append(self.path)
            self.send_response(200)
            self.send_header('Content-Length', str(package.stat().st_size))
            self.end_headers()
            with package.open('rb') as stream:
                shutil.copyfileobj(stream, self.wfile)

        def log_message(self, *_):
            pass

    proxy = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Proxy)
    threading.Thread(target=proxy.serve_forever, daemon=True).start()
    key = secrets.token_hex(32)
    config = root / 'test-config.toml'
    config.write_text(f'''protocols = []
enable_log = true
disable_console_input = false
github_proxy = "http://127.0.0.1:{proxy.server_port}"
host_update_repository = "ShirokaProject/ShiroBot"
[api]
enable = true
listen_urls = ["http://127.0.0.1:{args.port}"]
[api.auth]
enable = true
key = "{key}"
''')
    (root / 'plugins').mkdir()
    command = [str(executable), '--config', str(config), '--adapter', str(root / 'no-adapter.dll'),
               '--plugin-dir', str(root / 'plugins')]
    base = f'http://127.0.0.1:{args.port}'

    def api(path, method='GET'):
        request = urllib.request.Request(base + '/api/v1/' + path,
                                         data=b'' if method == 'POST' else None,
                                         headers={'Authorization': 'Bearer ' + key}, method=method)
        def request_result():
            with urllib.request.urlopen(request, timeout=30) as response:
                return json.load(response)
        if method != 'POST':
            return request_result()
        # POST logs to the console; keep the emulator answering cursor queries
        # while the HTTP request is in progress.
        outcome = []
        def send():
            try:
                outcome.append(request_result())
            except Exception as error:
                outcome.append(error)
        worker = threading.Thread(target=send)
        worker.start()
        while worker.is_alive():
            read_output()
        worker.join()
        if isinstance(outcome[0], Exception):
            raise outcome[0]
        return outcome[0]

    transcript = bytearray()
    master = None
    pid = None
    results = []

    def read_output():
        if select.select([master], [], [], 0.05)[0]:
            try:
                data = os.read(master, 65536)
                transcript.extend(data)
                # A PTY is not a terminal emulator. Answer cursor-position queries
                # made by Console.CursorTop, as an actual terminal would.
                for _ in range(data.count(b'\x1b[6n')):
                    os.write(master, b'\x1b[1;1R')
                (root / 'terminal.log').write_bytes(transcript)
            except OSError as error:
                if error.errno != errno.EIO:
                    raise

    def wait_until(predicate, timeout=40):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            read_output()
            try:
                if predicate():
                    return
            except (OSError, ValueError):
                pass
        raise AssertionError('Timed out; inspect terminal.log: ' + str(root))

    def ready(version):
        return api('overview')['bot_version'] == version

    def console_help():
        offset = len(transcript)
        os.write(master, b'help\r')
        wait_until(lambda: b'update' in transcript[offset:] and b'plugins' in transcript[offset:])
        assert os.tcgetpgrp(master) == pid, 'Restart lost the foreground terminal'

    try:
        shutil.copy2(args.old_publish.resolve() / 'ShiroBot', executable)
        executable.chmod(0o755)
        pid, master = pty.fork()
        if pid == 0:
            os.environ['TERM'] = 'xterm-256color'
            fcntl.ioctl(0, termios.TIOCSWINSZ, struct.pack('HHHH', 46, 199, 0, 0))
            os.chdir(install)
            os.execv(command[0], command)
        wait_until(lambda: ready(args.old_version))
        console_help()

        # A second process must fail promptly, including when its stdin is a TTY.
        other_pid, other_master = pty.fork()
        if other_pid == 0:
            os.environ['TERM'] = 'xterm-256color'
            fcntl.ioctl(0, termios.TIOCSWINSZ, struct.pack('HHHH', 46, 199, 0, 0))
            os.chdir(install)
            os.execv(command[0], command)
        other_log = bytearray()
        status = None
        try:
            deadline = time.monotonic() + 15
            while time.monotonic() < deadline:
                if select.select([other_master], [], [], 0.05)[0]:
                    try:
                        other_log.extend(os.read(other_master, 65536))
                    except OSError as error:
                        if error.errno != errno.EIO:
                            raise
                ended, status = os.waitpid(other_pid, os.WNOHANG)
                if ended:
                    break
            else:
                os.kill(other_pid, signal.SIGKILL)
                os.waitpid(other_pid, 0)
                raise AssertionError('Startup failure waited for a key')
            assert os.waitstatus_to_exitcode(status) == 1
            assert b'address already in use' in other_log
            assert b'Unhandled exception' not in other_log
            (root / 'duplicate-start.log').write_bytes(other_log)
            results.append('duplicate startup exits without ReadKey or unhandled exception')
        finally:
            os.close(other_master)

        for attempt in range(2):
            starts = transcript.count('ShiroBot 启动中'.encode())
            os.write(master, b'restart\r')
            wait_until(lambda: transcript.count('ShiroBot 启动中'.encode()) > starts and ready(args.old_version))
            console_help()
            results.append(f'console restart {attempt + 1}: same PID, API rebound, keyboard works')

        starts = transcript.count('ShiroBot 启动中'.encode())
        assert api('system/restart', 'POST')['ok']
        wait_until(lambda: transcript.count('ShiroBot 启动中'.encode()) > starts and ready(args.old_version))
        console_help()
        results.append('HTTP restart: same PID, API rebound, keyboard works')

        check = api('system/update')
        assert check['current_version'] == args.old_version, check
        assert check['latest_version'] == args.new_version and check['can_apply'], check
        starts = transcript.count('ShiroBot 启动中'.encode())
        applied = api('system/update', 'POST')
        assert applied['ok'] and applied['restarting'], applied
        wait_until(lambda: transcript.count('ShiroBot 启动中'.encode()) > starts and ready(args.new_version))
        console_help()
        assert downloads, 'Self-update did not download a package'
        assert hashlib.sha256(executable.read_bytes()).digest() == hashlib.sha256(latest.read_bytes()).digest()
        assert not Path(str(executable) + '.old').exists(), 'Old executable was not cleaned up'
        assert not api('system/update')['update_available']
        with urllib.request.urlopen(base + '/dashboard/') as response:
            html = response.read().decode()
        assert '/assets/' in html
        results.append('host update: old → new, real ZIP download/replacement/restart, latest check, embedded Dashboard')
        assert b'Unhandled exception' not in transcript
        assert b'address already in use' not in transcript
        assert api('system/shutdown', 'POST')['ok']
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            read_output()
            ended, status = os.waitpid(pid, os.WNOHANG)
            if ended:
                pid = None
                assert os.waitstatus_to_exitcode(status) == 0
                break
        else:
            raise AssertionError('Shutdown did not exit')
        (root / 'report.json').write_text(json.dumps(results, indent=2, ensure_ascii=False))
        for result in results:
            print('PASS:', result)
    finally:
        if pid is not None:
            try:
                os.kill(pid, signal.SIGTERM)
                for _ in range(100):
                    if os.waitpid(pid, os.WNOHANG)[0]:
                        break
                    time.sleep(0.05)
                else:
                    os.kill(pid, signal.SIGKILL)
                    os.waitpid(pid, 0)
            except ProcessLookupError:
                pass
        if master is not None:
            os.close(master)
        proxy.shutdown()
        (root / 'terminal.log').write_bytes(transcript)
        # Leave the user with the latest build, even if an assertion failed.
        shutil.copy2(latest, executable)
        executable.chmod(0o755)
        pdb = args.new_publish.resolve() / 'ShiroBot.pdb'
        if pdb.exists():
            shutil.copy2(pdb, install / pdb.name)
        print('Artifacts:', root)


if __name__ == '__main__':
    main()
