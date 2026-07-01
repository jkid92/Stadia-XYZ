#include <arpa/inet.h>
#include <sys/socket.h>
#include <linux/hidraw.h>
#include <sys/ioctl.h>
#include <fcntl.h>
#include <unistd.h>
#include <thread>
#include <cstring>
#include <algorithm>
#include <array>
#include <atomic>
#include <cctype>
#include <cstdarg>
#include <cerrno>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <dirent.h>
#include <filesystem>
#include <functional>
#include <linux/input.h>
#include <mutex>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <optional>
#include <poll.h>
#include <signal.h>
#include <sstream>
#include <string>
#include <sys/stat.h>
#include <sys/types.h>
#include <vector>

#pragma pack(push, 1)
struct ControllerState {
    uint16_t buttons;
    uint8_t  trigger_left;
    uint8_t  trigger_right;
    int16_t  stick_lx;
    int16_t  stick_ly;
    int16_t  stick_rx;
    int16_t  stick_ry;
};
struct RumbleState {
    uint8_t motor_left;
    uint8_t motor_right;
};
struct InputPacket {
    uint8_t magic;
    uint8_t version;
    uint8_t controller_index;
    uint8_t reserved;
    ControllerState state;
};
struct RumblePacket {
    uint8_t magic;
    uint8_t version;
    uint8_t controller_index;
    uint8_t reserved;
    RumbleState rumble;
};
#pragma pack(pop)

enum ButtonBit : uint16_t {
    BTN_BIT_A           = 1 << 0,
    BTN_BIT_B           = 1 << 1,
    BTN_BIT_X           = 1 << 2,
    BTN_BIT_Y           = 1 << 3,
    BTN_BIT_LB          = 1 << 4,
    BTN_BIT_RB          = 1 << 5,
    BTN_BIT_SELECT      = 1 << 6,
    BTN_BIT_START       = 1 << 7,
    BTN_BIT_STADIA      = 1 << 8,
    BTN_BIT_L3          = 1 << 9,
    BTN_BIT_R3          = 1 << 10,
    BTN_BIT_ASSISTANT   = 1 << 11,
    BTN_BIT_DPAD_UP     = 1 << 12,
    BTN_BIT_DPAD_DOWN   = 1 << 13,
    BTN_BIT_DPAD_LEFT   = 1 << 14,
    BTN_BIT_DPAD_RIGHT  = 1 << 15,
};

// Mask of all digital buttons that should be SUPPRESSED from gamepad output when a modifier is held
static constexpr uint16_t CHORD_SUPPRESS_MASK =
    BTN_BIT_A | BTN_BIT_B | BTN_BIT_X | BTN_BIT_Y |
    BTN_BIT_DPAD_UP | BTN_BIT_DPAD_DOWN | BTN_BIT_DPAD_LEFT | BTN_BIT_DPAD_RIGHT |
    BTN_BIT_LB | BTN_BIT_RB | BTN_BIT_L3 | BTN_BIT_R3 |
    BTN_BIT_SELECT | BTN_BIT_START | BTN_BIT_STADIA;

static constexpr uint16_t PORT_INPUT  = 45493;
static constexpr uint16_t PORT_RUMBLE = 45494;
static constexpr uint16_t PORT_C2     = 45495;
static constexpr uint16_t PORT_MACRO  = 45499;
static constexpr uint8_t PACKET_MAGIC = 0x53; // 'S'
static constexpr uint8_t PACKET_VERSION = 1;
static constexpr int MAX_CONTROLLERS = 4;

static std::atomic<bool> g_running{true};
static std::atomic<int>  g_evdev_fd{-1};
static std::mutex        g_state_mtx;
static ControllerState   g_state{};
static std::mutex        g_raw_state_mtx;
static ControllerState   g_raw_state{};

static std::atomic<bool> g_assistant_held{false};
static std::atomic<bool> g_capture_held{false};

static std::string       g_target_ip;
static std::mutex        g_controller_mtx;
static std::string       g_controller_path;
static std::string       g_controller_name;
static std::atomic<bool> g_controller_connected{false};
static std::mutex              g_wake_mtx;
static std::condition_variable g_wake_cv;

struct ControllerRuntime {
    std::atomic<int> evdev_fd{-1};
    std::mutex state_mtx;
    std::mutex meta_mtx;
    std::mutex rumble_mtx;
    ControllerState state{};
    std::string path;
    std::string name;
    std::string rumble_hidraw_path;
    int rumble_hid_fd{-1};
    int ff_effect_id{-1};
    bool native_ff_available{false};
    bool rumble_warning_logged{false};
    uint8_t last_rumble_strong{0};
    uint8_t last_rumble_weak{0};
    std::chrono::steady_clock::time_point last_rumble_sent{};
    std::atomic<bool> connected{false};
};

struct StadiaDeviceInfo {
    std::string path;
    std::string name;
};

struct HidrawDeviceInfo {
    std::string path;
    std::string sysfs_key;
};

enum class RumbleBackendPreference {
    Auto,
    Hidraw,
    Evdev,
    Off
};

static std::array<ControllerRuntime, MAX_CONTROLLERS> g_controllers;

static std::string shell_exec(const std::string& cmd) {
    std::string result; FILE* pipe = popen(cmd.c_str(), "r");
    if (!pipe) return result; char buf[512];
    while (fgets(buf, sizeof(buf), pipe)) result += buf;
    pclose(pipe);
    while (!result.empty() && (result.back() == '\n' || result.back() == '\r')) result.pop_back();
    return result;
}

static bool is_valid_bluetooth_mac(const std::string& mac) {
    if (mac.size() != 17) return false;
    for (size_t i = 0; i < mac.size(); ++i) {
        if ((i + 1) % 3 == 0) {
            if (mac[i] != ':') return false;
        } else if (!std::isxdigit(static_cast<unsigned char>(mac[i]))) {
            return false;
        }
    }
    return true;
}

