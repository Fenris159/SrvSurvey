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

int main(int argc, char **argv) {
    if (argc > 1 && strcmp(argv[1], "--steam") == 0) {
        prctl(PR_SET_NAME, "steam");
        sleep(30);
        return 0;
    }
    prctl(PR_SET_NAME, "EliteDangerous6");
    Display *display = XOpenDisplay(NULL);
    Display *primary = XOpenDisplay(getenv("PRIMARY_DISPLAY"));
    if (!display || !primary) return 2;
    Window window = XCreateSimpleWindow(display, DefaultRootWindow(display), 0, 0, 960, 600, 0, 0, 0xff0000);
    XStoreName(display, window, "Elite - Dangerous (CLIENT) TEST");
    cardinal(display, window, "STEAM_GAME", 359320);
    cardinal(display, window, "_NET_WM_PID", getpid());
    cardinal(primary, DefaultRootWindow(primary), "GAMESCOPECTRL_BASELAYER_APPID", 359320);
    XMapWindow(display, window);
    XClearWindow(display, window);
    XSync(display, False);
    XSync(primary, False);
    printf("GAME=0x%lx DISPLAY=%s\n", window, getenv("DISPLAY"));
    fflush(stdout);
    sleep(25);
    XCloseDisplay(display);
    XCloseDisplay(primary);
    return 0;
}
