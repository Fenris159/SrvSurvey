"""Probe actual desktop tracking against private Xvfb + real headless Gamescope.

The outer windows are synthetic geometry/focus fixtures. This does not validate
real SDL/Wayland outer presentation, pixel scaling, or any user's desktop.
"""
import argparse
import json
import os
import pathlib
import re
import select
import shlex
import shutil
import signal
import subprocess
import tempfile
import time

HERE = pathlib.Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dll', type=pathlib.Path, required=True, help='Built SrvSurvey.GamescopeSmoke DLL with --desktop-tracking')
parser.add_argument('--gamescope', default='gamescope')
parser.add_argument('--native-dir', type=pathlib.Path, default=HERE/'native')
parser.add_argument('--output', type=pathlib.Path)
args = parser.parse_args()
dll = args.dll.resolve()
assert dll.is_file(), dll
gamescope = shutil.which(args.gamescope)
assert gamescope, args.gamescope
output = (args.output or pathlib.Path(tempfile.mkdtemp(prefix='srvsurvey-desktop-bridge-'))).resolve()
output.mkdir(parents=True, exist_ok=True)
native = output/'driver'
flags = shlex.split(subprocess.check_output(['pkg-config', '--cflags', '--libs', 'x11'], text=True))
subprocess.run(['cc', '-Wall', '-Wextra', '-O2', str(args.native_dir/'desktop-host.c'), '-o', str(native), *flags], check=True)
game = output/'EliteDangerous64.exe'
shutil.copy2(native, game)
events = []

def line(process, timeout=15):
    assert select.select([process.stdout], [], [], timeout)[0], 'timeout waiting for process output'
    result = process.stdout.readline().strip()
    assert result, 'process ended without expected output'
    return result

def wait_for(process, prefix):
    while True:
        value = line(process)
        if value.startswith(prefix): return value
        print('CHILD:', value)

def sample(probe, phase):
    time.sleep(.12)  # Exceeds the production shared tracker cache's 40 ms freshness.
    probe.stdin.write('snapshot\n'); probe.stdin.flush()
    value = json.loads(wait_for(probe, '{'))
    value['Phase'] = phase
    events.append(value)
    print(json.dumps(value, separators=(',', ':')))
    return value

def scope_command(control, scope, command):
    control.write(command+'\n'); control.flush()
    wait_for(scope, 'ACK '+command)

def close_process(process):
    if process is not None and process.poll() is None:
        os.killpg(process.pid, signal.SIGTERM)
        try: process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL); process.wait()

env = os.environ.copy()
for key in ['DISPLAY', 'WAYLAND_DISPLAY', 'GAMESCOPE_WAYLAND_DISPLAY', 'GAMESCOPE_DISPLAY', 'LD_PRELOAD']:
    env.pop(key, None)
