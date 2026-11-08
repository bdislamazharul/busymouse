// BusyMouse launcher for the "no runtime" bundle.
//
// The app itself is .NET, so it can't say anything until .NET is installed. This tiny native exe sits next to the
// app folder as BusyMouse.exe: it checks for the .NET Runtime, offers to download and install it when it's
// missing, then starts app\BusyMouse.exe with the same command line.
//
// Build with tools\package.ps1 (needs the Visual Studio C++ Build Tools).

#ifndef UNICODE
#define UNICODE
#endif
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <urlmon.h>
#include <string>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "urlmon.lib")

namespace
{
  constexpr int RequiredMajor = 10;  // Runs on 10 or newer: the app rolls forward to later major versions.
  constexpr wchar_t Title[] = L"BusyMouse";

#if defined(_M_ARM64)
  constexpr wchar_t Arch[] = L"arm64";
#else
  constexpr wchar_t Arch[] = L"x64";
#endif

  std::wstring RuntimeUrl ()
  {
    return std::wstring (L"https://aka.ms/dotnet/") + std::to_wstring (RequiredMajor) + L".0/dotnet-runtime-win-" + Arch + L".exe";
  }

  std::wstring PageUrl ()
  {
    return std::wstring (L"https://dotnet.microsoft.com/download/dotnet/") + std::to_wstring (RequiredMajor) + L".0";
  }

  std::wstring Env (const wchar_t* name)
  {
    wchar_t buffer[MAX_PATH * 2];
    DWORD length = GetEnvironmentVariableW (name, buffer, ARRAYSIZE (buffer));

    return length > 0 && length < ARRAYSIZE (buffer) ? std::wstring (buffer, length) : std::wstring ();
  }

