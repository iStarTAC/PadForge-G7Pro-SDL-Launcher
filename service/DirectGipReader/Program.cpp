// Independent, bounded diagnostic. No GameSir DLL, WGI, USBPcap or driver changes.
// The 20-byte client header and read/init contract were checked against the
// installed Windows.Gaming.Input implementation. No system-state packets sent.
#include <windows.h>
#include <setupapi.h>
#include <cfgmgr32.h>
#ifndef G7PRO_TELEMETRY_SERVICE
#include <roapi.h>
#include <winstring.h>
#endif
#include <array>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <string>
#include <stdexcept>
#include <algorithm>
#include <cwctype>
#pragma comment(lib, "setupapi.lib")
#ifndef G7PRO_TELEMETRY_SERVICE
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "runtimeobject.lib")
#pragma comment(lib, "ole32.lib")
#endif
#ifdef G7PRO_TELEMETRY_SERVICE
#include "../G7ProTelemetryService/ServiceTransport.h"
#else
#include "ServiceTransport.h"
#include "AppModeEntry.h"
#endif
#include "TelemetryFrame.h"

namespace {
    constexpr GUID gipInterface{0x020bc73c,0x0dca,0x4ee3,{0x96,0xd5,0xab,0x00,0x6a,0xda,0x59,0x38}};
    constexpr DWORD enumerateContext = 0x40001cd0;
    struct Handle {
        HANDLE value{INVALID_HANDLE_VALUE};
        ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    };
#ifndef G7PRO_TELEMETRY_SERVICE
    HANDLE brokerOpen()
    {
        // Exact primary fallback in installed WGI PnpDevice::OpenInterface.
        // The Windows broker retains its own access checks. No token changes.
        struct Broker : IUnknown {
            virtual HRESULT STDMETHODCALLTYPE Open(wchar_t const*, HANDLE*) = 0;
        };
        struct LegacyBroker : IUnknown {
            virtual HRESULT STDMETHODCALLTYPE Open(wchar_t const*, DWORD, DWORD, DWORD, HANDLE*) = 0;
        };
        constexpr GUID factoryId{0x10975cab,0x3fb1,0x4f98,{0x8b,0x5f,0x58,0xb8,0x44,0x6b,0x5e,0xe4}};
        constexpr GUID brokerId{0x538dad8e,0x3925,0x457b,{0xad,0xb4,0x1f,0xfa,0xbb,0x45,0xf0,0x14}};
        HRESULT initialized = RoInitialize(RO_INIT_MULTITHREADED);
        if (FAILED(initialized)) return INVALID_HANDLE_VALUE;
        HSTRING name{};
        IUnknown* factory{};
        Broker* broker{};
        HANDLE result = INVALID_HANDLE_VALUE;
        constexpr wchar_t className[] = L"Windows.Internal.Gaming.GamingDevice.GamingDeviceStatics";
        HRESULT status = WindowsCreateString(className, ARRAYSIZE(className) - 1, &name);
        if (SUCCEEDED(status)) status = RoGetActivationFactory(name, factoryId, reinterpret_cast<void**>(&factory));
        if (SUCCEEDED(status)) status = factory->QueryInterface(brokerId, reinterpret_cast<void**>(&broker));
        if (SUCCEEDED(status)) status = broker->Open(L"\\\\.\\XboxGIP_Admin", &result);
        std::cout << "broker_open_hresult=0x" << std::hex << static_cast<unsigned long>(status) << std::dec << '\n';
        if (broker) broker->Release();
        if (factory) factory->Release();
        WindowsDeleteString(name);
        if (FAILED(status)) {
            if (result && result != INVALID_HANDLE_VALUE) CloseHandle(result);
            result = INVALID_HANDLE_VALUE;
            constexpr GUID legacyClass{0xacc56a05,0xe277,0x4b1e,{0xa4,0x3e,0x7a,0x73,0xe3,0xcd,0x6e,0x6c}};
            constexpr GUID legacyId{0x8604b268,0x34a6,0x4b1a,{0xa5,0x9f,0xcd,0xbd,0x83,0x79,0xfd,0x98}};
            LegacyBroker* legacy{};
            status = CoCreateInstance(legacyClass, nullptr, CLSCTX_INPROC_SERVER, legacyId,
                reinterpret_cast<void**>(&legacy));
            if (SUCCEEDED(status)) status = legacy->Open(L"\\\\.\\XboxGIP_Admin",
                GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                FILE_FLAG_OVERLAPPED, &result);
            std::cout << "legacy_broker_open_hresult=0x" << std::hex << static_cast<unsigned long>(status) << std::dec << '\n';
            if (legacy) legacy->Release();
        }
        RoUninitialize();
        if (FAILED(status)) {
            if (result && result != INVALID_HANDLE_VALUE) CloseHandle(result);
            SetLastError(ERROR_ACCESS_DENIED);
            return INVALID_HANDLE_VALUE;
        }
        return result;
    }
#endif
    #pragma pack(push, 1)
    struct Header {
        uint64_t device;
        uint8_t command, flags, sequence, padding;
        uint32_t length, reserved;
    };
    #pragma pack(pop)
    static_assert(sizeof(Header) == 20);

