#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <wayland-client.h>
#include "gamescope-control-client.h"

static struct gamescope_control *control;
static int complete;
static void feature(void *data, struct gamescope_control *object, uint32_t a, uint32_t b, uint32_t c) {
    (void)data; (void)object; (void)a; (void)b; (void)c;
}
static void active(void *data, struct gamescope_control *object, const char *a, const char *b, const char *c, uint32_t flags, struct wl_array *array) {
    (void)data; (void)object; (void)a; (void)b; (void)c; (void)flags; (void)array;
}
static void taken(void *data, struct gamescope_control *object, const char *path) {
    (void)data; (void)object;
    printf("Screenshot type=2 complete: %s\n", path);
    complete = 1;
}
static const struct gamescope_control_listener listener = {
    .feature_support = feature,
    .active_display_info = active,
    .screenshot_taken = taken,
};
static void global(void *data, struct wl_registry *registry, uint32_t name, const char *interface, uint32_t version) {
    (void)data;
    if (strcmp(interface, "gamescope_control") == 0 && version >= 3) {
        control = wl_registry_bind(registry, name, &gamescope_control_interface, 3);
        gamescope_control_add_listener(control, &listener, NULL);
    }
}
static void removed(void *data, struct wl_registry *registry, uint32_t name) {
    (void)data; (void)registry; (void)name;
}
static const struct wl_registry_listener registry_listener = {.global = global, .global_remove = removed};
int main(int argc, char **argv) {
    if (argc != 2) return 2;
    struct wl_display *display = wl_display_connect(getenv("GAMESCOPE_WAYLAND_DISPLAY"));
    if (!display) return 3;
    struct wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registry_listener, NULL);
    if (wl_display_roundtrip(display) < 0 || !control) return 4;
    gamescope_control_take_screenshot(control, argv[1], GAMESCOPE_CONTROL_SCREENSHOT_TYPE_ALL_REAL_LAYERS, 0);
    while (!complete && wl_display_dispatch(display) >= 0) {}
    gamescope_control_destroy(control);
    wl_registry_destroy(registry);
    wl_display_disconnect(display);
    return complete ? 0 : 5;
}