  // True when root\shared\Microsoft.NETCore.App holds a release (not preview) of version RequiredMajor or later.
  bool HasRuntimeIn (const std::wstring& root)
  {
    if (root.empty ())
      return false;

    WIN32_FIND_DATAW found;
    HANDLE find = FindFirstFileW ((root + L"\\shared\\Microsoft.NETCore.App\\*").c_str (), &found);

    if (find == INVALID_HANDLE_VALUE)
      return false;

    bool ok = false;

    do
    {
      if ((found.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && iswdigit (found.cFileName[0]) && !wcschr (found.cFileName, L'-')
          && _wtoi (found.cFileName) >= RequiredMajor)
        ok = true;
    } while (!ok && FindNextFileW (find, &found));

    FindClose (find);

    return ok;
  }

  // Looks where the .NET host looks: DOTNET_ROOT, the registered install location, then Program Files\dotnet.
  bool HasRuntime ()
  {
    if (HasRuntimeIn (Env (std::wstring (L"DOTNET_ROOT_").append (Arch).c_str ())) || HasRuntimeIn (Env (L"DOTNET_ROOT")))
      return true;

    wchar_t location[MAX_PATH];
    DWORD size = sizeof (location);
    std::wstring key = std::wstring (L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\") + Arch;

    if (RegGetValueW (HKEY_LOCAL_MACHINE, key.c_str (), L"InstallLocation", RRF_RT_REG_SZ | RRF_SUBKEY_WOW6432KEY, nullptr, location, &size) == ERROR_SUCCESS
        && HasRuntimeIn (location))
      return true;

    PWSTR programFiles = nullptr;
    bool ok = false;

    if (SUCCEEDED (SHGetKnownFolderPath (FOLDERID_ProgramFiles, 0, nullptr, &programFiles)))
      ok = HasRuntimeIn (std::wstring (programFiles) + L"\\dotnet");

    CoTaskMemFree (programFiles);

    return ok;
  }

  // Shows download progress in the standard Windows progress dialog, and lets Cancel stop it.
  class Progress final : public IBindStatusCallback
  {
  public:
    explicit Progress (IProgressDialog* dialog) : dialog (dialog) {}

    HRESULT STDMETHODCALLTYPE QueryInterface (REFIID riid, void** ppv) override
    {
      if (riid == IID_IUnknown || riid == IID_IBindStatusCallback)
      {
        *ppv = static_cast<IBindStatusCallback*> (this);
        return S_OK;
      }

      *ppv = nullptr;
      return E_NOINTERFACE;
    }

    ULONG STDMETHODCALLTYPE AddRef () override { return 2; }
    ULONG STDMETHODCALLTYPE Release () override { return 1; }

    HRESULT STDMETHODCALLTYPE OnProgress (ULONG done, ULONG total, ULONG, LPCWSTR) override
    {
      if (this->dialog->HasUserCancelled ())
        return E_ABORT;

      if (total > 0)
      {
        this->dialog->SetProgress (done, total);

        wchar_t line[128];
        swprintf_s (line, L"%.1f MB of %.1f MB", done / 1048576.0, total / 1048576.0);
        this->dialog->SetLine (2, line, FALSE, nullptr);
      }

      return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnStartBinding (DWORD, IBinding*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE GetPriority (LONG*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE OnLowResource (DWORD) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnStopBinding (HRESULT, LPCWSTR) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE GetBindInfo (DWORD*, BINDINFO*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnDataAvailable (DWORD, DWORD, FORMATETC*, STGMEDIUM*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnObjectAvailable (REFIID, IUnknown*) override { return S_OK; }

  private:
    IProgressDialog* dialog;
  };

  // Downloads the runtime installer to %TEMP%. Returns false if it failed or the user cancelled.
  bool Download (const std::wstring& file, bool& cancelled)
  {
    cancelled = false;

    IProgressDialog* dialog = nullptr;

    if (FAILED (CoCreateInstance (CLSID_ProgressDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS (&dialog))))
      return SUCCEEDED (URLDownloadToFileW (nullptr, RuntimeUrl ().c_str (), file.c_str (), 0, nullptr));

    dialog->SetTitle (Title);
    dialog->SetLine (1, L"Downloading the .NET Runtime\x2026", FALSE, nullptr);
    dialog->SetCancelMsg (L"Cancelling\x2026", nullptr);
    dialog->StartProgressDialog (nullptr, nullptr, PROGDLG_NORMAL | PROGDLG_AUTOTIME, nullptr);

    Progress progress (dialog);
    HRESULT hr = URLDownloadToFileW (nullptr, RuntimeUrl ().c_str (), file.c_str (), 0, &progress);

    cancelled = hr == E_ABORT || dialog->HasUserCancelled ();
    dialog->StopProgressDialog ();
    dialog->Release ();

    return SUCCEEDED (hr) && !cancelled;
  }

  // Runs the Microsoft installer with a progress bar and no questions. It asks Windows for admin rights itself.
  DWORD Install (const std::wstring& file)
  {
    SHELLEXECUTEINFOW info = { sizeof (info) };
    info.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC;
    info.lpFile = file.c_str ();
    info.lpParameters = L"/install /passive /norestart";
    info.nShow = SW_SHOWNORMAL;

    if (!ShellExecuteExW (&info) || info.hProcess == nullptr)
      return GetLastError () == ERROR_CANCELLED ? ERROR_INSTALL_USEREXIT : GetLastError ();

    WaitForSingleObject (info.hProcess, INFINITE);

    DWORD code = 1;
    GetExitCodeProcess (info.hProcess, &code);
    CloseHandle (info.hProcess);

    return code;
  }

  bool Ask (const std::wstring& text, UINT icon = MB_ICONINFORMATION)
  {
    return MessageBoxW (nullptr, text.c_str (), Title, MB_YESNO | icon | MB_SETFOREGROUND) == IDYES;
  }

  void OfferPage (const std::wstring& why)
  {
    if (Ask (why + L"\n\nOpen the .NET download page so you can install it yourself?", MB_ICONWARNING))
      ShellExecuteW (nullptr, L"open", PageUrl ().c_str (), nullptr, nullptr, SW_SHOWNORMAL);
  }

  // Makes sure the runtime is there, installing it if the user agrees. False means: don't start the app.
  bool EnsureRuntime ()
  {
    if (HasRuntime ())
      return true;

    std::wstring version = std::to_wstring (RequiredMajor);

    if (!Ask (L"BusyMouse needs the .NET " + version + L" Runtime, which isn't installed on this PC.\n\n"
              L"Download it from Microsoft (about 30 MB) and install it now?"))
      return false;

    wchar_t temp[MAX_PATH];
    GetTempPathW (ARRAYSIZE (temp), temp);
    std::wstring file = std::wstring (temp) + L"dotnet-runtime-" + version + L"-win-" + Arch + L".exe";

    bool cancelled;

    if (!Download (file, cancelled))
    {
      DeleteFileW (file.c_str ());

      if (!cancelled)
        OfferPage (L"The .NET Runtime couldn't be downloaded. Check your internet connection.");

      return false;
    }

    DWORD code = Install (file);
    DeleteFileW (file.c_str ());

    if (code == ERROR_INSTALL_USEREXIT || code == ERROR_CANCELLED)
      return false;

    if (HasRuntime ())
      return true;

    if (code == ERROR_SUCCESS || code == ERROR_SUCCESS_REBOOT_REQUIRED)
    {
      MessageBoxW (nullptr, L"The .NET Runtime was installed, but BusyMouse can't find it yet. Restart Windows, then start BusyMouse again.",
                   Title, MB_OK | MB_ICONINFORMATION);
      return false;
    }

    OfferPage (L"The .NET Runtime couldn't be installed (error " + std::to_wstring (code) + L").");

    return false;
  }

  // The launcher's own command line without its exe path, so the app gets the same options.
  const wchar_t* Arguments ()
  {
    const wchar_t* p = GetCommandLineW ();

    if (*p == L'"')
    {
      for (++p; *p && *p != L'"'; ++p) {}
      if (*p) ++p;
    }
    else
      for (; *p && *p != L' ' && *p != L'\t'; ++p) {}

    while (*p == L' ' || *p == L'\t')
      ++p;

    return p;
  }
}

int WINAPI wWinMain (HINSTANCE, HINSTANCE, PWSTR, int)
{
  CoInitializeEx (nullptr, COINIT_APARTMENTTHREADED);

  wchar_t self[MAX_PATH];
  GetModuleFileNameW (nullptr, self, ARRAYSIZE (self));
  PathRemoveFileSpecW (self);
  std::wstring app = std::wstring (self) + L"\\app\\BusyMouse.exe";

  if (GetFileAttributesW (app.c_str ()) == INVALID_FILE_ATTRIBUTES)
  {
    MessageBoxW (nullptr, (L"BusyMouse is incomplete: " + app + L" is missing.\n\nReinstall BusyMouse.").c_str (), Title, MB_OK | MB_ICONERROR);
    return 2;
  }

  if (!EnsureRuntime ())
    return 1;

  std::wstring commandLine = L"\"" + app + L"\" " + Arguments ();
  STARTUPINFOW startup = { sizeof (startup) };
  PROCESS_INFORMATION process = {};

  if (!CreateProcessW (app.c_str (), commandLine.data (), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process))
  {
    MessageBoxW (nullptr, L"BusyMouse couldn't be started.", Title, MB_OK | MB_ICONERROR);
    return 3;
  }

  // Let the app take the foreground (its window, or a message), since the user launched it through us.
  AllowSetForegroundWindow (process.dwProcessId);
  CloseHandle (process.hThread);
  CloseHandle (process.hProcess);
  CoUninitialize ();

  return 0;
}
