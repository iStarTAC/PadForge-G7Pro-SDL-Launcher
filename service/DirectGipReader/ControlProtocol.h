#pragma once
#include <cstdint>
#include <initializer_list>

namespace gipControl {
    constexpr uint32_t magic = 0x32475050; // PPG2, little endian
    constexpr uint16_t version = 2;
    enum class Command : uint16_t { status = 1, prepare = 2, startReader = 3, stopReader = 4,
        startProbe = 5, stopProbe = 6, probeStatus = 7, startBridge = 8, stopBridge = 9 };
    enum class State : uint32_t { idle = 0, reading = 1 };
    struct Request {
        uint32_t signature{magic}; uint16_t protocol{version}; Command command{};
        uint32_t requestId{}; uint32_t reserved{};
    };
    struct Response {
        uint32_t signature{magic}; uint16_t protocol{version}; Command command{};
        uint32_t requestId{}, error{}; State state{State::idle}; uint32_t readerResult{};
    };
    static_assert(sizeof(Request) == 16 && sizeof(Response) == 24);
    constexpr bool valid(Request const& request) {
        return request.signature == magic && request.protocol == version && request.reserved == 0 &&
            request.command >= Command::status && request.command <= Command::stopBridge;
    }
    constexpr bool allowed(Command command, bool running, uint32_t owner, uint32_t caller) {
        return command == Command::status || command == Command::probeStatus || !running || owner == caller;
    }
    inline bool selfTest() {
        Request request{};
        if (valid(request)) return false;
        for (auto command : {Command::status, Command::prepare, Command::startReader, Command::stopReader}) {
            request.command = command;
            if (!valid(request) || !allowed(command, false, 0, 17)) return false;
            if (command != Command::status && allowed(command, true, 17, 18)) return false;
            if (!allowed(command, true, 17, 17)) return false;
        }
        request.protocol = 1; if (valid(request)) return false;
        request.protocol = version; request.reserved = 1; if (valid(request)) return false;
        request.reserved = 0; request.signature = 0; if (valid(request)) return false;
        request.signature = magic; request.command = static_cast<Command>(10);
        return !valid(request);
    }
}