static std::mutex g_log_mtx;
static void log_info(const char* fmt, ...) {
    std::lock_guard<std::mutex> lk(g_log_mtx); va_list ap; va_start(ap, fmt);
    fprintf(stdout, "[INFO]  "); vfprintf(stdout, fmt, ap); fprintf(stdout, "\n"); fflush(stdout); va_end(ap);
}
static void log_err(const char* fmt, ...) {
    std::lock_guard<std::mutex> lk(g_log_mtx); va_list ap; va_start(ap, fmt);
    fprintf(stderr, "[ERROR] "); vfprintf(stderr, fmt, ap); fprintf(stderr, "\n"); fflush(stderr); va_end(ap);
}
static void signal_handler(int) { g_running = false; g_wake_cv.notify_all(); }

static int16_t scale_stick(int raw) {
    int centered = raw - 128;
    int scaled = (centered * 32767) / 127;
    return static_cast<int16_t>(std::clamp(scaled, -32767, 32767));
}

static uint16_t keycode_to_bit(uint16_t code) {
    switch (code) {
        case BTN_SOUTH: return BTN_BIT_A; case BTN_EAST: return BTN_BIT_B;
        case BTN_NORTH: return BTN_BIT_X; case BTN_WEST: return BTN_BIT_Y;
        case BTN_TL: return BTN_BIT_LB; case BTN_TR: return BTN_BIT_RB;
        case BTN_SELECT: return BTN_BIT_SELECT; case BTN_START: return BTN_BIT_START;
        case BTN_MODE: return BTN_BIT_STADIA; case BTN_THUMBL: return BTN_BIT_L3;
        case BTN_THUMBR: return BTN_BIT_R3; case BTN_TRIGGER_HAPPY1: return BTN_BIT_ASSISTANT;
        default: return 0;
    }
}

static void apply_event(const struct input_event& ev, ControllerState& st) {
    if (ev.type == EV_KEY) {
        uint16_t bit = keycode_to_bit(ev.code);
        if (bit) { if (ev.value) st.buttons |= bit; else st.buttons &= ~bit; }
    } else if (ev.type == EV_ABS) {
        switch (ev.code) {
            case ABS_X: st.stick_lx = scale_stick(ev.value); break;
            case ABS_Y: st.stick_ly = scale_stick(ev.value); break;
            case ABS_Z: st.stick_rx = scale_stick(ev.value); break;
            case ABS_RZ: st.stick_ry = scale_stick(ev.value); break;
            case ABS_BRAKE: st.trigger_left = static_cast<uint8_t>(std::clamp(ev.value, 0, 255)); break;
            case ABS_GAS: st.trigger_right = static_cast<uint8_t>(std::clamp(ev.value, 0, 255)); break;
            case ABS_HAT0X:
                st.buttons &= ~(BTN_BIT_DPAD_LEFT | BTN_BIT_DPAD_RIGHT);
                if (ev.value < 0) st.buttons |= BTN_BIT_DPAD_LEFT;
                else if (ev.value > 0) st.buttons |= BTN_BIT_DPAD_RIGHT; break;
            case ABS_HAT0Y:
                st.buttons &= ~(BTN_BIT_DPAD_UP | BTN_BIT_DPAD_DOWN);
                if (ev.value < 0) st.buttons |= BTN_BIT_DPAD_UP;
                else if (ev.value > 0) st.buttons |= BTN_BIT_DPAD_DOWN; break;
        }
    }
}

static constexpr uint16_t STADIA_VID = 0x18d1;
static constexpr uint16_t STADIA_PID = 0x9400;

static std::string find_stadia_evdev() {
    namespace fs = std::filesystem;
    if (!fs::exists("/dev/input/")) return {};
    for (auto& entry : fs::directory_iterator("/dev/input/")) {
        std::string path = entry.path().string();
        if (path.find("event") == std::string::npos) continue;
        int fd = open(path.c_str(), O_RDONLY | O_NONBLOCK);
        if (fd < 0) continue;
        struct input_id id{};
        if (ioctl(fd, EVIOCGID, &id) == 0) {
            if (id.vendor == STADIA_VID && id.product == STADIA_PID) {
                char name[256] = {}; ioctl(fd, EVIOCGNAME(sizeof(name)), name);
                close(fd);
                log_info("Found Stadia controller: %s [%s]", name, path.c_str());
                { std::lock_guard<std::mutex> lk(g_controller_mtx); g_controller_name = name; }
                return path;
            }
        }
        close(fd);
    }
    return {};
}

static std::vector<StadiaDeviceInfo> find_stadia_evdevs() {
    namespace fs = std::filesystem;
    std::vector<StadiaDeviceInfo> devices;
    if (!fs::exists("/dev/input/")) return devices;

    for (auto& entry : fs::directory_iterator("/dev/input/")) {
        std::string path = entry.path().string();
        if (path.find("event") == std::string::npos) continue;

        int fd = open(path.c_str(), O_RDONLY | O_NONBLOCK);
        if (fd < 0) continue;

        struct input_id id{};
        if (ioctl(fd, EVIOCGID, &id) == 0) {
            if (id.vendor == STADIA_VID && id.product == STADIA_PID) {
                char name[256] = {};
                ioctl(fd, EVIOCGNAME(sizeof(name)), name);
                devices.push_back({path, name[0] ? name : "Stadia Controller"});
            }
        }
        close(fd);
    }

    std::sort(devices.begin(), devices.end(), [](const StadiaDeviceInfo& a, const StadiaDeviceInfo& b) {
        return a.path < b.path;
    });
    return devices;
}

