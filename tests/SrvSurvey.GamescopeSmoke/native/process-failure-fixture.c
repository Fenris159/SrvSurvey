#include <errno.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/types.h>

/* Link-time wrappers keep fault injection from spawning or signalling real children. */
pid_t __wrap_fork(void) {
    static int ordinal;
    ++ordinal;
    if (ordinal == atoi(getenv("SRVSURVEY_TEST_FAIL_FORK"))) {
        errno = EAGAIN;
        return -1;
    }
    if (ordinal == 3) {
        char path[1024];
        snprintf(path, sizeof path, "%s/before-app.png", getenv("SRVSURVEY_TEST_OUTPUT"));
        FILE *image = fopen(path, "w");
        if (!image)
            exit(97);
        fputs("current-run screenshot fixture", image);
        fclose(image);
    }
    return 10000 + ordinal;
}

int __wrap_kill(pid_t pid, int signal) {
    if (pid <= 0 || signal != SIGTERM)
        exit(99);
    printf("SAFE_STOP %d\n", pid);
    return 0;
}

pid_t __wrap_waitpid(pid_t pid, int *status, int options) {
    (void)options;
    if (pid <= 0)
        exit(98);
    if (status)
        *status = 0;
    return pid;
}
