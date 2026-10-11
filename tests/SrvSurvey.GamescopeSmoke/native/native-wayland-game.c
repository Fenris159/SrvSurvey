#include <X11/Xatom.h>
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <unistd.h>

static void cardinal(Display *display, Window window, const char *name,
                     unsigned long value) {
    XChangeProperty(display, window, XInternAtom(display, name, False),
                    XA_CARDINAL, 32, PropModeReplace,
                    (unsigned char *)&value, 1);
}

int main(int argc, char **argv) {
    if (argc != 2 || strcmp(argv[1], "--game"))
        return 2;
    setvbuf(stdout, NULL, _IOLBF, 0);
    prctl(PR_SET_NAME, "EliteDangerous6", 0, 0, 0);
    Display *display = XOpenDisplay(NULL);
    if (!display)
        return 3;
    Window window = XCreateSimpleWindow(display, DefaultRootWindow(display),
                                        0, 0, 960, 600, 0, 0, 0xff0000);
    XStoreName(display, window, "Elite - Dangerous (CLIENT) TEST");
    cardinal(display, window, "_NET_WM_PID", getpid());
    cardinal(display, window, "STEAM_GAME", 359320);
    XClassHint klass = {.res_name = "EliteDangerous64.exe",
                       .res_class = "EliteDangerous64.exe"};
    XSetClassHint(display, window, &klass);
    XMapWindow(display, window);
    XFlush(display);
    printf("INNER_GAME pid=%d display=%s\n", getpid(), getenv("DISPLAY"));
    for (;;)
        pause();
}