static bool rumble_enabled() {
    const char* enable_rumble = std::getenv("STADIA_X_ENABLE_RUMBLE");
    return enable_rumble != nullptr && std::strcmp(enable_rumble, "1") == 0;
}

static std::string upper_ascii(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) {
        return static_cast<char>(std::toupper(c));
    });
    return value;
}

static RumbleBackendPreference rumble_backend_preference() {
    const char* value = std::getenv("STADIA_X_RUMBLE_BACKEND");
    if (value == nullptr) return RumbleBackendPreference::Auto;
    std::string text = upper_ascii(value);
    if (text == "HIDRAW" || text == "RAW") return RumbleBackendPreference::Hidraw;
    if (text == "EVDEV" || text == "FF" || text == "NATIVE") return RumbleBackendPreference::Evdev;
    if (text == "OFF" || text == "0" || text == "FALSE") return RumbleBackendPreference::Off;
    return RumbleBackendPreference::Auto;
}

static int rumble_dedupe_ms() {
    const char* value = std::getenv("STADIA_X_RUMBLE_DEDUPE_MS");
    if (value == nullptr || *value == '\0') return 4;
    char* end = nullptr;
    long parsed = std::strtol(value, &end, 10);
    if (end == value) return 4;
    return static_cast<int>(std::clamp(parsed, 0L, 25L));
}

static bool sysfs_component_is_stadia_key(const std::string& component) {
    const std::string upper = upper_ascii(component);
    return upper.find(":18D1:9400") != std::string::npos;
}

static std::string stadia_sysfs_key_from_class_device(const std::string& class_device_path) {
    namespace fs = std::filesystem;
    std::error_code ec;
    fs::path resolved = fs::weakly_canonical(class_device_path, ec);
    if (ec) return {};

    for (const auto& part : resolved) {
        const std::string component = part.string();
        if (sysfs_component_is_stadia_key(component)) {
            return upper_ascii(component);
        }
    }

    return {};
}

static std::string sysfs_key_for_evdev(const std::string& evpath) {
    namespace fs = std::filesystem;
    const std::string event_name = fs::path(evpath).filename().string();
    if (event_name.empty()) return {};
    return stadia_sysfs_key_from_class_device("/sys/class/input/" + event_name + "/device");
}

static std::string sysfs_key_for_hidraw(const std::string& hidraw_path) {
    namespace fs = std::filesystem;
    const std::string hidraw_name = fs::path(hidraw_path).filename().string();
    if (hidraw_name.empty()) return {};
    return stadia_sysfs_key_from_class_device("/sys/class/hidraw/" + hidraw_name + "/device");
}

static int open_stadia_hidraw(const std::string& path) {
    int fd = open(path.c_str(), O_RDWR | O_NONBLOCK | O_CLOEXEC);
    if (fd < 0) return -1;

    struct hidraw_devinfo info {};
    if (ioctl(fd, HIDIOCGRAWINFO, &info) != 0 ||
        static_cast<uint16_t>(info.vendor) != STADIA_VID ||
        static_cast<uint16_t>(info.product) != STADIA_PID) {
        close(fd);
        return -1;
    }

    return fd;
}

static std::vector<HidrawDeviceInfo> find_stadia_hidraws() {
    namespace fs = std::filesystem;
    std::vector<HidrawDeviceInfo> devices;
    std::error_code ec;
    if (!fs::exists("/sys/class/hidraw", ec)) return devices;

    for (const auto& entry : fs::directory_iterator("/sys/class/hidraw", ec)) {
        if (ec) break;
        const std::string name = entry.path().filename().string();
        if (name.empty()) continue;

        const std::string path = "/dev/" + name;
        int fd = open_stadia_hidraw(path);
        if (fd < 0) continue;
        close(fd);

        devices.push_back({path, sysfs_key_for_hidraw(path)});
    }

    std::sort(devices.begin(), devices.end(), [](const HidrawDeviceInfo& a, const HidrawDeviceInfo& b) {
        return a.path < b.path;
    });
    return devices;
}

static std::optional<HidrawDeviceInfo> find_hidraw_for_evdev(const std::string& evpath) {
    const std::string evdev_key = sysfs_key_for_evdev(evpath);
    const auto hidraws = find_stadia_hidraws();
    if (hidraws.empty()) return std::nullopt;

    if (!evdev_key.empty()) {
        for (const auto& hidraw : hidraws) {
            if (!hidraw.sysfs_key.empty() && hidraw.sysfs_key == evdev_key) {
                return hidraw;
            }
        }
    }

    if (hidraws.size() == 1) {
        return hidraws.front();
    }

    return std::nullopt;
}

static bool test_bit(unsigned int bit, const unsigned long* bits) {
    constexpr unsigned int bits_per_word = static_cast<unsigned int>(sizeof(unsigned long) * 8);
    return (bits[bit / bits_per_word] & (1UL << (bit % bits_per_word))) != 0;
}

static bool evdev_supports_native_rumble(int fd) {
    if (fd < 0) return false;
    constexpr unsigned int bits_per_word = static_cast<unsigned int>(sizeof(unsigned long) * 8);
    unsigned long ev_bits[(EV_MAX / bits_per_word) + 1] = {};
    if (ioctl(fd, EVIOCGBIT(0, sizeof(ev_bits)), ev_bits) < 0 || !test_bit(EV_FF, ev_bits)) {
        return false;
    }

    unsigned long ff_bits[(FF_MAX / bits_per_word) + 1] = {};
    if (ioctl(fd, EVIOCGBIT(EV_FF, sizeof(ff_bits)), ff_bits) < 0) {
        return false;
    }

    return test_bit(FF_RUMBLE, ff_bits);
}