env['XDG_SESSION_TYPE'] = 'x11'
probe = xvfb = scope = None
with tempfile.TemporaryDirectory(prefix='srvsurvey-desktop-runtime-') as runtime:
    env['XDG_RUNTIME_DIR'] = runtime
    try:
        xvfb = subprocess.Popen(['Xvfb', '-displayfd', '1', '-screen', '0', '2400x1600x24', '-nolisten', 'tcp'], stdout=subprocess.PIPE, stderr=(output/'xvfb.log').open('w'), text=True, start_new_session=True)
        outer_display = ':'+line(xvfb)
        env['DISPLAY'] = outer_display
        env['HARNESS_OUTER_DISPLAY'] = outer_display
        probe = subprocess.Popen(['dotnet', str(dll), '--desktop-tracking'], env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=(output/'probe-errors.log').open('w'), text=True, start_new_session=True, bufsize=1)
        assert wait_for(probe, 'DESKTOP_READY') == 'DESKTOP_READY external=false capture=true'
        sample(probe, 'before-session')
        for session in range(2):
            fifo = output/f'control-{session}.fifo'; os.mkfifo(fifo)
            scope = subprocess.Popen([gamescope, '--backend', 'headless', '-W', '1280', '-H', '800', '-w', '960', '-h', '600', '--', str(native), str(game), str(fifo)], env=env, stdout=subprocess.PIPE, stderr=(output/f'gamescope-{session}.log').open('w'), text=True, start_new_session=True, bufsize=1)
            ready = wait_for(scope, 'HARNESS_READY ')
            metadata = re.search(r'scope=(\d+) outer=(0x[0-9a-f]+) inner=([^ ]+) game=(\d+)', ready)
            assert metadata, ready
            scope_pid, outer, inner_display, game_pid = metadata.groups()
            print(ready)
            control = fifo.open('w', buffering=1)
            time.sleep(1)
            subprocess.run(['xprop', '-display', inner_display, '-root', 'GAMESCOPE_XWAYLAND_SERVER_ID', 'GAMESCOPE_PID', 'GAMESCOPE_FOCUSED_WINDOW', 'GAMESCOPE_FOCUS_DISPLAY', 'GAMESCOPE_MOUSE_FOCUS_DISPLAY', 'GAMESCOPE_KEYBOARD_FOCUS_DISPLAY', 'GAMESCOPE_NEW_SCALING_SCALER'], stdout=(output/f'properties-{session}.log').open('w'), check=True)
            for command in ['foreground', 'background', 'foreground', 'move', 'resize', 'hide', 'show', 'hidden-state', 'clear-state', 'duplicate', 'unduplicate']:
                scope_command(control, scope, command)
                sample(probe, f'session{session}-{command}')
            # A valid existing marker intentionally lacks queryable desktop activation.
            scope_command(control, scope, 'hide')
            stat = pathlib.Path('/proc')/scope_pid/'stat'
            start = stat.read_text().rsplit(') ', 1)[1].split()[19]
            marker = pathlib.Path(runtime)/('GamescopeGameWindowBridge.'+scope_pid)
            marker.write_text(f'{start}\n{inner_display}\n100 80 1280 800\n')
            sample(probe, f'session{session}-manual-marker-no-outer')
            marker.unlink()
            scope_command(control, scope, 'show')
            sample(probe, f'session{session}-automatic-restored')
            control.write('quit\n'); control.flush(); control.close()
            wait_for(scope, 'HARNESS_DONE')
            scope.wait(timeout=10); scope = None
            fifo.unlink()
            sample(probe, f'after-session{session}')
        probe.stdin.close(); probe.wait(timeout=10)
    finally:
        close_process(scope); close_process(probe); close_process(xvfb)
(output/'snapshots.json').write_text(json.dumps(events, indent=2)+'\n')
by_phase = {value['Phase']: value for value in events}
for index in range(2):
    prefix=f'session{index}-'
    assert by_phase[prefix+'foreground']['Available']
    assert by_phase[prefix+'foreground']['Foreground']
    assert not by_phase[prefix+'background']['Foreground'], 'BUG: nested foreground escaped the outer desktop focus gate'
    assert [by_phase[prefix+'move'][key] for key in ['X','Y','Width','Height']]==[320,200,1280,800]
    assert [by_phase[prefix+'resize'][key] for key in ['X','Y','Width','Height']]==[320,200,1000,700]
    assert not by_phase[prefix+'hide']['Available']
    assert by_phase[prefix+'show']['Available']
    assert not by_phase[prefix+'hidden-state']['Available'], 'BUG: a WM-hidden synthetic outer window is still treated as visible'
    assert by_phase[prefix+'clear-state']['Available']
    assert not by_phase[prefix+'duplicate']['Available']
    assert by_phase[prefix+'unduplicate']['Available']
    assert by_phase[prefix+'manual-marker-no-outer']['Available']
    assert by_phase[prefix+'manual-marker-no-outer']['Foreground'], 'Existing manual marker compatibility must retain unknown-host-focus behavior'
    assert by_phase[prefix+'automatic-restored']['Available']
    assert not by_phase['after-session'+str(index)]['Available']
assert not by_phase['before-session']['Available']
assert by_phase['session0-foreground']['ProcessId'] != by_phase['session1-foreground']['ProcessId']
print('PASS: actual desktop tracker stays on its outer display, gates foreground by synthetic outer activation, updates move/resize, rejects unmapped/ambiguous outer windows, and reconnects after real compositor restart.')
print('Tracking-only artifacts:', output)
