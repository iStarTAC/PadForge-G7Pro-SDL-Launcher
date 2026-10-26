#pragma once

#include <sddl.h>
#include <array>
#include <atomic>
#include <fstream>
#include "../DirectGipReader/ControlProtocol.h"

#pragma comment(lib, "advapi32.lib")

int runReader(int argc, wchar_t **argv);

namespace serviceTransport {
    constexpr wchar_t name[] = L"GameSirG7ProTelemetry";
    constexpr wchar_t pipeName[] = L"\\\\.\\pipe\\GameSirG7ProTelemetry.Data.v1";
    constexpr wchar_t controlName[] = L"\\\\.\\pipe\\GameSirG7ProTelemetry.Control.v1";

    inline HANDLE stop{}, sessionStop{}, pipe{INVALID_HANDLE_VALUE}, worker{};
    inline SERVICE_STATUS_HANDLE statusHandle{};
    inline bool active = false;
    inline std::atomic<DWORD> readerResult{};
    inline DWORD ownerPid{};

    inline void report(DWORD state, DWORD error = 0) {
        SERVICE_STATUS status{};
        status.dwServiceType = SERVICE_WIN32_OWN_PROCESS;
        status.dwCurrentState = state;
        status.dwControlsAccepted = state == SERVICE_RUNNING ? SERVICE_ACCEPT_STOP : 0;
        status.dwWin32ExitCode = error;
        SetServiceStatus(statusHandle, &status);
    }

    inline void WINAPI handler(DWORD code) {
        if (code == SERVICE_CONTROL_STOP) SetEvent(stop);
    }

    inline bool stopped() {
        return active && (WaitForSingleObject(stop, 0) == WAIT_OBJECT_0 ||
            WaitForSingleObject(sessionStop, 0) == WAIT_OBJECT_0);
    }

    inline bool waitIo(HANDLE handle, OVERLAPPED& io, DWORD timeout, DWORD& bytes, bool session = false) {
        HANDLE handles[]{io.hEvent, stop, sessionStop};
        DWORD wait = WaitForMultipleObjects(session ? 3 : 2, handles, FALSE, timeout);
        if (wait == WAIT_OBJECT_0) return !!GetOverlappedResult(handle, &io, &bytes, FALSE);
        CancelIoEx(handle, &io);
        GetOverlappedResult(handle, &io, &bytes, TRUE);
        return false;
    }

