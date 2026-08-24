#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <commctrl.h>
#include <wchar.h>

/*
 * FrameClip 进程内菜单桥接。
 *
 * PotPlayer 的皮肤菜单只能绑定播放器内置 CmdID，XML 没有第三方回调。
 * 本 DLL 由线程级 WH_MOUSE Hook 装入 PotPlayer，并在播放器 UI 线程中对子类化
 * 相关窗口。FrameClip 子菜单的九行全部绑定同一个安全叶子命令；鼠标点击时先
 * 根据真实子菜单窗口的客户区定位动作，随后在 WM_COMMAND 到达时吞掉原命令并
 * 启动 FrameClip 的一次性动作入口。
 *
 * DLL 不修改 PotPlayer 文件、导入表或可执行代码，卸载 Hook 后即可完全退出。
 */

static HMODULE g_module;
static BOOL g_module_pinned;
static HWND g_main_window;
static HWND g_root_menu;
static HWND g_action_menu;
static DWORD g_session_started;
static DWORD g_pending_until;
static int g_pending_action = -1;

static const UINT_PTR FRAMECLIP_SUBCLASS_ID = 0x46434C50u;
static const DWORD MENU_SESSION_MS = 10000;
static const DWORD COMMAND_WINDOW_MS = 1000;
static const UINT FRAMECLIP_PLACEHOLDER_COMMAND = 0xE140u; /* ID_APP_ABOUT */
static const UINT FRAMECLIP_ACTION_MESSAGE = WM_APP + 0x4C0;
static const wchar_t *FRAMECLIP_MESSAGE_WINDOW = L"PotPlayerFrameClip.NativeBridge";

static const wchar_t *g_action_arguments[] = {
    L"--capture",
    L"--mark-in",
    L"--mark-out",
    L"--original",
    L"--precise",
    L"--clear",
    L"--settings",
    L"--open-images",
    L"--open-videos"
};

static BOOL TickIsCurrent(DWORD now, DWORD deadline)
{
    return (LONG)(deadline - now) >= 0;
}

/*
 * 窗口子类保存的是 DLL 内函数地址。Hook 所有者退出时 Windows 会撤销 Hook，
 * 但不会替我们移除 PotPlayer 窗口上的子类，因此 DLL 必须固定到播放器进程结束。
 * 这也意味着升级桥接文件前必须先关闭 PotPlayer，安装器会执行该检查。
 */
static void PinBridgeModule(void)
{
    HMODULE pinned;
    if (g_module_pinned) return;
    if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        (LPCWSTR)&g_module, &pinned)) {
        g_module_pinned = TRUE;
    }
}

static void ResetMenuSession(void)
{
    g_root_menu = NULL;
    g_action_menu = NULL;
    g_session_started = GetTickCount();
    g_pending_until = 0;
    g_pending_action = -1;
}

static void WriteBridgeLog(const wchar_t *message, int value)
{
    wchar_t local_app_data[MAX_PATH];
    wchar_t directory[MAX_PATH];
    wchar_t path[MAX_PATH];
    wchar_t line[320];
    SYSTEMTIME time;
    DWORD written;
    HANDLE file;

    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", local_app_data, MAX_PATH)) return;
    wsprintfW(directory, L"%s\\PotPlayerFrameClip", local_app_data);
    CreateDirectoryW(directory, NULL);
    wsprintfW(path, L"%s\\bridge-debug.log", directory);
    GetLocalTime(&time);
    wsprintfW(line, L"%02u:%02u:%02u.%03u pid=%lu %s %d\r\n",
        time.wHour, time.wMinute, time.wSecond, time.wMilliseconds,
        GetCurrentProcessId(), message, value);

    file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return;
    WriteFile(file, line, (DWORD)(wcslen(line) * sizeof(wchar_t)), &written, NULL);
    CloseHandle(file);
}

