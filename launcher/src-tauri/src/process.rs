//! The few Windows calls the launcher needs around a game process.

use std::{mem, ptr};
use windows_sys::Win32::{
    Foundation::{CloseHandle, BOOL, HWND, LPARAM, RECT},
    System::{
        ProcessStatus::{EnumProcesses, GetProcessMemoryInfo, PROCESS_MEMORY_COUNTERS, PROCESS_MEMORY_COUNTERS_EX},
        Threading::{OpenProcess, QueryFullProcessImageNameW, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_VM_READ},
    },
    UI::WindowsAndMessaging::{
        EnumWindows, GetWindowLongW, GetWindowRect, GetWindowThreadProcessId, IsIconic, IsWindowVisible,
        SetForegroundWindow, SetWindowPos, GWL_STYLE, SWP_NOACTIVATE, SWP_NOSIZE, SWP_NOZORDER, WS_CAPTION,
    },
};

struct Search {
    pid: u32,
    window: HWND,
}

unsafe extern "system" fn visit(window: HWND, param: LPARAM) -> BOOL {
    let search = &mut *(param as *mut Search);
    let mut pid = 0;
    GetWindowThreadProcessId(window, &mut pid);
    if pid == search.pid && IsWindowVisible(window) != 0 {
        search.window = window;
        return 0;
    }
    1
}

fn window(pid: u32) -> Option<HWND> {
    let mut search = Search { pid, window: ptr::null_mut() };
    unsafe { EnumWindows(Some(visit), &mut search as *mut Search as LPARAM) };
    (!search.window.is_null()).then_some(search.window)
}

/// Gives a process's window the keyboard and controller.
pub fn focus(pid: u32) -> bool {
    window(pid).is_some_and(|window| unsafe { SetForegroundWindow(window) != 0 })
}

fn is_windowed(window: HWND) -> bool {
    unsafe { GetWindowLongW(window, GWL_STYLE) as u32 & WS_CAPTION == WS_CAPTION && IsIconic(window) == 0 }
}

/// Where a windowed game is. `None` in full screen.
pub fn position(pid: u32) -> Option<(i32, i32)> {
    let window = window(pid).filter(|window| is_windowed(*window))?;
    let mut rect = RECT { left: 0, top: 0, right: 0, bottom: 0 };
    (unsafe { GetWindowRect(window, &mut rect) } != 0).then_some((rect.left, rect.top))
}

/// Puts a windowed game where the other one was.
pub fn move_to(pid: u32, (x, y): (i32, i32)) {
    if let Some(window) = window(pid).filter(|window| is_windowed(*window)) {
        unsafe { SetWindowPos(window, ptr::null_mut(), x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE) };
    }
}

/// Working set, peak working set and private memory, in megabytes.
pub fn memory_mb(pid: u32) -> Option<(usize, usize, usize)> {
    unsafe {
        let handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, 0, pid);
        if handle.is_null() {
            return None;
        }
        let mut info: PROCESS_MEMORY_COUNTERS_EX = mem::zeroed();
        let size = mem::size_of::<PROCESS_MEMORY_COUNTERS_EX>() as u32;
        info.cb = size;
        let ok = GetProcessMemoryInfo(handle, &mut info as *mut _ as *mut PROCESS_MEMORY_COUNTERS, size);
        CloseHandle(handle);
        const MB: usize = 1024 * 1024;
        (ok != 0).then_some((info.WorkingSetSize / MB, info.PeakWorkingSetSize / MB, info.PrivateUsage / MB))
    }
}

/// True if a process with the given executable file name (case-insensitive) is currently running.
pub fn is_running(exe_name: &str) -> bool {
    let mut pids = vec![0u32; 2048];
    let mut returned = 0u32;
    let ok = unsafe {
        EnumProcesses(pids.as_mut_ptr(), (pids.len() * mem::size_of::<u32>()) as u32, &mut returned)
    };
    if ok == 0 {
        return false;
    }
    let count = (returned as usize) / mem::size_of::<u32>();
    let mut buf = vec![0u16; 512];
    for &pid in &pids[..count.min(pids.len())] {
        if pid == 0 {
            continue;
        }
        unsafe {
            let handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
            if handle.is_null() {
                continue;
            }
            let mut len = buf.len() as u32;
            let got = QueryFullProcessImageNameW(handle, 0, buf.as_mut_ptr(), &mut len);
            CloseHandle(handle);
            if got != 0 && len > 0 {
                let path = String::from_utf16_lossy(&buf[..len as usize]);
                if let Some(name) = path.rsplit(['\\', '/']).next() {
                    if name.eq_ignore_ascii_case(exe_name) {
                        return true;
                    }
                }
            }
        }
    }
    false
}
