#pragma once

#include <windows.h>
#include <setupapi.h>
#include <cfgmgr32.h>
#include <xinput.h>
#include <mmsystem.h>
#include <algorithm>
#include <cstdint>
#include <cwctype>
#include <string>

#pragma comment(lib, "cfgmgr32.lib")
#pragma comment(lib, "winmm.lib")

namespace gipModeEntry {
    enum class TargetState : DWORD { absent = 0, normalXinput = 1, xboxGip = 2, ambiguous = 3 };
    enum class PrepareResult : DWORD { none = 0, alreadyGip = 1, transitioned = 2 };

    constexpr TargetState classify(unsigned normal, unsigned gip, bool failed) {
        if (!normal && !gip) return TargetState::absent;
        if (failed) return TargetState::ambiguous;
        // A profile can expose companion keyboard/mouse interfaces, and the old
        // identity can briefly overlap the new one during re-enumeration.
        // Prefer a healthy Xbox GIP identity whenever one is present.
        return gip ? TargetState::xboxGip : TargetState::normalXinput;
    }

    constexpr DWORD transitionFailure(TargetState finalState) {
        if (finalState == TargetState::absent) return ERROR_NOT_FOUND;
        if (finalState == TargetState::ambiguous) return ERROR_DEVICE_NOT_CONNECTED;
        return ERROR_TIMEOUT;
    }

    inline bool selfTest() {
        return classify(0, 0, false) == TargetState::absent &&
            classify(1, 0, false) == TargetState::normalXinput &&
            classify(0, 1, false) == TargetState::xboxGip &&
            classify(1, 1, false) == TargetState::xboxGip &&
            classify(2, 0, false) == TargetState::normalXinput &&
            classify(0, 1, true) == TargetState::ambiguous &&
            transitionFailure(TargetState::absent) == ERROR_NOT_FOUND &&
            transitionFailure(TargetState::normalXinput) == ERROR_TIMEOUT &&
            transitionFailure(TargetState::xboxGip) == ERROR_TIMEOUT &&
            transitionFailure(TargetState::ambiguous) == ERROR_DEVICE_NOT_CONNECTED;
    }

    inline TargetState state() {
        constexpr GUID usb{0xa5dcbf10, 0x6530, 0x11d2, {0x90, 0x1f, 0x00, 0xc0, 0x4f, 0xb9, 0x51, 0xed}};
        HDEVINFO devices = SetupDiGetClassDevsW(&usb, nullptr, nullptr, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devices == INVALID_HANDLE_VALUE) return TargetState::absent;

        unsigned normal = 0, gip = 0;
        bool failed = false;
        for (DWORD index = 0;; ++index) {
            SP_DEVINFO_DATA info{sizeof(info)};
            if (!SetupDiEnumDeviceInfo(devices, index, &info)) break;

            wchar_t id[MAX_DEVICE_ID_LEN]{};
            if (!SetupDiGetDeviceInstanceIdW(devices, &info, id, ARRAYSIZE(id), nullptr)) {
                failed = true;
                break;
            }
            std::wstring identity(id);
            std::transform(identity.begin(), identity.end(), identity.begin(),
                [](wchar_t value) { return static_cast<wchar_t>(towupper(value)); });
            bool isNormal = identity.find(L"USB\\VID_3537&PID_100A\\") == 0;
            bool isGip = identity.find(L"USB\\VID_3537&PID_109B\\") == 0 ||
                identity.find(L"USB\\VID_3537&PID_109C\\") == 0;
            if (!isNormal && !isGip) continue;

            ULONG flags = 0, problem = 0;
            if (CM_Get_DevNode_Status(&flags, &problem, info.DevInst, 0) != CR_SUCCESS ||
                problem || !(flags & DN_STARTED)) failed = true;
            if (isNormal) ++normal;
            else ++gip;
        }
        SetupDiDestroyDeviceInfoList(devices);
        return classify(normal, gip, failed);
    }

    inline DWORD prepare(HANDLE stop, PrepareResult& result) {
        result = PrepareResult::none;
        TargetState initial = state();
        if (initial == TargetState::xboxGip) {
            result = PrepareResult::alreadyGip;
            return ERROR_SUCCESS;
        }
        if (initial == TargetState::absent) return ERROR_NOT_FOUND;
        if (initial != TargetState::normalXinput) return ERROR_DEVICE_NOT_CONNECTED;

        HMODULE module = LoadLibraryExW(L"xinput1_4.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!module) return GetLastError();
        auto get = reinterpret_cast<DWORD(WINAPI *)(DWORD, XINPUT_STATE *)>(GetProcAddress(module, "XInputGetState"));
        auto set = reinterpret_cast<DWORD(WINAPI *)(DWORD, XINPUT_VIBRATION *)>(GetProcAddress(module, "XInputSetState"));

        DWORD slot = 0, count = 0;
        if (get && set) {
            for (DWORD index = 0; index < XUSER_MAX_COUNT; ++index) {
                XINPUT_STATE value{};
                if (get(index, &value) == ERROR_SUCCESS) {
                    slot = index;
                    ++count;
                }
            }
        }
        if (!get || !set || count != 1 || timeBeginPeriod(1) != TIMERR_NOERROR) {
            FreeLibrary(module);
            return ERROR_NOT_SUPPORTED;
        }

        constexpr uint8_t pairs[][2] = {
            {'g', 'a'}, {0, 0}, {'m', 'e'}, {0, 0}, {'s', 'i'},
            {0, 0}, {'r', 'a'}, {0, 0}, {'p', 'p'}, {0, 0}
        };
        bool cancelled = false;
        for (auto const& pair : pairs) {
            if (WaitForSingleObject(stop, 0) == WAIT_OBJECT_0) {
                cancelled = true;
                break;
            }
            XINPUT_VIBRATION vibration{
                static_cast<WORD>(pair[0] * 0x0101),
                static_cast<WORD>(pair[1] * 0x0101)
            };
            if (set(slot, &vibration) != ERROR_SUCCESS) break; // Re-enumeration can interrupt the final send.
            if (WaitForSingleObject(stop, 15) == WAIT_OBJECT_0) {
                cancelled = true;
                break;
            }
        }
        timeEndPeriod(1);
        FreeLibrary(module);
        if (cancelled) return ERROR_CANCELLED;

        int healthy = 0;
        TargetState finalState = TargetState::normalXinput;
        for (int attempt = 0; attempt < 10; ++attempt) {
            if (WaitForSingleObject(stop, 1000) == WAIT_OBJECT_0) return ERROR_CANCELLED;
            TargetState current = state();
            if (current == TargetState::ambiguous) return ERROR_DEVICE_NOT_CONNECTED;
            finalState = current;
            healthy = current == TargetState::xboxGip ? healthy + 1 : 0;
            if (healthy == 3) {
                result = PrepareResult::transitioned;
                return ERROR_SUCCESS;
            }
        }
        // A temporary absence is normal while the USB identity re-enumerates.
        // Only classify it as missing if neither supported identity is present
        // when the full transition window expires.
        return transitionFailure(finalState); // No reset and no automatic retry.
    }
}