    void clockEvent(char const* name)
    {
        FILETIME time{};
        GetSystemTimePreciseAsFileTime(&time);
        ULARGE_INTEGER value{};
        value.LowPart = time.dwLowDateTime;
        value.HighPart = time.dwHighDateTime;
        std::cout << "direct_clock,event=" << name << ",tick_ms=" << GetTickCount64()
                  << ",unix_us=" << ((value.QuadPart - 116444736000000000ULL) / 10) << '\n';
    }

    bool onlyTargetPresent(std::wstring& targetPath)
    {
        auto devices = SetupDiGetClassDevsW(&gipInterface, nullptr, nullptr, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devices == INVALID_HANDLE_VALUE) return false;
        unsigned total = 0, targets = 0;
        for (DWORD index = 0;; ++index) {
            SP_DEVICE_INTERFACE_DATA entry{sizeof(entry)};
            if (!SetupDiEnumDeviceInterfaces(devices, nullptr, &gipInterface, index, &entry)) {
                DWORD error = GetLastError();
                if (error != ERROR_NO_MORE_ITEMS) {
                    SetupDiDestroyDeviceInfoList(devices);
                    throw std::runtime_error("GIP interface enumeration failed");
                }
                break;
            }
            SP_DEVINFO_DATA info{sizeof(info)};
            DWORD required{};
            SetupDiGetDeviceInterfaceDetailW(devices, &entry, nullptr, 0, &required, nullptr);
            std::wstring storage((required + sizeof(wchar_t) - 1) / sizeof(wchar_t), L'\0');
            auto detail = reinterpret_cast<SP_DEVICE_INTERFACE_DETAIL_DATA_W*>(storage.data());
            detail->cbSize = sizeof(*detail);
            if (!SetupDiGetDeviceInterfaceDetailW(devices, &entry, detail, required, nullptr, &info)) {
                SetupDiDestroyDeviceInfoList(devices);
                throw std::runtime_error("Cannot resolve GIP interface owner");
            }
            wchar_t instance[MAX_DEVICE_ID_LEN]{};
            if (!SetupDiGetDeviceInstanceIdW(devices, &info, instance, MAX_DEVICE_ID_LEN, nullptr)) {
                SetupDiDestroyDeviceInfoList(devices);
                throw std::runtime_error("Cannot identify GIP interface owner");
            }
            std::wstring identity(instance);
            std::transform(identity.begin(), identity.end(), identity.begin(), [](wchar_t c) {
                return static_cast<wchar_t>(towupper(c));
            });
            ++total;
            if (identity.find(L"VID_3537&PID_109B") != std::wstring::npos ||
                identity.find(L"VID_3537&PID_109C") != std::wstring::npos) {
                ++targets;
                targetPath = detail->DevicePath;
            }
        }
        SetupDiDestroyDeviceInfoList(devices);
        std::cout << "present_gip_interfaces=" << total << ",target_app_mode_interfaces=" << targets << '\n';
        return targets == 1;
    }

