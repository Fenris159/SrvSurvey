#include "HostFocusPublisher.h"
#include <filesystem>
#include <iostream>
#include <stdexcept>

static void require(bool condition, const char *message) {
    if (!condition) throw std::runtime_error(message);
}

int main(int argc, char **argv) {
    require(argc == 3, "Expected startup/existing and focus state");
    const bool startup = std::string(argv[1]) == "startup";
    const bool focused = std::string(argv[2]) == "1";
    const auto directory = std::filesystem::path(std::getenv("XDG_RUNTIME_DIR"));
    const auto name = "GamescopeGameWindowFocus." + std::to_string(getpid());
    const auto marker = directory / name;
    HostFocusPublisher publisher;
    if (!startup) {
        require(publisher.Publish(false), "Initial publication failed");
        require(publisher.Publish(true), "Foreground publication failed");
    }
    // O_EXCL must fail on the next temporary file, while the final sidecar
    // remains writable. This exercises the real publisher failure branch.
    const auto collision = directory / (name + (startup ? ".tmp.1" : ".tmp.3"));
    std::ofstream(collision) << "reserved\n";
    require(!publisher.Publish(focused), "Expected atomic publication failure");
    require(std::filesystem::exists(marker), "Failed publication deleted the focus capability");
    std::ifstream state(marker);
    std::string start, value;
    std::getline(state, start);
    std::getline(state, value);
    require(!start.empty() && value == "0", "Failed publication did not retain explicit unfocused state");
    struct stat info;
    require(stat(marker.c_str(), &info) == 0 && (info.st_mode & 0777) == 0600,
            "Fallback publication has incorrect permissions");
    require(publisher.Publish(true), "Publication did not recover after failure");
    std::cout << "PASS: failed publication retains stamped false and can recover\n";
}
