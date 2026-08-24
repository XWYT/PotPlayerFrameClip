#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <wchar.h>

/* 32 位 PotPlayer 的 Hook 必须由 32 位宿主持有；主程序通过父进程 PID 管理其生命周期。 */

typedef struct HookEntry {
    DWORD process_id;
    DWORD thread_id;
    HHOOK hook;
} HookEntry;

static HookEntry g_hooks[16];
static HMODULE g_bridge;
static HOOKPROC g_hook_proc;

static BOOL IsPotPlayerProcess(DWORD process_id)
{
    HANDLE process;
    wchar_t path[MAX_PATH];
    wchar_t *name;
    DWORD length = MAX_PATH;
    BOOL wow64 = FALSE;

    process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, process_id);
    if (!process) return FALSE;
    if (!QueryFullProcessImageNameW(process, 0, path, &length)) {
        CloseHandle(process);
        return FALSE;
    }
    name = wcsrchr(path, L'\\');
    name = name ? name + 1 : path;
    if (!wcsstr(name, L"PotPlayer") || wcsstr(name, L"FrameClip")) {
        CloseHandle(process);
        return FALSE;
    }

    if (sizeof(void *) == 4) {
        SYSTEM_INFO info;
        GetNativeSystemInfo(&info);
        if (info.wProcessorArchitecture != PROCESSOR_ARCHITECTURE_INTEL) {
            if (!IsWow64Process(process, &wow64) || !wow64) {
                CloseHandle(process);
                return FALSE;
            }
        }
    }
    CloseHandle(process);
    return TRUE;
}

static BOOL HasHook(DWORD process_id, DWORD thread_id)
{
    int index;
    for (index = 0; index < 16; index++)
        if (g_hooks[index].hook && g_hooks[index].process_id == process_id && g_hooks[index].thread_id == thread_id)
            return TRUE;
    return FALSE;
}

static void AddHook(DWORD process_id, DWORD thread_id)
{
    int index;
    HHOOK hook;
    if (HasHook(process_id, thread_id)) return;
    hook = SetWindowsHookExW(WH_MOUSE, g_hook_proc, g_bridge, thread_id);
    if (!hook) return;
    for (index = 0; index < 16; index++) {
        if (!g_hooks[index].hook) {
            g_hooks[index].process_id = process_id;
            g_hooks[index].thread_id = thread_id;
            g_hooks[index].hook = hook;
            return;
        }
    }
    UnhookWindowsHookEx(hook);
}

static BOOL CALLBACK InspectWindow(HWND window, LPARAM parameter)
{
    DWORD process_id;
    DWORD thread_id;
    (void)parameter;
    if (!IsWindowVisible(window) || GetWindow(window, GW_OWNER)) return TRUE;
    thread_id = GetWindowThreadProcessId(window, &process_id);
    if (thread_id && IsPotPlayerProcess(process_id)) AddHook(process_id, thread_id);
    return TRUE;
}

static void RemoveDeadHooks(void)
{
    int index;
    for (index = 0; index < 16; index++) {
        HANDLE process;
        if (!g_hooks[index].hook) continue;
        process = OpenProcess(SYNCHRONIZE, FALSE, g_hooks[index].process_id);
        if (!process || WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
            UnhookWindowsHookEx(g_hooks[index].hook);
            ZeroMemory(&g_hooks[index], sizeof(g_hooks[index]));
        }
        if (process) CloseHandle(process);
    }
}

static DWORD ReadParentProcessId(void)
{
    int count;
    int index;
    LPWSTR *arguments = CommandLineToArgvW(GetCommandLineW(), &count);
    DWORD result = 0;
    if (!arguments) return 0;
    for (index = 1; index + 1 < count; index++) {
        if (lstrcmpiW(arguments[index], L"--parent-pid") == 0) {
            result = wcstoul(arguments[index + 1], NULL, 10);
            break;
        }
    }
    LocalFree(arguments);
    return result;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, LPWSTR command_line, int show_command)
{
    wchar_t module_path[MAX_PATH];
    wchar_t *separator;
    HANDLE mutex;
    HANDLE parent = NULL;
    DWORD parent_id;
    int index;
    (void)instance;
    (void)previous;
    (void)command_line;
    (void)show_command;

    mutex = CreateMutexW(NULL, FALSE, L"Local\\PotPlayerFrameClipBridgeHost32");
    if (!mutex || GetLastError() == ERROR_ALREADY_EXISTS) return 0;
    parent_id = ReadParentProcessId();
    if (parent_id) parent = OpenProcess(SYNCHRONIZE, FALSE, parent_id);

    GetModuleFileNameW(NULL, module_path, MAX_PATH);
    separator = wcsrchr(module_path, L'\\');
    if (!separator) return 2;
    lstrcpyW(separator + 1, L"FrameClipBridge32.dll");
    g_bridge = LoadLibraryW(module_path);
    if (!g_bridge) return 3;
    g_hook_proc = (HOOKPROC)GetProcAddress(g_bridge, "FrameClipMouseProc");
    if (!g_hook_proc) return 4;

    while (!parent || WaitForSingleObject(parent, 0) == WAIT_TIMEOUT) {
        RemoveDeadHooks();
        EnumWindows(InspectWindow, 0);
        Sleep(1000);
    }

    for (index = 0; index < 16; index++)
        if (g_hooks[index].hook) UnhookWindowsHookEx(g_hooks[index].hook);
    if (parent) CloseHandle(parent);
    FreeLibrary(g_bridge);
    CloseHandle(mutex);
    return 0;
}