    bool finish(HANDLE device, OVERLAPPED& operation, DWORD timeout, DWORD& transferred)
    {
        DWORD wait = WaitForSingleObject(operation.hEvent, timeout);
        if (wait != WAIT_OBJECT_0) {
            CancelIoEx(device, &operation);
            GetOverlappedResult(device, &operation, &transferred, TRUE);
            SetLastError(wait == WAIT_TIMEOUT ? ERROR_TIMEOUT : ERROR_GEN_FAILURE);
            return false;
        }
        return !!GetOverlappedResult(device, &operation, &transferred, FALSE);
    }

    bool control(HANDLE device, DWORD code, void* input = nullptr, DWORD inputSize = 0)
    {
        Handle event{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
        OVERLAPPED operation{};
        operation.hEvent = event.value;
        DWORD transferred{};
        if (DeviceIoControl(device, code, input, inputSize, nullptr, 0, &transferred, &operation)) return true;
        return GetLastError() == ERROR_IO_PENDING && finish(device, operation, 1000, transferred);
    }

    bool heartbeat(HANDLE device, uint64_t target)
    {
        // Same client envelope built by WGI SendMessageW; driver constructs wire framing.
        // Page-aligned storage also supports WGI's FILE_FLAG_NO_BUFFERING open flags.
        auto data = static_cast<uint8_t*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
        if (!data) return false;
        Header header{target, 0x0f, 0, 0, 0, 2, 0};
        std::memcpy(data, &header, sizeof(header));
        data[20] = 0xf2;
        data[21] = 0;
        Handle event{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
        OVERLAPPED operation{};
        operation.hEvent = event.value;
        DWORD transferred{};
        BOOL started = WriteFile(device, data, 22, &transferred, &operation);
        // WGI always obtains the overlapped completion, even on immediate
        // success, and does not require the driver to echo the write length.
        bool success = (started || GetLastError() == ERROR_IO_PENDING) &&
            finish(device, operation, 1000, transferred);
        DWORD error = success ? 0 : GetLastError();
        VirtualFree(data, 0, MEM_RELEASE);
        if (!success) {
            std::cout << "heartbeat_failed,error=" << error << ",bytes=" << transferred << '\n';
            return false;
        }
        return true;
    }
}

int runReader(int argc, wchar_t** argv)
{
    std::cout << std::unitbuf;
    clockEvent("start");
    int seconds = argc > 1 ? _wtoi(argv[1]) : 8;
#ifdef G7PRO_TELEMETRY_SERVICE
    constexpr bool sendHeartbeat = true;
    constexpr bool wgiSubscription = true;
#else
    bool sendHeartbeat = false, inspect = false, visible = false, automaticFocusTest = false, wgiSubscription = false;
    bool deviceInterface = false;
    bool adminEndpoint = false;
    bool brokerFallback = false;
    for (int i = 2; i < argc; ++i) {
        if (!_wcsicmp(argv[i], L"--heartbeat")) sendHeartbeat = true;
        else if (!_wcsicmp(argv[i], L"--inspect")) inspect = true;
        else if (!_wcsicmp(argv[i], L"--window")) visible = true;
        else if (!_wcsicmp(argv[i], L"--auto-focus-test")) automaticFocusTest = true;
        else if (!_wcsicmp(argv[i], L"--wgi-subscription")) wgiSubscription = true;
        else if (!_wcsicmp(argv[i], L"--device-interface")) deviceInterface = true;
        else if (!_wcsicmp(argv[i], L"--admin-endpoint")) adminEndpoint = true;
        else if (!_wcsicmp(argv[i], L"--broker-open")) brokerFallback = true;
        else { std::cerr << "Unknown option\n"; return 1; }
    }
    if ((seconds < 1 || seconds > 60) && !(serviceTransport::active && seconds == 0)) { std::cerr << "Duration must be1..60 seconds\n"; return 1; }
    if (automaticFocusTest && !visible) { std::cerr << "--auto-focus-test requires --window\n"; return 1; }
    if (deviceInterface && adminEndpoint) { std::cerr << "Choose only one endpoint\n"; return 1; }
    if (brokerFallback && !adminEndpoint) { std::cerr << "--broker-open requires --admin-endpoint\n"; return 1; }
#endif
    try {
        std::wstring targetPath;
        if (!onlyTargetPresent(targetPath)) {
            std::cerr << "Refused: requires exactly one present 3537:109B/109C GIP interface\n";
            return 2;
        }
#ifndef G7PRO_TELEMETRY_SERVICE
        struct Window {
            HWND value{};
            ~Window() { if (value) DestroyWindow(value); }
        } window;
        if (visible) {
            window.value = CreateWindowW(L"STATIC", L"Panda direct GIP diagnostic",
                WS_OVERLAPPEDWINDOW | WS_VISIBLE, CW_USEDEFAULT, CW_USEDEFAULT,
                640, 180, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
            if (!window.value) throw std::runtime_error("Cannot create direct-reader test window");
            SetForegroundWindow(window.value);
        }
#endif
#ifdef G7PRO_TELEMETRY_SERVICE
        std::cout << "open_endpoint=admin\n";
        Handle device{CreateFileW(L"\\\\.\\XboxGIP_Admin", GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED | FILE_FLAG_NO_BUFFERING, nullptr)};
#else
        std::cout << "open_endpoint=" << (deviceInterface ? "device_interface" : adminEndpoint ? "admin" : "global") << '\n';
        // WGI CheckForGipDeviceArrival constructs this exact administrative
        // control endpoint. No privilege changes or broker fallback here.
        Handle device{CreateFileW(deviceInterface ? targetPath.c_str() : adminEndpoint ? L"\\\\.\\XboxGIP_Admin" : L"\\\\.\\XboxGIP", GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED | FILE_FLAG_NO_BUFFERING, nullptr)};
        if (device.value == INVALID_HANDLE_VALUE && GetLastError() == ERROR_ACCESS_DENIED && brokerFallback)
            device.value = brokerOpen();
#endif
        if (device.value == INVALID_HANDLE_VALUE) {
            std::cerr << "direct_open_failed,error=" << GetLastError() << '\n';
            return 3;
        }
        std::cout << "direct_open=success,no_WGI=1,no_vendor_DLL=1,no_USB_capture=1\n";
#ifndef G7PRO_TELEMETRY_SERVICE
        if (inspect) return 0;
#endif
        auto memory = static_cast<uint8_t*>(VirtualAlloc(nullptr, 4 * 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
        if (!memory) throw std::runtime_error("Cannot allocate read buffers");
        struct Reads {
            HANDLE device;
            uint8_t* memory;
            std::array<OVERLAPPED, 4> operations{};
            std::array<HANDLE, 4> events{};
            std::array<bool, 4> pending{};
            ~Reads() {
                CancelIoEx(device, nullptr);
                for (size_t i = 0; i < operations.size(); ++i) {
                    auto& operation = operations[i];
                    if (!operation.hEvent) continue;
                    DWORD transferred{};
                    if (pending[i]) GetOverlappedResult(device, &operation, &transferred, TRUE);
                    CloseHandle(operation.hEvent);
                }
                VirtualFree(memory, 0, MEM_RELEASE);
            }
            void queue(size_t index) {
                auto& operation = operations[index];
                if (!events[index]) events[index] = CreateEventW(nullptr, TRUE, FALSE, nullptr);
                if (!events[index]) throw std::runtime_error("Cannot allocate read event");
                ResetEvent(events[index]);
                operation = {};
                operation.hEvent = events[index];
                DWORD transferred{};
                pending[index] = false;
                if (!ReadFile(device, memory + index * 4096, 4096, &transferred, &operation) &&
                    GetLastError() != ERROR_IO_PENDING)
                    throw std::runtime_error("Direct ReadFile submission failed");
                pending[index] = true;
            }
        } reads{device.value, memory};
        for (size_t i = 0; i < 4; ++i) reads.queue(i);
        if (wgiSubscription) {
            // Exact four-byte value/request observed in installed WGI and
            // GameInputRedist initialization, not a guessed focus flag sweep.
            DWORD flags = 0x0f38;
            if (!control(device.value, 0x40001c10, &flags, sizeof(flags))) {
                std::cerr << "subscription_flags_failed,error=" << GetLastError() << '\n';
                return 4;
            }
            std::cout << "subscription_flags=0x0f38,ioctl=0x40001c10,completion=success\n";
        }
        if (!control(device.value, enumerateContext)) {
            std::cerr << "context_enumeration_failed,error=" << GetLastError() << '\n';
            return 4;
        }
#ifdef G7PRO_TELEMETRY_SERVICE
        std::cout << "context_enumeration=requested,queued_reads=4,background_windowless=1\n";
#else
        std::cout << "context_enumeration=requested,queued_reads=4,background_windowless=" << !visible << '\n';
#endif
        uint64_t target{}, packets{}, telemetry{}, focusEvents{}, malformed{}, writes{};
        ULONGLONG deadline = GetTickCount64() + static_cast<ULONGLONG>(seconds) * 1000;
        ULONGLONG nextHeartbeat{}, nextSummary = GetTickCount64() + 1000;
#ifndef G7PRO_TELEMETRY_SERVICE
        ULONGLONG focusedSince{};
        bool minimized = false;
#endif
        ULONGLONG lastTelemetry = GetTickCount64();
        while ((seconds == 0 || GetTickCount64() < deadline) && !serviceTransport::stopped()) {
            if (serviceTransport::active && GetTickCount64() - lastTelemetry > 5000) return 8;
#ifndef G7PRO_TELEMETRY_SERVICE
            MSG message{};
            while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
            bool focused = window.value && GetForegroundWindow() == window.value;
            if (automaticFocusTest && !minimized) {
                if (!focused) focusedSince = 0;
                else if (!focusedSince) focusedSince = GetTickCount64();
                else if (GetTickCount64() - focusedSince >= 10000) {
                    std::cout << "direct_focus_test=automatic_minimize\n";
                    clockEvent("before_minimize");
                    ShowWindow(window.value, SW_MINIMIZE);
                    minimized = true;
                }
            }
#endif
            DWORD wait = WaitForMultipleObjects(4, reads.events.data(), FALSE, 10);
            if (wait < WAIT_OBJECT_0 + 4) {
                size_t index = wait - WAIT_OBJECT_0;
                DWORD transferred{};
                if (!GetOverlappedResult(device.value, &reads.operations[index], &transferred, FALSE)) {
                    std::cerr << "direct_read_failed,error=" << GetLastError() << '\n';
                    return 5;
                }
                auto buffer = memory + index * 4096;
                reads.pending[index] = false;
                size_t offset = 0;
                while (offset + sizeof(Header) <= transferred) {
                    Header header{};
                    std::memcpy(&header, buffer + offset, sizeof(header));
                    if (header.length > transferred - offset - sizeof(Header)) { ++malformed; break; }
                    auto payload = buffer + offset + sizeof(Header);
                    ++packets;
                    if (packets <= 10)
                        std::cout << "driver_message,command=" << static_cast<unsigned>(header.command)
                                  << ",flags=" << static_cast<unsigned>(header.flags)
                                  << ",length=" << header.length << '\n';
                    if (header.command == 2 && (header.flags & 0x20) && header.length >= 12) {
                        uint16_t vendor{}, product{};
                        uint64_t arrival{};
                        std::memcpy(&arrival, payload, 8);
                        std::memcpy(&vendor, payload + 8, 2);
                        std::memcpy(&product, payload + 10, 2);
                        if (vendor == 0x3537 && (product == 0x109b || product == 0x109c) && arrival == header.device) {
                            target = header.device;
                            std::cout << "target_arrival=3537:" << std::hex << product << std::dec
                                      << ",identity_verified=1\n";
                        }
                    }
                    // Context focus may precede the target's arrival packet.
                    if (header.command == 0xe0 && (header.flags & 0x20) && header.length >= 4) {
                        ++focusEvents;
                        uint32_t focus{};
                        std::memcpy(&focus, payload, 4);
                        std::cout << "driver_focus,status=" << focus << '\n';
                        clockEvent(focus ? "driver_focus_gained" : "driver_focus_lost");
                    }
                    if (target && header.device == target && header.command == 0x10 &&
                        header.length == 60 && payload[0] == 0xe0) {
                        ++telemetry;
                        lastTelemetry = GetTickCount64();
                        if (serviceTransport::active) {
                            auto frame = telemetryFrame(payload, header.flags, header.sequence);
                            if (!serviceTransport::send(frame)) return 9;
                        }
                    }
                    offset += sizeof(Header) + header.length;
                }
                reads.queue(index);
            } else if (wait == WAIT_FAILED) throw std::runtime_error("Direct read wait failed");
            if (sendHeartbeat && target && GetTickCount64() >= nextHeartbeat) {
                if (!heartbeat(device.value, target)) return 6;
                ++writes;
                nextHeartbeat = GetTickCount64() + 310;
            }
            if (GetTickCount64() >= nextSummary) {
#ifdef G7PRO_TELEMETRY_SERVICE
                std::cout << "direct_sample,packets=" << packets << ",telemetry=" << telemetry
                          << ",heartbeats=" << writes << '\n';
#else
                std::cout << "direct_sample,foreground=" << (window.value && GetForegroundWindow() == window.value)
                          << ",packets=" << packets << ",telemetry=" << telemetry << ",heartbeats=" << writes << '\n';
#endif
                nextSummary = GetTickCount64() + 1000;
            }
        }
        std::cout << "direct_summary,packets=" << packets << ",telemetry=" << telemetry
                  << ",focus_events=" << focusEvents << ",heartbeats=" << writes << ",malformed=" << malformed << '\n';
        return telemetry ? 0 : 7;
    } catch (std::exception const& error) {
        std::cerr << "direct_error=" << error.what() << '\n';
        return 1;
    }
}

int wmain(int argc, wchar_t** argv) {
#ifdef G7PRO_TELEMETRY_SERVICE
    if (argc == 2 && !_wcsicmp(argv[1], L"--control-self-test")) return gipControl::selfTest() ? 0 : 1;
    if (argc == 2 && !_wcsicmp(argv[1], L"--frame-self-test")) return telemetryFrameSelfTest() ? 0 : 1;
    if (argc == 2 && !_wcsicmp(argv[1], L"--service")) return serviceTransport::dispatch();
    std::cerr << "Use --service, --control-self-test, or --frame-self-test\n";
    return 1;
#else
    if (argc == 2 && !_wcsicmp(argv[1], L"--control-self-test")) return gipControl::selfTest() ? 0 : 1;
    if (argc == 2 && !_wcsicmp(argv[1], L"--service-protocol-version")) return 4; // v2 wire + persistent backend
    if (argc == 2 && !_wcsicmp(argv[1], L"--target-state")) return appModeEntry::state();
    if (argc == 2 && !_wcsicmp(argv[1], L"--frame-self-test")) return telemetryFrameSelfTest() ? 0 : 1;
    if (argc == 2 && !_wcsicmp(argv[1], L"--service")) return serviceTransport::dispatch();
    if (argc == 2 && !_wcsicmp(argv[1], L"--prepare-app-mode")) return appModeEntry::prepare();
    return runReader(argc, argv);
#endif
}
