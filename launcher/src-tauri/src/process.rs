//! The few Windows calls the launcher needs around a game process.

use std::{mem, os::windows::io::AsRawHandle, path::Path, process::Child, ptr, thread::sleep, time::Duration};
use windows_sys::Win32::{
    Foundation::{CloseHandle, BOOL, HANDLE, HWND, LPARAM, RECT},
    System::{
        ProcessStatus::{EnumProcesses, GetProcessMemoryInfo, PROCESS_MEMORY_COUNTERS, PROCESS_MEMORY_COUNTERS_EX},
        SystemInformation::{GlobalMemoryStatusEx, MEMORYSTATUSEX},
        Threading::{
            OpenProcess, QueryFullProcessImageNameW, TerminateProcess, PROCESS_QUERY_LIMITED_INFORMATION,
            PROCESS_TERMINATE, PROCESS_VM_READ,
        },
    },
    UI::WindowsAndMessaging::{
        EnumWindows, GetWindowLongW, GetWindowRect, GetWindowThreadProcessId, IsIconic, IsWindow, IsWindowVisible,
        SetForegroundWindow, SetWindowPos, ShowWindowAsync, GWL_STYLE, SWP_NOACTIVATE, SWP_NOSIZE, SWP_NOZORDER,
        SW_HIDE, SW_SHOW, WS_CAPTION,
    },
};

#[link(name = "ntdll")]
extern "system" {
    fn NtSuspendProcess(process: HANDLE) -> i32;
    fn NtResumeProcess(process: HANDLE) -> i32;
}

/// A process's main window, kept as a number so it can cross threads. Found while it is visible.
pub fn window_id(pid: u32) -> Option<isize> {
    window(pid).map(|window| window as isize)
}

/// Hides a window without waiting on its thread, then waits up to half a second for it to go.
pub fn hide_window(id: isize) {
    let window = id as HWND;
    unsafe {
        ShowWindowAsync(window, SW_HIDE);
        for _ in 0..50 {
            if IsWindowVisible(window) == 0 {
                break;
            }
            sleep(Duration::from_millis(10));
        }
    }
}

/// Shows a window hidden by [`hide_window`]. False if the window is gone.
pub fn show_window(id: isize) -> bool {
    let window = id as HWND;
    unsafe { IsWindow(window) != 0 && ShowWindowAsync(window, SW_SHOW) != 0 }
}

/// Stops every thread of a running child. It keeps its memory and carries on where it was after [`resume`].
pub fn suspend(child: &Child) -> bool {
    unsafe { NtSuspendProcess(child.as_raw_handle() as HANDLE) >= 0 }
}

pub fn resume(child: &Child) -> bool {
    unsafe { NtResumeProcess(child.as_raw_handle() as HANDLE) >= 0 }
}

/// Physical memory installed and free now, in megabytes.
pub fn system_memory_mb() -> Option<(u64, u64)> {
    let mut status: MEMORYSTATUSEX = unsafe { mem::zeroed() };
    status.dwLength = mem::size_of::<MEMORYSTATUSEX>() as u32;
    const MB: u64 = 1024 * 1024;
    (unsafe { GlobalMemoryStatusEx(&mut status) } != 0).then_some((status.ullTotalPhys / MB, status.ullAvailPhys / MB))
}

/// Ends a game an earlier launcher left paused, but only if that process still runs from `folder`.
pub fn kill_left_behind(pid: u32, folder: &Path) -> bool {
    unsafe {
        let handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE, 0, pid);
        if handle.is_null() {
            return false;
        }
        let mut buf = vec![0u16; 1024];
        let mut len = buf.len() as u32;
        let ours = QueryFullProcessImageNameW(handle, 0, buf.as_mut_ptr(), &mut len) != 0
            && is_inside(&String::from_utf16_lossy(&buf[..len as usize]), folder);
        let killed = ours && TerminateProcess(handle, 1) != 0;
        CloseHandle(handle);
        killed
    }
}

fn is_inside(path: &str, folder: &Path) -> bool {
    let folder = folder.to_string_lossy().trim_end_matches(['\\', '/']).to_ascii_lowercase();
    let path = path.to_ascii_lowercase();
    path.len() > folder.len() && path.starts_with(&folder) && matches!(path.as_bytes()[folder.len()], b'\\' | b'/')
}

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

#[cfg(test)]
mod tests {
    use super::*;
    use std::process::{Command, Stdio};

    #[test]
    fn only_games_in_the_mirror_folder_count() {
        let folder = Path::new(r"C:\Users\a\AppData\Local\HKSS\games");
        assert!(is_inside(r"C:\Users\a\AppData\Local\HKSS\games\silksong\Hollow Knight Silksong.exe", folder));
        assert!(is_inside(r"c:\users\A\appdata\local\hkss\GAMES\hollow_knight\hollow_knight.exe", folder));
        assert!(!is_inside(r"C:\Users\a\AppData\Local\HKSS\games-old\x.exe", folder));
        assert!(!is_inside(r"C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight\hollow_knight.exe", folder));
    }

    #[test]
    fn pauses_and_resumes_a_child() {
        let mut child = Command::new("ping").args(["-n", "30", "127.0.0.1"]).stdout(Stdio::null()).spawn().unwrap();
        assert!(suspend(&child));
        assert!(child.try_wait().unwrap().is_none());
        assert!(resume(&child));
        child.kill().unwrap();
        child.wait().unwrap();
    }

    #[test]
    fn reads_system_memory() {
        let (total, free) = system_memory_mb().unwrap();
        assert!(total > 0 && free <= total);
    }
}
