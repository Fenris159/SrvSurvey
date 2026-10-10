#include <X11/Xatom.h>
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/extensions/Xrender.h>
#include <X11/extensions/shape.h>
#include <fcntl.h>
#include <libei.h>
#include <linux/input-event-codes.h>
#include <poll.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>

static void cardinal(Display *d, Window w, const char *name,
                     unsigned long value) {
    XChangeProperty(d, w, XInternAtom(d, name, False), XA_CARDINAL, 32,
                    PropModeReplace, (unsigned char *)&value, 1);
}
static Window argb(Display *d, const char *name, Picture *picture) {
    XVisualInfo vi;
    if (!XMatchVisualInfo(d, DefaultScreen(d), 32, TrueColor, &vi))
        exit(3);
    Window root = DefaultRootWindow(d);
    XSetWindowAttributes a = {0};
    a.colormap = XCreateColormap(d, root, vi.visual, AllocNone);
    Window w =
        XCreateWindow(d, root, 0, 0, 1280, 800, 0, 32, InputOutput, vi.visual,
                      CWColormap | CWBorderPixel | CWBackPixel, &a);
    XStoreName(d, w, name);
    XSelectInput(d, w,
                 ButtonPressMask | ButtonReleaseMask | KeyPressMask |
                     KeyReleaseMask | PointerMotionMask);
    *picture = XRenderCreatePicture(d, w, XRenderFindVisualFormat(d, vi.visual),
                                    0, NULL);
    return w;
}
static void paint(Display *d, Picture p, int marker) {
    XRenderColor clear = {0, 0, 0, 0}, green = {0, 65535, 0, 65535};
    XRenderFillRectangle(d, PictOpSrc, p, &clear, 0, 0, 1280, 800);
    if (marker)
        XRenderFillRectangle(d, PictOpSrc, p, &green, 16, 16, 64, 64);
}
static int shot(const char *path) {
    unlink(path);
    pid_t pid = fork();
    if (pid == 0) {
        const char *client = getenv("SCREENSHOT_CLIENT");
        if (client)
            execl(client, "screenshot-client", path, NULL);
        _exit(125);
    }
    int status;
    waitpid(pid, &status, 0);
    if (!WIFEXITED(status) || WEXITSTATUS(status))
        return 1;
    for (int i = 0; i < 150; i++) {
        struct stat st;
        if (!stat(path, &st) && st.st_size > 0)
            return 0;
        usleep(100000);
    }
    return 1;
}
static void info(Display *d, Window own, Window foreign) {
    const char *names[] = {
        "GAMESCOPE_FOCUSED_APP_GFX",        "GAMESCOPE_FOCUSED_APP",
        "GAMESCOPE_FOCUSED_WINDOW",         "GAMESCOPE_MOUSE_FOCUS_DISPLAY",
        "GAMESCOPE_KEYBOARD_FOCUS_DISPLAY", NULL};
    for (int i = 0; names[i]; i++) {
        Atom type;
        int format;
        unsigned long count, remaining;
        unsigned char *data = NULL;
        XGetWindowProperty(
            d, DefaultRootWindow(d), XInternAtom(d, names[i], False), 0, 64,
            False, AnyPropertyType, &type, &format, &count, &remaining, &data);
        printf("%s=", names[i]);
        if (format == 32)
            for (unsigned long n = 0; n < count; n++)
                printf("%08lx ", ((unsigned long *)data)[n]);
        puts("");
        if (data)
            XFree(data);
    }
    Window root, parent, *children = NULL;
    unsigned int count = 0;
    if (XQueryTree(d, DefaultRootWindow(d), &root, &parent, &children,
                   &count)) {
        printf("ROOT_STACK_BOTTOM_TO_TOP=");
        for (unsigned int i = 0; i < count; i++)
            printf("0x%lx%s ", children[i],
                   children[i] == own       ? "(ours)"
                   : children[i] == foreign ? "(foreign)"
                                            : "");
        puts("");
        if (children)
            XFree(children);
    }
    const char *ctl = getenv("GAMESCOPECTL");
    if (ctl) {
        pid_t child = fork();
        if (child == 0) {
            execl(ctl, "gamescopectl", "focus_info", NULL);
            _exit(125);
        }
        int status;
        waitpid(child, &status, 0);
    }
}
static void events(Display *d, const char *label) {
    XSync(d, False);
    while (XPending(d)) {
        XEvent e;
        XNextEvent(d, &e);
        if (e.type == ButtonPress || e.type == ButtonRelease)
            printf("EVENT %s button=%u type=%d window=0x%lx x=%d y=%d\n", label,
                   e.xbutton.button, e.type, e.xbutton.window, e.xbutton.x,
                   e.xbutton.y);
        if (e.type == KeyPress || e.type == KeyRelease)
            printf("EVENT %s keycode=%u type=%d window=0x%lx\n", label,
                   e.xkey.keycode, e.type, e.xkey.window);
    }
}
static struct ei *input_context;
static struct ei_device *input_device;
static void dispatch_input(void) {
    ei_dispatch(input_context);
    struct ei_event *e;
    while ((e = ei_get_event(input_context))) {
        enum ei_event_type type = ei_event_get_type(e);
        if (type == EI_EVENT_SEAT_ADDED)
            ei_seat_bind_capabilities(
                ei_event_get_seat(e), EI_DEVICE_CAP_POINTER_ABSOLUTE,
                EI_DEVICE_CAP_BUTTON, EI_DEVICE_CAP_KEYBOARD, NULL);
        if (type == EI_EVENT_DEVICE_RESUMED && !input_device) {
            input_device = ei_device_ref(ei_event_get_device(e));
            ei_device_start_emulating(input_device, 1);
        }
        if (type == EI_EVENT_DISCONNECT)
            puts("EI_DISCONNECTED");
        ei_event_unref(e);
    }
}
static void input_wait(int ms) {
    struct pollfd fd = {.fd = ei_get_fd(input_context), .events = POLLIN};
    poll(&fd, 1, ms);
    dispatch_input();
}
static void inject(void) {
    if (!input_context) {
        input_context = ei_new_sender(NULL);
        if (ei_setup_backend_socket(input_context, getenv("LIBEI_SOCKET")) !=
            0) {
            puts("EI_SETUP_FAILED");
            return;
        }
        for (int i = 0; i < 30 && !input_device; i++)
            input_wait(100);
    }
    if (!input_device) {
        puts("EI_NO_DEVICE");
        return;
    }
    for (int p = 0; p < 2; p++) {
        int xy = p ? 200 : 24;
        ei_device_pointer_motion_absolute(input_device, xy, xy);
        ei_device_frame(input_device, ei_now(input_context));
        input_wait(150);
        ei_device_button_button(input_device, BTN_LEFT, true);
        ei_device_frame(input_device, ei_now(input_context));
        input_wait(100);
        ei_device_button_button(input_device, BTN_LEFT, false);
        ei_device_frame(input_device, ei_now(input_context));
        input_wait(100);
    }
    ei_device_keyboard_key(input_device, KEY_F8, true);
    ei_device_frame(input_device, ei_now(input_context));
    input_wait(100);
    ei_device_keyboard_key(input_device, KEY_F8, false);
    ei_device_frame(input_device, ei_now(input_context));
    input_wait(100);
}

