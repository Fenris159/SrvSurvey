#include <X11/Xlib.h>
#include <X11/Xatom.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <unistd.h>

static void cardinal(Display *display, Window window, const char *name, unsigned long value) {
    XChangeProperty(display, window, XInternAtom(display, name, False), XA_CARDINAL,
                    32, PropModeReplace, (unsigned char *)&value, 1);
}

int main(void) {
    prctl(PR_SET_NAME, "EliteDangerous6");
    Display *game = XOpenDisplay(NULL);
    Display *primary = XOpenDisplay(getenv("PRIMARY_DISPLAY"));
    if (!game || !primary) return 2;
    Window window = XCreateSimpleWindow(game, DefaultRootWindow(game), 0, 0, 960, 600, 0, 0, 0xff0000);
    XStoreName(game, window, "Elite - Dangerous (CLIENT) SCALING TEST");
    cardinal(game, window, "STEAM_GAME", 359320);
    cardinal(game, window, "_NET_WM_PID", getpid());
    cardinal(primary, DefaultRootWindow(primary), "GAMESCOPECTRL_BASELAYER_APPID", 359320);
    XMapWindow(game, window);
    XClearWindow(game, window);
    XSync(game, False);
    XSync(primary, False);
    puts("GAME_READY");
    fflush(stdout);
    char line[128], target[16], command[16];
    unsigned long value;
    while (fgets(line, sizeof line, stdin)) {
        if (sscanf(line, "%15s %15s %lu", target, command, &value) != 3) return 3;
        Display *display = strcmp(target, "primary") == 0 ? primary : game;
        cardinal(display, DefaultRootWindow(display),
                 strcmp(command, "new") == 0 ? "GAMESCOPE_NEW_SCALING_SCALER" : "GAMESCOPE_SCALING_FILTER", value);
        XSync(display, False);
        puts("ACK");
        fflush(stdout);
    }
    XCloseDisplay(game);
    XCloseDisplay(primary);
    return 0;
}
