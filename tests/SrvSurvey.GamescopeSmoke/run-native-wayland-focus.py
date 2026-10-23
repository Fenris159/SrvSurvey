"""Validate real native Wayland focus privately, optionally through the production tracker.

Requirements: GNOME Shell with headless automation, zenity, Xvfb, cc/pkg-config,
X11 development files, working Gamescope Vulkan backend, and optional
prebuilt SrvSurvey.GamescopeSmoke DLL. No live desktop or installed files change.
"""
import argparse
import json
import os
import pathlib
import select
import shlex
import shutil
import signal
import subprocess
import tempfile
import threading
import time

HERE = pathlib.Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--inner', required=True, help='Gamescope with the existing byte publisher or candidate native focus patch')
parser.add_argument('--output', type=pathlib.Path, required=True)
parser.add_argument('--expect-sidecar', action='store_true')
parser.add_argument('--dll', type=pathlib.Path, help='Already built smoke DLL with --desktop-tracking; no build is performed')
args = parser.parse_args()
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
assert not (output/'session.json').exists(), 'Use a fresh artifact directory for each run'
inner = shutil.which(args.inner)
assert inner, args.inner
flags = shlex.split(subprocess.check_output(['pkg-config', '--cflags', '--libs', 'x11'], text=True))
subprocess.run(['cc', '-Wall', '-Wextra', '-O2', str(HERE/'native/native-wayland-game.c'), '-o', str(output/'EliteDangerous64.exe'), *flags], check=True)
env = os.environ.copy()
for key in list(env):
    if key.startswith(('GAMESCOPE', 'ELITE_HOST_FOCUS', 'DBUS', 'VK_')) or key in ('DISPLAY', 'WAYLAND_DISPLAY', 'LD_PRELOAD', 'XDG_SESSION_ID', 'XDG_SESSION_TYPE', 'XDG_CURRENT_DESKTOP'):
        env.pop(key, None)


def line(process):
    assert select.select([process.stdout], [], [], 15)[0], 'Timed out reading probe output'
    value = process.stdout.readline().strip()
    assert value, 'Probe ended without expected output'
    return value


def stop(process):
    if process is None:
        return
    # A private session leader can exit before descendants; still clean its
    # process group. Every process passed here was started in a new session.
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        pass
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    process.wait()


snapshots = []
errors = []
finished = threading.Event()
probe = xvfb = shell = worker = None
with tempfile.TemporaryDirectory(prefix='srv-native-gnome-') as private:
    private = pathlib.Path(private)
    runtime = private/'runtime'
    runtime.mkdir(mode=0o700)
    env.update(XDG_RUNTIME_DIR=str(runtime), XDG_CONFIG_HOME=str(private/'config'), XDG_DATA_HOME=str(private/'data'), XDG_CACHE_HOME=str(private/'cache'), GSETTINGS_BACKEND='memory', GDK_BACKEND='wayland', WAYLAND_DISPLAY='srv-private-native-wayland', ELITE_HOST_FOCUS_FILE=str(output/'host-focus'), FOCUS_OUTPUT=str(output), FOCUS_INNER=inner, FOCUS_GAME=str(output/'EliteDangerous64.exe'), ENABLE_GAMESCOPE_WSI='0', FOCUS_EXPECT_SIDECAR='1' if args.expect_sidecar else '0', FOCUS_USE_TRACKER='1' if args.dll else '0')
    if args.expect_sidecar:
        env.pop('ELITE_HOST_FOCUS_FILE', None)
    (output/'host-focus').write_text('0')
    (output/'host-focus').chmod(0o600)
    try:
        if args.dll:
            assert args.dll.is_file(), args.dll
            xvfb = subprocess.Popen(['Xvfb', '-displayfd', '1', '-screen', '0', '1600x1000x24', '-nolisten', 'tcp'], stdout=subprocess.PIPE, stderr=(output/'xvfb.log').open('w'), text=True, start_new_session=True)
            tracker_env = env.copy()
            tracker_env.update(DISPLAY=':'+line(xvfb), XDG_SESSION_TYPE='x11', GDK_BACKEND='x11')
            tracker_env.pop('WAYLAND_DISPLAY', None)
            probe = subprocess.Popen(['dotnet', str(args.dll.resolve()), '--desktop-tracking'], env=tracker_env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=(output/'tracker.err').open('w'), text=True, start_new_session=True, bufsize=1)
            while not line(probe).startswith('DESKTOP_READY'):
                pass

            def sample_requests():
                seen = None
                try:
                    while not finished.is_set():
                        request = output/'tracker.request'
                        label = request.read_text().strip() if request.exists() else ''
                        if label and label != seen:
                            session = json.loads((output/'session.json').read_text())
                            if args.expect_sidecar and label != 'after-stop':
                                focus_stat = (runtime/('GamescopeGameWindowFocus.'+session['pid'])).stat()
                                assert focus_stat.st_uid == os.getuid() and focus_stat.st_mode & 0o777 == 0o600
                            marker = runtime/('GamescopeGameWindowBridge.'+session['pid'])
                            marker.write_text(session['start']+'\n'+session['display']+'\n100 80 1280 800\n')
                            marker.chmod(0o600)
                            time.sleep(.15)
                            probe.stdin.write('snapshot\n')
                            probe.stdin.flush()
                            value = line(probe)
                            while not value.startswith('{'):
                                value = line(probe)
                            snapshot = json.loads(value)
                            snapshot['Phase'] = label
                            snapshots.append(snapshot)
                            print('PRODUCTION_TRACKER', json.dumps(snapshot, separators=(',', ':')), flush=True)
                            if label != 'after-stop':
                                assert snapshot['Available'] and snapshot['Visible'], snapshot
                                assert snapshot['Foreground'] == (label != 'leave'), snapshot
                            else:
                                assert not snapshot['Available'], snapshot
                            (output/'tracker.reply').write_text(label)
                            seen = label
                        time.sleep(.03)
                except BaseException as error:
                    errors.append(str(error))
                    (output/'tracker.reply').write_text('ERROR '+str(error))

            worker = threading.Thread(target=sample_requests, daemon=True)
            worker.start()
        cmd = ['dbus-run-session', '--', 'gnome-shell', '--headless', '--virtual-monitor', '1280x800', '--no-x11', '--wayland-display', 'srv-private-native-wayland', '--automation-script', str(HERE/'native/native-wayland-focus.js')]
        shell = subprocess.Popen(cmd, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, start_new_session=True)
        try:
            stdout, stderr = shell.communicate(timeout=100)
        except subprocess.TimeoutExpired:
            os.killpg(shell.pid, signal.SIGTERM)
            try:
                stdout, stderr = shell.communicate(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(shell.pid, signal.SIGKILL)
                stdout, stderr = shell.communicate()
        (output/'shell.log').write_text(stdout)
        (output/'shell.err').write_text(stderr)
        print(stdout)
        print('Exit', shell.returncode, 'Artifacts', output)
        assert shell.returncode == 0, stderr[-6000:]
        assert 'NATIVE_WAYLAND_PROTOCOL_PASS' in stdout, stdout
        assert not errors, errors
        if args.dll:
            assert [s['Phase'] for s in snapshots] == ['enter', 'leave', 'return', 'after-stop'], snapshots
    finally:
        finished.set()
        if worker is not None:
            worker.join(timeout=1)
        stop(shell)
        stop(probe)
        stop(xvfb)
(output/'tracker-snapshots.json').write_text(json.dumps(snapshots, indent=2)+'\n')
print('PASS: actual native backend focus events'+(' propagate through the production tracker.' if args.dll else '.'))
