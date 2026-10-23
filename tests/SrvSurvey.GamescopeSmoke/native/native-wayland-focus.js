import GLib from 'gi://GLib';
import Gio from 'gi://Gio';
import Clutter from 'gi://Clutter';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
export const METRICS = {};
let sidecar = null;
let startTicks = null;
function delay(ms) { return new Promise(resolve => GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => { resolve(); return GLib.SOURCE_REMOVE; })); }
function focusValue() { return GLib.getenv('FOCUS_EXPECT_SIDECAR') === '1' ? contents(sidecar).trim().split('\n')[1] || '' : contents(GLib.getenv('ELITE_HOST_FOCUS_FILE')).trim(); }
function contents(path) { try { return new TextDecoder().decode(GLib.file_get_contents(path)[1]); } catch { return ''; } }
function spawn(argv, logname) {
    const launcher = new Gio.SubprocessLauncher({flags: Gio.SubprocessFlags.NONE});
    launcher.set_stdout_file_path(GLib.getenv('FOCUS_OUTPUT') + '/' + logname + '.log');
    launcher.set_stderr_file_path(GLib.getenv('FOCUS_OUTPUT') + '/' + logname + '.err');
    return launcher.spawnv(argv);
}
async function windowWith(predicate) {
    for (let i = 0; i < 600; i++) {
        const w = global.display.list_all_windows().find(predicate);
        if (w) return w;
        await delay(100);
    }
    throw Error('Timed out waiting for window');
}
async function sampleTracker(label) {
    if (GLib.getenv("FOCUS_USE_TRACKER") !== "1") return;
    const output = GLib.getenv("FOCUS_OUTPUT");
    GLib.file_set_contents(output + "/tracker.request", label);
    for (let i=0; i<150; i++) {
        const reply = contents(output + "/tracker.reply").trim();
        if (reply === label) { print("TRACKER_ACK " + label); return; }
        if (reply.startsWith("ERROR")) throw Error(reply);
        await delay(100);
    }
    throw Error("Tracker timeout " + label);
}
async function expectFocus(label, expected) {
    for (let i = 0; i < 100; i++) {
        let value = focusValue();
        if (value === expected) {
            print(`ACTUAL_NATIVE_FOCUS ${label}=${value}`);
            if (GLib.getenv("FOCUS_EXPECT_SIDECAR") === "1") {
                const stamped = contents(sidecar).trim().split("\n");
                if (stamped.length !== 2 || stamped[0] !== startTicks || stamped[1] !== expected) throw Error("Invalid sidecar " + contents(sidecar));
                print(`SIDECAR ${label} start=${stamped[0]} focus=${stamped[1]}`);
                GLib.file_set_contents(GLib.getenv("FOCUS_OUTPUT") + "/sidecar-" + label, contents(sidecar));
            }
            await sampleTracker(label);
            return;
        }
        await delay(100);
    }
    throw Error(`Focus ${label}: expected ${expected}, saw ${focusValue()}`);
}
export async function run() {
    const keyboard = global.stage.context.get_backend().get_default_seat().create_virtual_device(Clutter.InputDeviceType.KEYBOARD_DEVICE);
    keyboard.notify_keyval(GLib.get_monotonic_time(), Clutter.KEY_F8, Clutter.KeyState.PRESSED);
    keyboard.notify_keyval(GLib.get_monotonic_time(), Clutter.KEY_F8, Clutter.KeyState.RELEASED);
    Main.overview.hide();
    await delay(1000);
    const scope = spawn([GLib.getenv('FOCUS_INNER'), '--backend', 'wayland', '-b', '-f', '-W', '1280', '-H', '800', '-w', '960', '-h', '600', '--', GLib.getenv('FOCUS_GAME'), '--game'], 'inner');
    print('NATIVE_SCOPE_PID=' + scope.get_identifier());
    sidecar = GLib.getenv('XDG_RUNTIME_DIR') + '/GamescopeGameWindowFocus.' + scope.get_identifier();
    const stat = contents('/proc/' + scope.get_identifier() + '/stat');
    startTicks = stat.slice(stat.lastIndexOf(')') + 1).trim().split(/\s+/)[19];
    if (GLib.getenv('FOCUS_EXPECT_SIDECAR') === '1') {
        for (let i=0; i<150; i++) {
            const initial=contents(sidecar).trim().split('\n');
            if (initial.length===2) {
                if (initial[0]!==startTicks || initial[1]!=='0') throw Error('Initial native focus should be false: '+contents(sidecar));
                print('SIDECAR initial start='+initial[0]+' focus=0');
                break;
            }
            if (i===149) throw Error('No initial sidecar');
            await delay(20);
        }
    }
    const game = await windowWith(w => (w.get_wm_class() || '').toLowerCase().includes('gamescope') || (w.get_title() || '').toLowerCase().includes('gamescope'));
    print('NATIVE_WINDOW title=' + game.get_title() + ' class=' + game.get_wm_class());
    let gameDisplay = null;
    for (let i=0; i<150; i++) {
        const match = contents(GLib.getenv('FOCUS_OUTPUT') + '/inner.log').match(/INNER_GAME pid=\d+ display=([^\s]+)/);
        if (match) { gameDisplay=match[1]; break; }
        await delay(100);
    }
    if (!gameDisplay) throw Error('No inner game display');
    GLib.file_set_contents(GLib.getenv('FOCUS_OUTPUT') + '/session.json', JSON.stringify({pid:scope.get_identifier(), start:startTicks, display:gameDisplay}));
    game.activate(global.get_current_time());
    print("HOST_FOCUSED_WINDOW=" + (global.display.focus_window?.get_title() || "none"));
    await expectFocus('enter', '1');
    const competitor = spawn(['zenity', '--info', '--title=SrvSurvey private focus competitor', '--text=Private headless test surface'], 'competitor');
    const other = await windowWith(w => (w.get_title() || '').includes('SrvSurvey private focus competitor'));
    other.activate(global.get_current_time());
    await expectFocus('leave', '0');
    game.activate(global.get_current_time());
    await expectFocus('return', '1');
    competitor.force_exit();
    scope.send_signal(15);
    await delay(3000);
    if (GLib.getenv('FOCUS_EXPECT_SIDECAR') === '1') {
        print('SIDECAR after-stop exists=' + GLib.file_test(sidecar, GLib.FileTest.EXISTS));
        if (GLib.file_test(sidecar, GLib.FileTest.EXISTS)) throw Error('Sidecar not cleaned up');
    }
    scope.force_exit();
    await sampleTracker('after-stop');
    print('NATIVE_WAYLAND_PROTOCOL_PASS');
}
