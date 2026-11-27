using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DevSAK.Services
{
    [SupportedOSPlatform("windows")]
    public static class ExplorerFolderService
    {
        public static void OpenAndSelectFile(string filePath)
        {
            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath)!;
            if (!TryActivateFolder(directory, fullPath))
                Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
        }

        private static bool TryActivateFolder(string directory, string filePath)
        {
            object? shell = null;
            object? windows = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return false;
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return false;
                windows = ((dynamic)shell).Windows();
                int count = ((dynamic)windows).Count;
                for (int index = 0; index < count; index++)
                {
                    object? window = null;
                    object? document = null;
                    object? folder = null;
                    object? self = null;
                    object? item = null;
                    try
                    {
                        window = ((dynamic)windows).Item(index);
                        if (window == null) continue;
                        document = ((dynamic)window).Document;
                        folder = ((dynamic)document).Folder;
                        self = ((dynamic)folder).Self;
                        string openPath = ((dynamic)self).Path;
                        if (!string.Equals(NormalizePath(openPath), NormalizePath(directory), StringComparison.OrdinalIgnoreCase))
                            continue;

                        var handle = new IntPtr((long)((dynamic)window).HWND);
                        if (handle == IntPtr.Zero || !IsWindow(handle)) continue;

                        // Selection is best effort: failure must not create a duplicate window.
                        try
                        {
                            item = ((dynamic)folder).ParseName(Path.GetFileName(filePath));
                            if (item != null)
                                ((dynamic)document).SelectItem(item, 1 | 4 | 8 | 16);
                        }
                        catch (Exception ex) { Debug.WriteLine($"Explorer selection failed: {ex.Message}"); }

                        if (IsIconic(handle)) ShowWindow(handle, 9); // SW_RESTORE
                        SetForegroundWindow(handle);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        // A window can close or navigate while the collection is being inspected.
                        Debug.WriteLine($"Explorer window inspection failed: {ex.Message}");
                    }
                    finally
                    {
                        Release(item);
                        Release(self);
                        Release(folder);
                        Release(document);
                        Release(window);
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Explorer lookup failed: {ex.Message}"); }
            finally
            {
                Release(windows);
                Release(shell);
            }
            return false;
        }

        private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        private static void Release(object? value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr handle);
    }
}