static double monotonic(void) {
    struct timespec t;
    clock_gettime(CLOCK_MONOTONIC, &t);
    return t.tv_sec + t.tv_nsec / 1000000000.0;
}
static const char *output_directory;
static char *read_log(void) {
    char path[512];
    snprintf(path, sizeof path, "%s/avalonia.log", output_directory);
    FILE *f = fopen(path, "r");
    if (!f)
        return NULL;
    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    rewind(f);
    char *s = calloc((size_t)size + 1, 1);
    if (s) {
        size_t read = fread(s, 1, (size_t)size, f);
        s[read] = 0;
    }
    fclose(f);
    return s;
}
static int await_log(const char *text, double timeout) {
    double end = monotonic() + timeout;
    while (monotonic() < end) {
        char *log = read_log();
        int found = log && strstr(log, text);
        free(log);
        if (found) {
            printf("SAW_LOG %s\n", text);
            fflush(stdout);
            return 1;
        }
        usleep(30000);
    }
    fprintf(stderr, "Missing log marker: %s\n", text);
    return 0;
}
static unsigned long number(Display *d, Window w, const char *name) {
    Atom type;
    int format;
    unsigned long count, left;
    unsigned char *data = NULL;
    unsigned long value = 0;
    XGetWindowProperty(d, w, XInternAtom(d, name, False), 0, 2, False,
                       AnyPropertyType, &type, &format, &count, &left, &data);
    if (format == 32 && count == 1)
        value = ((unsigned long *)data)[0];
    if (data)
        XFree(data);
    return value;
}
static void native_windows(Display *d, Window foreign) {
    Window root = DefaultRootWindow(d), parent, *children = NULL;
    unsigned int count = 0;
    XQueryTree(d, root, &root, &parent, &children, &count);
    for (unsigned int i = 0; i < count; i++) {
        Window w = children[i];
        if (w == foreign)
            continue;
        unsigned long ext = number(d, w, "GAMESCOPE_EXTERNAL_OVERLAY"),
                      over = number(d, w, "STEAM_OVERLAY"),
                      focus = number(d, w, "STEAM_INPUT_FOCUS");
        if (ext || over) {
            XWindowAttributes a;
            XGetWindowAttributes(d, w, &a);
            int rectangles = 0, order = 0;
            XRectangle *shape =
                XShapeGetRectangles(d, w, ShapeInput, &rectangles, &order);
            printf("AV_NATIVE window=0x%lx external=%lu overlay=%lu focus=%lu "
                   "mapped=%d size=%dx%d shapeRectangles=%d",
                   w, ext, over, focus, a.map_state, a.width, a.height,
                   rectangles);
            for (int k = 0; k < rectangles; k++)
                printf(" shape=%d,%d,%u,%u", shape[k].x, shape[k].y,
                       shape[k].width, shape[k].height);
            puts("");
            if (shape)
                XFree(shape);
        }
    }
    if (children)
        XFree(children);
}
static void phase(Display *d, Window foreign, const char *name) {
    printf("\nPHASE %s\n", name);
    fflush(stdout);
    native_windows(d, foreign);
    info(d, None, foreign);
    inject();
    events(d, "primary");
    usleep(200000);
    fflush(stdout);
    char path[512];
    snprintf(path, sizeof path, "%s/%s.png", output_directory, name);
    if (shot(path))
        exit(7);
}
static int game_main(void) {
    prctl(PR_SET_NAME, "EliteDangerous6");
    Display *d = XOpenDisplay(NULL),
            *primary = XOpenDisplay(getenv("PRIMARY_DISPLAY"));
    if (!d || !primary)
        return 2;
    Window game = XCreateSimpleWindow(d, DefaultRootWindow(d), 0, 0, 960, 600,
                                      0, 0, 0xff0000);
    XStoreName(d, game, "Elite - Dangerous (CLIENT) TEST");
    cardinal(d, game, "STEAM_GAME", 359320);
    cardinal(d, game, "_NET_WM_PID", (unsigned long)getpid());
    cardinal(primary, DefaultRootWindow(primary),
             "GAMESCOPECTRL_BASELAYER_APPID", 359320);
    XSelectInput(d, game,
                 ButtonPressMask | ButtonReleaseMask | KeyPressMask |
                     KeyReleaseMask);
    XMapWindow(d, game);
    XClearWindow(d, game);
    XSync(d, False);
    XSync(primary, False);
    printf("GAME_READY GAME=0x%lx DISPLAY=%s PID=%d\n", game, getenv("DISPLAY"),
           getpid());
    fflush(stdout);
    double end = monotonic() + 35;
    while (monotonic() < end) {
        events(d, "game");
        fflush(stdout);
        usleep(15000);
    }
    XCloseDisplay(d);
    XCloseDisplay(primary);
    return 0;
}
int main(int argc, char **argv) {
    if (argc > 1 && !strcmp(argv[1], "--steam")) {
        prctl(PR_SET_NAME, "steam");
        sleep(35);
        return 0;
    }
    if (argc > 1 && !strcmp(argv[1], "--game"))
        return game_main();
    if (argc < 4) {
        fprintf(stderr, "Usage:driver DLL OUTPUT from-game\n");
        return 2;
    }
    const char *dll = argv[1];
    output_directory = argv[2];
    int from_game = atoi(argv[3]);
    const char *primary = getenv("DISPLAY"),
               *game_display = getenv("STEAM_GAME_DISPLAY_0");
    char executable[512];
    snprintf(executable, sizeof executable, "%s/EliteDangerous64.exe",
             output_directory);
    pid_t game = fork();
    if (!game) {
        setenv("PRIMARY_DISPLAY", primary, 1);
        setenv("DISPLAY", game_display, 1);
        execl(executable, executable, "--game", NULL);
        _exit(125);
    }
    snprintf(executable, sizeof executable, "%s/steam", output_directory);
    pid_t steam = fork();
    if (!steam) {
        execl(executable, executable, "--steam", NULL);
        _exit(125);
    }
    Display *d = XOpenDisplay(primary);
    if (!d)
        return 2;
    Picture foreign_picture;
    Window foreign = argb(d, "Simulated full-opacity inactive Steam overlay",
                          &foreign_picture);
    cardinal(d, foreign, "STEAM_OVERLAY", 1);
    cardinal(d, foreign, "STEAM_INPUT_FOCUS", 0);
    cardinal(d, foreign, "_NET_WM_WINDOW_OPACITY", 0xffffffffUL);
    XMapWindow(d, foreign);
    paint(d, foreign_picture, 0);
    XSync(d, False);
    sleep(1);
    printf("HOST PRIMARY=%s GAME=%s FOREIGN=0x%lx FROM_GAME=%d\n", primary,
           game_display, foreign, from_game);
    fflush(stdout);
    char path[512];
    snprintf(path, sizeof path, "%s/before-app.png", output_directory);
    if (shot(path))
        return 7;
    pid_t app = fork();
    if (!app) {
        snprintf(path, sizeof path, "%s/avalonia.log", output_directory);
        int log = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0600);
        dup2(log, STDOUT_FILENO);
        dup2(log, STDERR_FILENO);
        close(log);
        if (from_game)
            setenv("DISPLAY", game_display, 1);
        execlp("dotnet", "dotnet", dll, "--interactive", NULL);
        _exit(125);
    }
    int result = 0;
    if (!await_log("GAME visible=", 6)) {
        result = 3;
        goto cleanup;
    }
    phase(d, foreign, "passive");
    if (!await_log("LIVE on", 6)) {
        result = 3;
        goto cleanup;
    }
    usleep(150000);
    phase(d, foreign, "live");
    cardinal(d, foreign, "STEAM_INPUT_FOCUS", 1);
    XSync(d, False);
    usleep(200000);
    phase(d, foreign, "steam-yield");
    cardinal(d, foreign, "STEAM_INPUT_FOCUS", 0);
    XSync(d, False);
    usleep(200000);
    phase(d, foreign, "reacquired");
    if (!await_log("LIVE off", 12)) {
        result = 3;
        goto cleanup;
    }
    usleep(200000);
    phase(d, foreign, "after-off");
    if (!await_log("SOURCE hidden", 6)) {
        result = 3;
        goto cleanup;
    }
    usleep(150000);
    printf("\nPHASE source-hidden\n");
    native_windows(d, foreign);
    info(d, None, foreign);
    fflush(stdout);
    snprintf(path, sizeof path, "%s/source-hidden.png", output_directory);
    if (shot(path)) {
        result = 7;
        goto cleanup;
    }
    if (!await_log("SOURCE shown", 6)) {
        result = 3;
        goto cleanup;
    }
    usleep(200000);
    phase(d, foreign, "source-shown");
    {
        int status = 0;
        double end = monotonic() + 10;
        while (monotonic() < end) {
            if (waitpid(app, &status, WNOHANG) == app) {
                app = 0;
                result = WIFEXITED(status) ? WEXITSTATUS(status) : 8;
                break;
            }
            usleep(50000);
        }
        if (app)
            result = 9;
    }
cleanup:
    if (!result && !app)
        phase(d, foreign, "disposed");
    printf("PRODUCTION_RESULT %d\n", result);
    fflush(stdout);
    if (app) {
        kill(app, SIGTERM);
        waitpid(app, NULL, 0);
    }
    kill(game, SIGTERM);
    kill(steam, SIGTERM);
    waitpid(game, NULL, 0);
    waitpid(steam, NULL, 0);
    XRenderFreePicture(d, foreign_picture);
    XDestroyWindow(d, foreign);
    XCloseDisplay(d);
    return result;
}