static BOOL IsMenuCandidate(HWND window)
{
    RECT rect;
    int width;
    int height;

    if (!window || window == g_main_window || !IsWindowVisible(window)) return FALSE;
    if (!GetWindowRect(window, &rect)) return FALSE;
    width = rect.right - rect.left;
    height = rect.bottom - rect.top;
    if (width < 120 || width > 900 || height < 80 || height > 1400) return FALSE;
    return TRUE;
}

/*
 * 菜单设计基准：普通动作行高 24，分隔线高 5，总高 226。
 * PotPlayer 会按 DPI/皮肤缩放整个菜单，因此使用客户区高度归一化，不依赖屏幕
 * 坐标、固定像素或菜单弹出方向。
 */
static int MapActionFromClientY(HWND menu, int y)
{
    RECT client;
    int normalized;
    if (!GetClientRect(menu, &client) || client.bottom <= 0 || y < 0 || y >= client.bottom) return -1;
    normalized = (int)(((LONGLONG)y * 226) / client.bottom);

    if (normalized < 24) return 0;
    if (normalized < 29) return -1;
    if (normalized < 53) return 1;
    if (normalized < 77) return 2;
    if (normalized < 101) return 3;
    if (normalized < 125) return 4;
    if (normalized < 130) return -1;
    if (normalized < 154) return 5;
    if (normalized < 178) return 6;
    if (normalized < 202) return 7;
    if (normalized < 226) return 8;
    return -1;
}

static BOOL CALLBACK FindFrameClipReceiverCallback(HWND window, LPARAM parameter)
{
    wchar_t caption[128];
    HWND *receiver = (HWND *)parameter;

    if (!receiver || *receiver) return FALSE;
    if (!GetWindowTextW(window, caption, (int)(sizeof(caption) / sizeof(caption[0])))) return TRUE;
    if (wcscmp(caption, FRAMECLIP_MESSAGE_WINDOW) != 0) return TRUE;
    *receiver = window;
    return FALSE;
}

/*
 * FindWindow(NULL, caption) 在部分 PotPlayer/Windows 组合中没有返回实际存在的
 * WinForms 隐藏窗口。枚举顶层窗口并精确比较标题更稳定，也不会依赖动态生成的
 * WindowsForms10 窗口类名。
 */
static HWND FindFrameClipReceiver(void)
{
    HWND receiver = NULL;
    EnumWindows(FindFrameClipReceiverCallback, (LPARAM)&receiver);
    return receiver;
}

static void LaunchFrameClipAction(int action)
{
    HWND receiver;
    wchar_t module_path[MAX_PATH];
    wchar_t executable[MAX_PATH];
    wchar_t command_line[MAX_PATH * 2];
    wchar_t *separator;
    STARTUPINFOW startup;
    PROCESS_INFORMATION process;

    if (action < 0 || action >= (int)(sizeof(g_action_arguments) / sizeof(g_action_arguments[0]))) return;
    receiver = FindFrameClipReceiver();
    if (receiver && PostMessageW(receiver, FRAMECLIP_ACTION_MESSAGE, (WPARAM)action, 0)) {
        WriteBridgeLog(L"posted-action", action);
        return;
    }
    WriteBridgeLog(receiver ? L"post-action-failed" : L"receiver-not-found", (int)GetLastError());
    if (!GetModuleFileNameW(g_module, module_path, MAX_PATH)) return;
    separator = wcsrchr(module_path, L'\\');
    if (!separator) return;
    *separator = L'\0';
    wsprintfW(executable, L"%s\\PotPlayerFrameClip.exe", module_path);
    wsprintfW(command_line, L"\"%s\" %s", executable, g_action_arguments[action]);

    ZeroMemory(&startup, sizeof(startup));
    ZeroMemory(&process, sizeof(process));
    startup.cb = sizeof(startup);
    if (CreateProcessW(executable, command_line, NULL, NULL, FALSE,
        CREATE_UNICODE_ENVIRONMENT, NULL, module_path, &startup, &process)) {
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        WriteBridgeLog(L"launched-action", action);
    } else {
        WriteBridgeLog(L"launch-failed", (int)GetLastError());
    }
}