static bool play_evdev_rumble_locked(ControllerRuntime& slot, int fd, uint8_t strong, uint8_t weak) {
    if (fd < 0) return false;

    if (strong == 0 && weak == 0) {
        if (slot.ff_effect_id >= 0) {
            struct input_event stop {};
            stop.type = EV_FF;
            stop.code = static_cast<uint16_t>(slot.ff_effect_id);
            stop.value = 0;
            if (write(fd, &stop, sizeof(stop)) < 0) {
                return false;
            }
        }
        return true;
    }

    struct ff_effect effect {};
    effect.type = FF_RUMBLE;
    effect.id = slot.ff_effect_id;
    effect.u.rumble.strong_magnitude = static_cast<uint16_t>(strong) * 257;
    effect.u.rumble.weak_magnitude = static_cast<uint16_t>(weak) * 257;
    effect.replay.length = 250;
    effect.replay.delay = 0;

    if (ioctl(fd, EVIOCSFF, &effect) < 0) {
        return false;
    }

    slot.ff_effect_id = effect.id;
    struct input_event play {};
    play.type = EV_FF;
    play.code = static_cast<uint16_t>(slot.ff_effect_id);
    play.value = 1;
    return write(fd, &play, sizeof(play)) >= 0;
}

static bool write_hidraw_rumble_report(int fd, uint8_t strong, uint8_t weak) {
    uint8_t report[5] = { 0x05, strong, strong, weak, weak };
    return write(fd, report, sizeof(report)) == static_cast<ssize_t>(sizeof(report));
}

static bool play_hidraw_rumble_locked(ControllerRuntime& slot, uint8_t strong, uint8_t weak) {
    if (slot.rumble_hidraw_path.empty()) return false;
    if (slot.rumble_hid_fd < 0) {
        slot.rumble_hid_fd = open_stadia_hidraw(slot.rumble_hidraw_path);
    }
    if (slot.rumble_hid_fd < 0) return false;

    if (write_hidraw_rumble_report(slot.rumble_hid_fd, strong, weak)) {
        return true;
    }

    close(slot.rumble_hid_fd);
    slot.rumble_hid_fd = -1;
    return false;
}

static void configure_controller_rumble(ControllerRuntime& slot, int ev_fd, const std::string& evpath, int controller_index) {
    std::lock_guard<std::mutex> lk(slot.rumble_mtx);
    if (slot.rumble_hid_fd >= 0) {
        close(slot.rumble_hid_fd);
        slot.rumble_hid_fd = -1;
    }
    slot.rumble_hidraw_path.clear();
    slot.ff_effect_id = -1;
    slot.native_ff_available = evdev_supports_native_rumble(ev_fd);
    slot.rumble_warning_logged = false;
    slot.last_rumble_strong = 0;
    slot.last_rumble_weak = 0;
    slot.last_rumble_sent = {};

    const auto hidraw = find_hidraw_for_evdev(evpath);
    if (hidraw.has_value()) {
        slot.rumble_hidraw_path = hidraw->path;
        slot.rumble_hid_fd = open_stadia_hidraw(slot.rumble_hidraw_path);
    }

    log_info(
        "Controller %d rumble paths: evdev force-feedback=%s, hidraw=%s%s",
        controller_index + 1,
        slot.native_ff_available ? "yes" : "no",
        slot.rumble_hidraw_path.empty() ? "none" : slot.rumble_hidraw_path.c_str(),
        slot.rumble_hid_fd >= 0 ? " open" : "");
}

static void send_controller_rumble(int controller_index, uint8_t strong, uint8_t weak) {
    if (!rumble_enabled() || controller_index < 0 || controller_index >= MAX_CONTROLLERS) return;

    ControllerRuntime& slot = g_controllers[controller_index];
    std::lock_guard<std::mutex> lk(slot.rumble_mtx);
    if (!slot.connected.load()) return;

    const auto now = std::chrono::steady_clock::now();
    const int dedupe_ms = rumble_dedupe_ms();
    if (dedupe_ms > 0 &&
        slot.last_rumble_sent.time_since_epoch().count() != 0 &&
        slot.last_rumble_strong == strong &&
        slot.last_rumble_weak == weak &&
        now - slot.last_rumble_sent < std::chrono::milliseconds(dedupe_ms)) {
        return;
    }

    const RumbleBackendPreference preference = rumble_backend_preference();
    if (preference == RumbleBackendPreference::Off) return;

    bool sent = false;
    int ev_fd = slot.evdev_fd.load();
    if ((preference == RumbleBackendPreference::Auto || preference == RumbleBackendPreference::Evdev) &&
        slot.native_ff_available) {
        sent = play_evdev_rumble_locked(slot, ev_fd, strong, weak);
        if (!sent) {
            slot.native_ff_available = false;
            log_info("Controller %d native force-feedback failed; falling back to hidraw", controller_index + 1);
        }
    }

    if (!sent && (preference == RumbleBackendPreference::Auto || preference == RumbleBackendPreference::Hidraw)) {
        sent = play_hidraw_rumble_locked(slot, strong, weak);
    }

    if (sent) {
        slot.last_rumble_strong = strong;
        slot.last_rumble_weak = weak;
        slot.last_rumble_sent = now;
        return;
    }

    if (!slot.rumble_warning_logged && (strong != 0 || weak != 0)) {
        log_info("Controller %d rumble unavailable: no usable evdev force-feedback or matching hidraw path", controller_index + 1);
        slot.rumble_warning_logged = true;
    }
}