    inline bool transfer(HANDLE handle, void *buffer, DWORD size, bool write, DWORD timeout, bool session = false) {
        OVERLAPPED io{};
        io.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!io.hEvent) return false;
        DWORD bytes{};
        bool success = write ? !!WriteFile(handle, buffer, size, &bytes, &io) :
            !!ReadFile(handle, buffer, size, &bytes, &io);
        if (success) success = !!GetOverlappedResult(handle, &io, &bytes, FALSE);
        else if (GetLastError() == ERROR_IO_PENDING) success = waitIo(handle, io, timeout, bytes, session);
        CloseHandle(io.hEvent);
        return success && bytes == size;
    }

    inline bool connect(HANDLE handle, DWORD timeout, bool session = false) {
        OVERLAPPED io{};
        io.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!io.hEvent) return false;
        DWORD bytes{};
        bool success = !!ConnectNamedPipe(handle, &io);
        if (!success) {
            DWORD error = GetLastError();
            success = error == ERROR_PIPE_CONNECTED ||
                (error == ERROR_IO_PENDING && waitIo(handle, io, timeout, bytes, session));
        }
        CloseHandle(io.hEvent);
        return success;
    }

    inline bool send(std::array<uint8_t, 65> const& frame) {
        return transfer(pipe, const_cast<uint8_t *>(frame.data()),
            static_cast<DWORD>(frame.size()), true, 1000, true);
    }

    inline bool isSystem() {
        HANDLE token{};
        BYTE data[256]{};
        DWORD needed{};
        bool result = false;
        if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) {
            if (GetTokenInformation(token, TokenUser, data, sizeof(data), &needed)) {
                result = !!IsWellKnownSid(reinterpret_cast<TOKEN_USER *>(data)->User.Sid, WinLocalSystemSid);
            }
            CloseHandle(token);
        }
        return result;
    }

    inline bool authenticate(HANDLE control, PSID owner, DWORD& pid) {
        if (!GetNamedPipeClientProcessId(control, &pid) || !pid || !ImpersonateNamedPipeClient(control)) return false;
        HANDLE token{};
        BYTE data[512]{};
        DWORD needed{};
        bool result = false;
        if (OpenThreadToken(GetCurrentThread(), TOKEN_QUERY, TRUE, &token)) {
            if (GetTokenInformation(token, TokenUser, data, sizeof(data), &needed)) {
                result = !!EqualSid(owner, reinterpret_cast<TOKEN_USER *>(data)->User.Sid);
            }
            CloseHandle(token);
        }
        if (!RevertToSelf()) {
            SetEvent(stop);
            return false;
        }
        return result;
    }

    inline DWORD WINAPI readSession(void *) {
        DWORD result = ERROR_TIMEOUT, pid{};
        if (connect(pipe, 15000, true) && !stopped() &&
            GetNamedPipeClientProcessId(pipe, &pid) && pid == ownerPid) {
            wchar_t exe[] = L"GameSirG7ProTelemetry";
            wchar_t duration[] = L"0";
            wchar_t heartbeat[] = L"--heartbeat";
            wchar_t subscription[] = L"--wgi-subscription";
            wchar_t admin[] = L"--admin-endpoint";
            wchar_t *args[]{exe, duration, heartbeat, subscription, admin};
            result = static_cast<DWORD>(runReader(5, args));
        }
        readerResult = result;
        return result;
    }

    inline bool running() {
        return worker && WaitForSingleObject(worker, 0) == WAIT_TIMEOUT;
    }

    inline void reap() {
        if (!worker || running()) return;
        CloseHandle(worker);
        worker = nullptr;
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
        pipe = INVALID_HANDLE_VALUE;
        ownerPid = 0;
    }

    inline DWORD stopReader() {
        if (!worker) return 0;
        SetEvent(sessionStop);
        if (WaitForSingleObject(worker, 5000) != WAIT_OBJECT_0) return ERROR_TIMEOUT;
        reap();
        return 0;
    }

    inline void WINAPI main(DWORD, wchar_t **) {
        statusHandle = RegisterServiceCtrlHandlerW(name, handler);
        if (!statusHandle) return;
        report(SERVICE_START_PENDING);
        stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        sessionStop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        DWORD error = ERROR_ACCESS_DENIED;
        HANDLE control = INVALID_HANDLE_VALUE;
        PSID owner{};
        PSECURITY_DESCRIPTOR security{};
        wchar_t path[32768]{};
        DWORD length = GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));

        if (stop && sessionStop && isSystem() && length && length < ARRAYSIZE(path)) {
            std::wstring directory(path);
            directory.resize(directory.find_last_of(L"\\"));
            std::ifstream config(directory + L"\\client.sid");
            std::string text;
            std::getline(config, text);
            if (!text.empty() && text.back() == '\r') text.pop_back();
            std::wstring sid(text.begin(), text.end());
            if (sid.starts_with(L"S-1-5-21-") && ConvertStringSidToSidW(sid.c_str(), &owner)) {
                std::wstring sddl = L"D:P(A;;GA;;;SY)(A;;GRGW;;;" + sid + L")";
                if (ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1,
                                                                          &security, nullptr)) {
                    SECURITY_ATTRIBUTES attributes{sizeof(attributes), security, FALSE};
                    control = CreateNamedPipeW(controlName,
                        PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
                        PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                        1, 256, 256, 0, &attributes);
                    if (control != INVALID_HANDLE_VALUE) {
                        active = true;
                        error = 0;
                        report(SERVICE_RUNNING);
                        while (WaitForSingleObject(stop, 0) != WAIT_OBJECT_0) {
                            reap();
                            if (!connect(control, 1000)) {
                                DisconnectNamedPipe(control);
                                continue;
                            }
                            gipControl::Request request{};
                            DWORD pid{};
                            if (transfer(control, &request, sizeof(request), false, 3000) &&
                                authenticate(control, owner, pid)) {
                                gipControl::Response response{};
                                response.command = request.command;
                                response.requestId = request.requestId;
                                reap();
                                bool supported = request.command == gipControl::Command::status ||
                                    request.command == gipControl::Command::prepare ||
                                    request.command == gipControl::Command::startReader ||
                                    request.command == gipControl::Command::stopReader;
                                if (!gipControl::valid(request) || !supported) response.error = ERROR_INVALID_DATA;
                                else if (!gipControl::allowed(request.command, running(), ownerPid, pid)) response.error = ERROR_BUSY;
                                else if (request.command == gipControl::Command::prepare) {
                                    if (running()) response.error = ERROR_BUSY;
                                    else {
                                        gipModeEntry::PrepareResult result{};
                                        response.error = gipModeEntry::prepare(stop, result);
                                        response.readerResult = static_cast<DWORD>(result);
                                    }
                                } else if (request.command == gipControl::Command::startReader) {
                                    if (running()) response.error = ERROR_BUSY;
                                    else {
                                        ResetEvent(sessionStop);
                                        readerResult = 0;
                                        ownerPid = pid;
                                        pipe = CreateNamedPipeW(pipeName,
                                            PIPE_ACCESS_OUTBOUND | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
                                            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                                            1, 65 * 16, 0, 0, &attributes);
                                        if (pipe == INVALID_HANDLE_VALUE) {
                                            response.error = GetLastError();
                                            ownerPid = 0;
                                        } else {
                                            worker = CreateThread(nullptr, 0, readSession, nullptr, 0, nullptr);
                                            if (!worker) {
                                                response.error = GetLastError();
                                                CloseHandle(pipe);
                                                pipe = INVALID_HANDLE_VALUE;
                                                ownerPid = 0;
                                            }
                                        }
                                    }
                                } else if (request.command == gipControl::Command::stopReader) {
                                    response.error = stopReader();
                                }
                                response.state = running() ? gipControl::State::reading : gipControl::State::idle;
                                if (request.command != gipControl::Command::prepare) response.readerResult = readerResult;
                                if (transfer(control, &response, sizeof(response), true, 1000)) {
                                    uint8_t ack{};
                                    transfer(control, &ack, 1, false, 1000);
                                }
                            }
                            DisconnectNamedPipe(control);
                        }
                    } else error = GetLastError();
                }
            }
        }

        if (worker) {
            SetEvent(sessionStop);
            WaitForSingleObject(worker, INFINITE);
            reap();
        }
        active = false;
        if (control != INVALID_HANDLE_VALUE) CloseHandle(control);
        if (security) LocalFree(security);
        if (owner) LocalFree(owner);
        if (sessionStop) CloseHandle(sessionStop);
        if (stop) CloseHandle(stop);
        report(SERVICE_STOPPED, error);
    }

    inline int dispatch() {
        SERVICE_TABLE_ENTRYW entries[]{{const_cast<wchar_t *>(name), main}, {nullptr, nullptr}};
        return StartServiceCtrlDispatcherW(entries) ? 0 : static_cast<int>(GetLastError());
    }
}