static LRESULT CALLBACK FrameClipSubclassProc(
    HWND window, UINT message, WPARAM w_param, LPARAM l_param,
    UINT_PTR subclass_id, DWORD_PTR reference_data)
{
    DWORD now = GetTickCount();
    (void)subclass_id;
    (void)reference_data;

    if (message == WM_COMMAND && g_pending_action >= 0) {
        int action = g_pending_action;
        BOOL current = TickIsCurrent(now, g_pending_until);
        g_pending_action = -1;
        g_pending_until = 0;

        /*
         * 自绘菜单窗口没有稳定的类名或可访问标题，其他 PotPlayer 二级菜单也可能
         * 短暂成为候选窗口。只有 FrameClip XML 使用的安全占位命令到达时才吞掉
         * WM_COMMAND；其他命令必须原样交还播放器。
         */
        if (!current || HIWORD(w_param) != 0 || LOWORD(w_param) != FRAMECLIP_PLACEHOLDER_COMMAND || l_param != 0) {
            WriteBridgeLog(L"ignored-command", LOWORD(w_param));
            return DefSubclassProc(window, message, w_param, l_param);
        }

        PostMessageW(window, WM_CANCELMODE, 0, 0);
        WriteBridgeLog(L"intercept-command", LOWORD(w_param));
        LaunchFrameClipAction(action);
        return 0;
    }

    if (message == WM_NCDESTROY) {
        RemoveWindowSubclass(window, FrameClipSubclassProc, FRAMECLIP_SUBCLASS_ID);
        if (window == g_main_window) g_main_window = NULL;
        if (window == g_root_menu) g_root_menu = NULL;
        if (window == g_action_menu) g_action_menu = NULL;
    }
    return DefSubclassProc(window, message, w_param, l_param);
}

static void EnsureSubclass(HWND window)
{
    if (window) SetWindowSubclass(window, FrameClipSubclassProc, FRAMECLIP_SUBCLASS_ID, 0);
}

__declspec(dllexport) LRESULT CALLBACK FrameClipMouseProc(int code, WPARAM w_param, LPARAM l_param)
{
    MOUSEHOOKSTRUCT *mouse;
    DWORD now;
    HWND root;
    HWND point_window;
    HWND target_window;

    if (code < 0 || !l_param) return CallNextHookEx(NULL, code, w_param, l_param);
    mouse = (MOUSEHOOKSTRUCT *)l_param;
    PinBridgeModule();
    now = GetTickCount();
    point_window = WindowFromPoint(mouse->pt);
    if (!point_window) point_window = mouse->hwnd;
    target_window = GetAncestor(point_window, GA_ROOT);
    if (!target_window) target_window = point_window;

    if (w_param == WM_RBUTTONDOWN) {
        root = target_window;
        g_main_window = root;
        EnsureSubclass(g_main_window);
        ResetMenuSession();
        WriteBridgeLog(L"session-start", 0);
    } else if (g_main_window && TickIsCurrent(now, g_session_started + MENU_SESSION_MS)) {
        if (IsMenuCandidate(target_window)) {
            EnsureSubclass(target_window);
            if (!g_root_menu) {
                g_root_menu = target_window;
                WriteBridgeLog(L"root-menu", 0);
            } else if (target_window != g_root_menu && target_window != g_action_menu) {
                g_action_menu = target_window;
                WriteBridgeLog(L"action-menu", 0);
            }
        }

        if (w_param == WM_LBUTTONDOWN) {
            if (target_window == g_action_menu) {
                POINT point = mouse->pt;
                int action;
                ScreenToClient(g_action_menu, &point);
                action = MapActionFromClientY(g_action_menu, point.y);
                if (action >= 0) {
                    g_pending_action = action;
                    g_pending_until = now + COMMAND_WINDOW_MS;
                    EnsureSubclass(g_main_window);
                    EnsureSubclass(g_action_menu);
                    WriteBridgeLog(L"pending-action", action);
                }
            } else {
                g_pending_action = -1;
                g_pending_until = 0;
            }
        }
    }

    return CallNextHookEx(NULL, code, w_param, l_param);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}