static void shutdown_controller_rumble(ControllerRuntime& slot, int ev_fd) {
    std::lock_guard<std::mutex> lk(slot.rumble_mtx);

    if (slot.ff_effect_id >= 0 && ev_fd >= 0) {
        struct input_event stop {};
        stop.type = EV_FF;
        stop.code = static_cast<uint16_t>(slot.ff_effect_id);
        stop.value = 0;
        ssize_t stop_result = write(ev_fd, &stop, sizeof(stop));
        (void)stop_result;
        ioctl(ev_fd, EVIOCRMFF, slot.ff_effect_id);
        slot.ff_effect_id = -1;
    }

    if (slot.rumble_hid_fd >= 0) {
        write_hidraw_rumble_report(slot.rumble_hid_fd, 0, 0);
        close(slot.rumble_hid_fd);
        slot.rumble_hid_fd = -1;
    }
    slot.rumble_hidraw_path.clear();
    slot.native_ff_available = false;
    slot.rumble_warning_logged = false;
    slot.last_rumble_strong = 0;
    slot.last_rumble_weak = 0;
    slot.last_rumble_sent = {};
}

static int create_udp_socket() {
    int fd = socket(AF_INET, SOCK_DGRAM, 0);
    if (fd < 0) log_err("socket(UDP) failed: %s", strerror(errno));
    return fd;
}
static int create_udp_recv_socket(uint16_t port) {
    int fd = create_udp_socket(); if (fd < 0) return -1;
    int opt = 1; setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));
    struct sockaddr_in addr{}; addr.sin_family = AF_INET; addr.sin_addr.s_addr = INADDR_ANY; addr.sin_port = htons(port);
    if (bind(fd, reinterpret_cast<struct sockaddr*>(&addr), sizeof(addr)) < 0) {
        log_err("bind(UDP %u) failed: %s", port, strerror(errno)); close(fd); return -1;
    }
    return fd;
}
static void send_udp_code(const char* win_ip, const char* msg) {
    int sock = socket(AF_INET, SOCK_DGRAM, 0); if (sock < 0) return;
    struct sockaddr_in addr{}; addr.sin_family = AF_INET; addr.sin_port = htons(PORT_MACRO);
    if (inet_pton(AF_INET, win_ip, &addr.sin_addr) != 1) {
        log_err("Invalid Windows host IP for macro UDP: %s", win_ip);
        close(sock);
        return;
    }
    sendto(sock, msg, strlen(msg), 0, (struct sockaddr*)&addr, sizeof(addr));
    close(sock);
}

static bool controller_path_active(const std::string& path) {
    for (auto& controller : g_controllers) {
        if (!controller.connected.load()) continue;
        std::lock_guard<std::mutex> lk(controller.meta_mtx);
        if (controller.path == path) return true;
    }
    return false;
}

static int find_free_controller_slot() {
    for (int i = 0; i < MAX_CONTROLLERS; ++i) {
        if (!g_controllers[i].connected.load()) return i;
    }
    return -1;
}

static void mirror_primary_state(const ControllerState& raw_state) {
    {
        std::lock_guard<std::mutex> lk(g_state_mtx);
        g_state = raw_state;
    }
    {
        std::lock_guard<std::mutex> lk(g_raw_state_mtx);
        g_raw_state = raw_state;
    }
}

static ControllerState build_output_state(int controller_index, const ControllerState& raw_state) {
    ControllerState send_state = raw_state;
    if (controller_index == 0 && (g_assistant_held.load() || g_capture_held.load())) {
        send_state.buttons &= ~CHORD_SUPPRESS_MASK;
        send_state.trigger_left = 0;
        send_state.trigger_right = 0;
    }
    return send_state;
}

static void send_controller_packet(int udp_fd, const struct sockaddr_in& dest, int controller_index, const ControllerState& send_state) {
    InputPacket packet{};
    packet.magic = PACKET_MAGIC;
    packet.version = PACKET_VERSION;
    packet.controller_index = static_cast<uint8_t>(controller_index);
    packet.state = send_state;
    sendto(udp_fd, &packet, sizeof(packet), 0, (const struct sockaddr*)&dest, sizeof(dest));
}

static void controller_worker(int controller_index, std::string evpath, std::string name) {
    int udp_fd = create_udp_socket();
    if (udp_fd < 0) return;

    struct sockaddr_in dest{};
    dest.sin_family = AF_INET;
    dest.sin_port = htons(PORT_INPUT);
    if (inet_pton(AF_INET, g_target_ip.c_str(), &dest.sin_addr) != 1) {
        log_err("Invalid Windows host IP for controller UDP: %s", g_target_ip.c_str());
        close(udp_fd);
        return;
    }

    int ev_fd = open(evpath.c_str(), O_RDWR | O_NONBLOCK);
    if (ev_fd < 0) {
        log_info("Controller %d open failed for %s: %s", controller_index + 1, evpath.c_str(), strerror(errno));
        g_controllers[controller_index].connected.store(false);
        close(udp_fd);
        return;
    }

    ControllerRuntime& slot = g_controllers[controller_index];
    if (ioctl(ev_fd, EVIOCGRAB, 1) < 0) {
        log_info("Controller %d EVIOCGRAB failed for %s: %s", controller_index + 1, evpath.c_str(), strerror(errno));
    }
    {
        std::lock_guard<std::mutex> lk(slot.meta_mtx);
        slot.path = evpath;
        slot.name = name;
    }
    {
        std::lock_guard<std::mutex> lk(slot.state_mtx);
        std::memset(&slot.state, 0, sizeof(slot.state));
    }
    slot.evdev_fd.store(ev_fd);
    configure_controller_rumble(slot, ev_fd, evpath, controller_index);
    slot.connected.store(true);

    if (controller_index == 0) {
        {
            std::lock_guard<std::mutex> lk(g_controller_mtx);
            g_controller_path = evpath;
            g_controller_name = name;
        }
        g_evdev_fd.store(ev_fd);
        g_controller_connected.store(true);
        mirror_primary_state(ControllerState{});
    }

    log_info("Controller %d connected: %s [%s]", controller_index + 1, name.c_str(), evpath.c_str());

    struct pollfd pfd{};
    pfd.fd = ev_fd;
    pfd.events = POLLIN;
    bool connected = true;

    while (g_running && connected) {
        int ret = poll(&pfd, 1, 500);
        if (ret < 0) {
            if (errno == EINTR) continue;
            log_info("Controller %d poll failed for %s: %s", controller_index + 1, evpath.c_str(), strerror(errno));
            break;
        }
        if (ret == 0) continue;
        if (pfd.revents & (POLLERR | POLLHUP | POLLNVAL)) {
            log_info("Controller %d event device closed for %s: revents=0x%x", controller_index + 1, evpath.c_str(), pfd.revents);
            connected = false;
            break;
        }
        if (!(pfd.revents & POLLIN)) continue;

        struct input_event events[64];
        ssize_t rd = read(ev_fd, events, sizeof(events));
        if (rd < 0) {
            if (errno == EINTR || errno == EAGAIN || errno == EWOULDBLOCK) continue;
            log_info("Controller %d read failed for %s: %s", controller_index + 1, evpath.c_str(), strerror(errno));
            connected = false;
            break;
        }
        if (rd == 0) {
            log_info("Controller %d read returned EOF for %s", controller_index + 1, evpath.c_str());
            connected = false;
            break;
        }
        if ((rd % static_cast<ssize_t>(sizeof(struct input_event))) != 0) {
            log_info("Controller %d partial evdev read for %s: %zd bytes", controller_index + 1, evpath.c_str(), rd);
        }

        size_t count = static_cast<size_t>(rd) / sizeof(struct input_event);
        if (count == 0) continue;
        bool changed = false;

        {
            std::lock_guard<std::mutex> lk(slot.state_mtx);
            for (size_t i = 0; i < count; ++i) {
                if (events[i].type == EV_SYN) {
                    if (changed) {
                        ControllerState raw_state = slot.state;
                        if (controller_index == 0) mirror_primary_state(raw_state);
                        send_controller_packet(udp_fd, dest, controller_index, build_output_state(controller_index, raw_state));
                        changed = false;
                    }
                } else {
                    apply_event(events[i], slot.state);
                    changed = true;
                }
            }

            if (changed) {
                ControllerState raw_state = slot.state;
                if (controller_index == 0) mirror_primary_state(raw_state);
                send_controller_packet(udp_fd, dest, controller_index, build_output_state(controller_index, raw_state));
            }
        }
    }

    log_info("Controller %d disconnected: %s", controller_index + 1, evpath.c_str());
    slot.connected.store(false);
    slot.evdev_fd.store(-1);
    {
        std::lock_guard<std::mutex> lk(slot.meta_mtx);
        slot.path.clear();
        slot.name.clear();
    }

    if (controller_index == 0) {
        g_controller_connected.store(false);
        g_evdev_fd.store(-1);
        {
            std::lock_guard<std::mutex> lk(g_controller_mtx);
            g_controller_path.clear();
            g_controller_name.clear();
        }
    }

    shutdown_controller_rumble(slot, ev_fd);
    ioctl(ev_fd, EVIOCGRAB, 0);
    close(ev_fd);
    close(udp_fd);
}

static void input_sender_thread() {
    while (g_running) {
        auto devices = find_stadia_evdevs();
        for (const auto& device : devices) {
            if (controller_path_active(device.path)) continue;

            int slot = find_free_controller_slot();
            if (slot < 0) {
                log_info("Extra Stadia controller ignored because all %d slots are already active: %s", MAX_CONTROLLERS, device.path.c_str());
                break;
            }

            {
                ControllerRuntime& reserved = g_controllers[slot];
                std::lock_guard<std::mutex> lk(reserved.meta_mtx);
                reserved.path = device.path;
                reserved.name = device.name;
                reserved.connected.store(true);
            }
            std::thread(controller_worker, slot, device.path, device.name).detach();
        }

        std::unique_lock<std::mutex> lk(g_wake_mtx);
        g_wake_cv.wait_for(lk, std::chrono::seconds(3));
    }
}

static void rumble_receiver_thread() {
    int udp_fd = create_udp_recv_socket(PORT_RUMBLE); if (udp_fd < 0) return;
    struct timeval tv{}; tv.tv_sec = 1; tv.tv_usec = 0; setsockopt(udp_fd, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
    while (g_running) {
        char buf[64] = {};
        RumbleState rs{};
        uint8_t controller_index = 0;
        ssize_t n = recv(udp_fd, buf, sizeof(buf), 0);
        if (n == sizeof(RumbleState)) {
            std::memcpy(&rs, buf, sizeof(rs));
        } else if (n == sizeof(RumblePacket)) {
            RumblePacket packet{};
            std::memcpy(&packet, buf, sizeof(packet));
            if (packet.magic != PACKET_MAGIC || packet.version != PACKET_VERSION || packet.controller_index >= MAX_CONTROLLERS) {
                continue;
            }
            controller_index = packet.controller_index;
            rs = packet.rumble;
        } else {
            continue;
        }

        send_controller_rumble(controller_index, rs.motor_left, rs.motor_right);
    }
    close(udp_fd);
}

void start_extra_buttons_thread(const char* win_ip) {
    std::thread([win_ip] {
        while (g_running) {
        int hid_fd = -1;
        while (g_running && hid_fd < 0) {
            for (int i = 0; i < 15; ++i) {
                char path[256]; snprintf(path, sizeof(path), "/dev/hidraw%d", i);
                int test_fd = open(path, O_RDONLY);
                if (test_fd >= 0) {
                    struct hidraw_devinfo info;
                    if (ioctl(test_fd, HIDIOCGRAWINFO, &info) == 0 && (uint16_t)info.vendor == 0x18d1 && (uint16_t)info.product == 0x9400) {
                        hid_fd = test_fd; break;
                    }
                    close(test_fd);
                }
            }
            if (hid_fd < 0 && g_running) sleep(1);
        }

        if (!g_running) {
            if (hid_fd >= 0) close(hid_fd);
            break;
        }

        struct ChordSlot {
            const char* code; bool held; bool sent_once;
            std::chrono::steady_clock::time_point first_fire;
            std::chrono::steady_clock::time_point last_fire;
        };

        #define NUM_CHORDS 17
        ChordSlot ast_slots[NUM_CHORDS] = {
            {"A_A",0,0,{},{}}, {"A_B",0,0,{},{}}, {"A_X",0,0,{},{}}, {"A_Y",0,0,{},{}},
            {"A_UP",0,0,{},{}}, {"A_DOWN",0,0,{},{}}, {"A_LEFT",0,0,{},{}}, {"A_RIGHT",0,0,{},{}},
            {"A_LB",0,0,{},{}}, {"A_RB",0,0,{},{}}, {"A_L2",0,0,{},{}}, {"A_R2",0,0,{},{}},
            {"A_L3",0,0,{},{}}, {"A_R3",0,0,{},{}},
            {"A_SELECT",0,0,{},{}}, {"A_START",0,0,{},{}}, {"A_STADIA",0,0,{},{}}
        };
        ChordSlot cap_slots[NUM_CHORDS] = {
            {"C_A",0,0,{},{}}, {"C_B",0,0,{},{}}, {"C_X",0,0,{},{}}, {"C_Y",0,0,{},{}},
            {"C_UP",0,0,{},{}}, {"C_DOWN",0,0,{},{}}, {"C_LEFT",0,0,{},{}}, {"C_RIGHT",0,0,{},{}},
            {"C_LB",0,0,{},{}}, {"C_RB",0,0,{},{}}, {"C_L2",0,0,{},{}}, {"C_R2",0,0,{},{}},
            {"C_L3",0,0,{},{}}, {"C_R3",0,0,{},{}},
            {"C_SELECT",0,0,{},{}}, {"C_START",0,0,{},{}}, {"C_STADIA",0,0,{},{}}
        };

        constexpr auto REPEAT_DELAY = std::chrono::milliseconds(350);
        constexpr auto REPEAT_INTERVAL = std::chrono::milliseconds(100);

        auto fire_slot = [&](ChordSlot& slot) {
            auto now = std::chrono::steady_clock::now();
            if (!slot.sent_once) {
                send_udp_code(win_ip, slot.code);
                slot.first_fire = now; slot.last_fire = now; slot.sent_once = true;
            } else if (now - slot.first_fire >= REPEAT_DELAY && now - slot.last_fire >= REPEAT_INTERVAL) {
                send_udp_code(win_ip, slot.code); slot.last_fire = now;
            }
        };

        uint8_t buf[32] = {};
        bool ast_solo_sent = false; bool cap_solo_sent = false;
        while (g_running) {
            struct pollfd pfd{}; pfd.fd = hid_fd; pfd.events = POLLIN;
            int ret = poll(&pfd, 1, 50);
            if (ret < 0) { if (errno == EINTR) continue; log_info("Extra button HID poll failed; rescanning"); break; }
            if (ret > 0) { int n = read(hid_fd, buf, sizeof(buf)); if (n <= 0) { log_info("Extra button HID closed; rescanning"); break; } if (n < 6) continue; }

            bool ast = (buf[2] & 0x02) != 0;
            bool cap = (buf[2] & 0x01) != 0;
            g_assistant_held.store(ast); g_capture_held.store(cap);

            uint16_t b = 0; uint8_t l2_val = 0; uint8_t r2_val = 0;
            {
                std::lock_guard<std::mutex> rlk(g_raw_state_mtx);
                b = g_raw_state.buttons;
                l2_val = g_raw_state.trigger_left;
                r2_val = g_raw_state.trigger_right;
            }

            bool st_a = (b & BTN_BIT_A) != 0; bool st_b = (b & BTN_BIT_B) != 0;
            bool st_x = (b & BTN_BIT_X) != 0; bool st_y = (b & BTN_BIT_Y) != 0;
            bool d_up = (b & BTN_BIT_DPAD_UP) != 0; bool d_dn = (b & BTN_BIT_DPAD_DOWN) != 0;
            bool d_lf = (b & BTN_BIT_DPAD_LEFT) != 0; bool d_rt = (b & BTN_BIT_DPAD_RIGHT) != 0;
            bool st_lb = (b & BTN_BIT_LB) != 0; bool st_rb = (b & BTN_BIT_RB) != 0;
            bool st_l2 = l2_val > 128; bool st_r2 = r2_val > 128;
            bool st_l3 = (b & BTN_BIT_L3) != 0; bool st_r3 = (b & BTN_BIT_R3) != 0;
            bool st_sel = (b & BTN_BIT_SELECT) != 0; bool st_sta = (b & BTN_BIT_START) != 0;
            bool st_home = (b & BTN_BIT_STADIA) != 0;

            bool active_arr[NUM_CHORDS] = { st_a, st_b, st_x, st_y, d_up, d_dn, d_lf, d_rt, st_lb, st_rb, st_l2, st_r2, st_l3, st_r3, st_sel, st_sta, st_home };

            bool any_ast = false; bool any_cap = false;

            for (int i = 0; i < NUM_CHORDS; ++i) {
                if (ast && active_arr[i]) { ast_slots[i].held = true; fire_slot(ast_slots[i]); any_ast = true; } 
                else { ast_slots[i].held = false; ast_slots[i].sent_once = false; }

                if (cap && active_arr[i]) { cap_slots[i].held = true; fire_slot(cap_slots[i]); any_cap = true; } 
                else { cap_slots[i].held = false; cap_slots[i].sent_once = false; }
            }

            if (ast && !any_ast) { if (!ast_solo_sent) { send_udp_code(win_ip, "A"); ast_solo_sent = true; } } else ast_solo_sent = false;
            if (cap && !any_cap) { if (!cap_solo_sent) { send_udp_code(win_ip, "C"); cap_solo_sent = true; } } else cap_solo_sent = false;
        }
        if (hid_fd >= 0) close(hid_fd);
        g_assistant_held.store(false); g_capture_held.store(false);
        if (g_running) sleep(1);
        }
    }).detach();
}

static std::string bt_scan(int seconds = 8) { return shell_exec("timeout " + std::to_string(seconds + 2) + " bluetoothctl --timeout " + std::to_string(seconds) + " scan on 2>&1; bluetoothctl devices 2>&1"); }
static std::string bt_pair(const std::string& mac) { if (!is_valid_bluetooth_mac(mac)) return "Invalid Bluetooth MAC"; std::ostringstream oss; oss << "trust: " << shell_exec("bluetoothctl trust " + mac + " 2>&1") << "\npair: " << shell_exec("timeout 15 bluetoothctl pair " + mac + " 2>&1") << "\nconnect: " << shell_exec("bluetoothctl connect " + mac + " 2>&1") << "\n"; return oss.str(); }
static std::string bt_connect(const std::string& mac) { if (!is_valid_bluetooth_mac(mac)) return "Invalid Bluetooth MAC"; return shell_exec("bluetoothctl connect " + mac + " 2>&1"); }
static std::string bt_disconnect(const std::string& mac) { if (!is_valid_bluetooth_mac(mac)) return "Invalid Bluetooth MAC"; return shell_exec("bluetoothctl disconnect " + mac + " 2>&1"); }
static std::string bt_remove(const std::string& mac) { if (!is_valid_bluetooth_mac(mac)) return "Invalid Bluetooth MAC"; return shell_exec("bluetoothctl remove " + mac + " 2>&1"); }
static std::string bt_info(const std::string& mac) { if (!is_valid_bluetooth_mac(mac)) return "Invalid Bluetooth MAC"; return shell_exec("bluetoothctl info " + mac + " 2>&1"); }
static std::string bt_devices() { return shell_exec("bluetoothctl devices 2>&1"); }

static void handle_c2_client(int client_fd) {
    auto send_reply = [&](const std::string& msg) { std::string out = msg + "\n"; send(client_fd, out.c_str(), out.size(), MSG_NOSIGNAL); };
    send_reply("STADIA_BRIDGE READY");
    char buf[4096]; std::string leftover;
    while (g_running) {
        struct pollfd pfd{}; pfd.fd = client_fd; pfd.events = POLLIN;
        int ret = poll(&pfd, 1, 1000); if (ret <= 0) { if (ret == 0 || errno == EINTR) continue; break; }
        ssize_t n = recv(client_fd, buf, sizeof(buf) - 1, 0); if (n <= 0) break;
        buf[n] = '\0'; leftover += buf;
        size_t pos;
        while ((pos = leftover.find('\n')) != std::string::npos) {
            std::string line = leftover.substr(0, pos); leftover.erase(0, pos + 1);
            if (line == "SHUTDOWN") { send_reply("BYE"); g_running = false; g_wake_cv.notify_all(); break; }
            send_reply("ACK");
        }
    }
    close(client_fd);
}

static void c2_server_thread() {
    int server_fd = socket(AF_INET, SOCK_STREAM, 0);
    if (server_fd < 0) {
        log_err("C2 socket failed: %s", strerror(errno));
        return;
    }
    int opt = 1; setsockopt(server_fd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));
    struct sockaddr_in addr{}; addr.sin_family = AF_INET; addr.sin_addr.s_addr = INADDR_ANY; addr.sin_port = htons(PORT_C2);
    if (bind(server_fd, (struct sockaddr*)&addr, sizeof(addr)) < 0 || listen(server_fd, 4) < 0) {
        log_err("C2 bind/listen on TCP %u failed: %s", static_cast<unsigned>(PORT_C2), strerror(errno));
        close(server_fd);
        return;
    }
    while (g_running) {
        struct pollfd pfd{}; pfd.fd = server_fd; pfd.events = POLLIN;
        int ret = poll(&pfd, 1, 1000); if (ret <= 0) continue;
        struct sockaddr_in client_addr{}; socklen_t client_len = sizeof(client_addr);
        int client_fd = accept(server_fd, (struct sockaddr*)&client_addr, &client_len);
        if (client_fd >= 0) std::thread(handle_c2_client, client_fd).detach();
    }
    close(server_fd);
}

int main(int argc, char* argv[]) {
    if (argc < 2) return 1;
    g_target_ip = argv[1];
    struct in_addr target_addr{};
    if (inet_pton(AF_INET, g_target_ip.c_str(), &target_addr) != 1) {
        log_err("Invalid Windows host IP: %s", g_target_ip.c_str());
        return 1;
    }
    const char* enable_hidraw_chords = std::getenv("STADIA_X_ENABLE_HIDRAW_CHORDS");
    if (enable_hidraw_chords != nullptr && std::strcmp(enable_hidraw_chords, "1") == 0) {
        start_extra_buttons_thread(argv[1]);
    }
    std::thread t_input(input_sender_thread);
    std::thread t_rumble(rumble_receiver_thread);
    std::thread t_c2(c2_server_thread);
    t_input.join(); t_rumble.join(); t_c2.join();
    return 0;
}
