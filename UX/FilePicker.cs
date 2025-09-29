using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public record FilePickerOptions(bool Multi, string[]? Filters, PathPickerMode Mode = PathPickerMode.OpenExisting);

public interface IFilePicker
{
    Task<List<string>> ShowAsync(FilePickerOptions opt);
}

/// <summary>
/// Factory that returns the right platform picker at runtime.
/// </summary>
public static class FilePicker
{
    public static IFilePicker Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsFilePicker();
        if (OperatingSystem.IsMacOS()) return new MacFilePicker();
        // TODO: add Linux support, possibly via zenity/kdialog/portals
        throw new PlatformNotSupportedException("No file picker for this OS.");
    }
}

/* =========================
 * macOS: NSOpenPanel via ObjC runtime, **forced to run on the main thread**
 * ========================= */
internal sealed class MacFilePicker : IFilePicker
{
    public Task<List<string>> ShowAsync(FilePickerOptions opt) => Log.Method(ctx =>
    {
        var extensions = NormalizeExtensions(opt.Filters);

        if (opt.Mode == PathPickerMode.SaveFile)
        {
            var saved = SaveFile(extensions);
            var results = string.IsNullOrEmpty(saved) ? new List<string>() : new List<string> { saved };
            if (results.Count == 0)
            {
                ctx.Append(Log.Data.Message, "User cancelled");
                ctx.Append(Log.Data.Result, "<empty>");
            }
            else
            {
                ctx.Append(Log.Data.Result, string.Join(", ", results));
            }
            ctx.Succeeded(results.Count > 0);
            return Task.FromResult(results);
        }

        // We are on the UI thread (Photino.Invoke). Just open the panel.
        var files = OpenFiles(allowMultiple: opt.Multi, allowedExtensions: extensions);
        ctx.Append(Log.Data.Result, files.Count == 0 ? "<empty>" : string.Join(", ", files));
        ctx.Succeeded(files.Count > 0);
        return Task.FromResult(files);
    });

    static string[]? NormalizeExtensions(string[]? patterns)
    {
        if (patterns is null || patterns.Length == 0) return null;
        var list = new List<string>();
        foreach (var p in patterns)
            foreach (var part in p.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var e = part.Trim().TrimStart('*').TrimStart('.');
                if (!string.IsNullOrWhiteSpace(e)) list.Add(e);
            }
        return list.Count == 0 ? null : list.ToArray();
    }

    // ===== Objective-C runtime =====
    [DllImport("/usr/lib/libobjc.A.dylib")] static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern nint objc_msgSend_nint(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern nuint objc_msgSend_nuint(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector, bool arg1);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern void objc_msgSend_void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_IntPtr_nuint(IntPtr receiver, IntPtr selector, nuint index);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_IntPtr_retPtr(IntPtr receiver, IntPtr selector);

    static IntPtr GetClass(string name) => objc_getClass(name);
    static IntPtr Sel(string name) => sel_registerName(name);

    sealed class AutoReleasePool : IDisposable
    {
        static readonly IntPtr NSAutoreleasePool = GetClass("NSAutoreleasePool");
        static readonly IntPtr selAlloc = Sel("alloc");
        static readonly IntPtr selInit = Sel("init");
        static readonly IntPtr selDrain = Sel("drain");
        readonly IntPtr pool;
        public AutoReleasePool()
        {
            var tmp = objc_msgSend_IntPtr(NSAutoreleasePool, selAlloc);
            pool = objc_msgSend_IntPtr(tmp, selInit);
        }
        public void Dispose() => objc_msgSend_IntPtr(pool, selDrain);
    }

    static string NSStringToManagedString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return string.Empty;
        var utf8Sel = Sel("UTF8String");
        IntPtr cStringPtr = objc_msgSend_IntPtr_retPtr(nsString, utf8Sel);
        return Marshal.PtrToStringUTF8(cStringPtr) ?? string.Empty;
    }

    static IntPtr NSStringFromManaged(string s)
    {
        var NSString = GetClass("NSString");
        var alloc = objc_msgSend_IntPtr(NSString, Sel("alloc"));
        var initWithUTF8Sel = Sel("initWithUTF8String:");
        var bytes = Encoding.UTF8.GetBytes(s);
        var p = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, p, bytes.Length);
        Marshal.WriteByte(p, bytes.Length, 0);
        var str = objc_msgSend_IntPtr_IntPtr(alloc, initWithUTF8Sel, p);
        Marshal.FreeHGlobal(p);
        return str; // drained by autorelease pool
    }

    static IntPtr BuildNSArrayOfNSStrings(string[] items)
    {
        var NSMutableArray = GetClass("NSMutableArray");
        var array = objc_msgSend_IntPtr(NSMutableArray, Sel("array"));
        var addObjSel = Sel("addObject:");
        foreach (var it in items)
        {
            var ns = NSStringFromManaged(it.TrimStart('.'));
            objc_msgSend_void_IntPtr(array, addObjSel, ns);
        }
        return array;
    }

    static List<string> OpenFiles(bool allowMultiple, string[]? allowedExtensions) => Log.Method(ctx =>
    {
        ctx.Append(Log.Data.Input, $"allowMultiple={allowMultiple}, allowedExtensions=[{(allowedExtensions is null ? "" : string.Join(", ", allowedExtensions))}]");
        using var _ = new AutoReleasePool();

        var NSOpenPanel = GetClass("NSOpenPanel");
        var panel = objc_msgSend_IntPtr(NSOpenPanel, Sel("openPanel"));

        objc_msgSend_void_bool(panel, Sel("setAllowsMultipleSelection:"), allowMultiple);
        objc_msgSend_void_bool(panel, Sel("setCanChooseFiles:"), true);
        objc_msgSend_void_bool(panel, Sel("setCanChooseDirectories:"), false);

        if (allowedExtensions is { Length: > 0 })
        {
            var nsArray = BuildNSArrayOfNSStrings(allowedExtensions);
            objc_msgSend_void_IntPtr(panel, Sel("setAllowedFileTypes:"), nsArray);
        }

        var result = objc_msgSend_nint(panel, Sel("runModal"));
        const nint NSModalResponseOK = 1;
        if (result != NSModalResponseOK)
        {
            ctx.Append(Log.Data.Message, "User cancelled");
            ctx.Succeeded();
            return new();
        }

        ctx.Append(Log.Data.Message, "User selected files");

        var urls = objc_msgSend_IntPtr(panel, Sel("URLs"));
        var count = objc_msgSend_nuint(urls, Sel("count"));

        var paths = new List<string>((int)count);
        for (nuint i = 0; i < count; i++)
        {
            var url = objc_msgSend_IntPtr_nuint(urls, Sel("objectAtIndex:"), i);
            var pathNsString = objc_msgSend_IntPtr(url, Sel("path"));
            paths.Add(NSStringToManagedString(pathNsString));
        }
        ctx.Append(Log.Data.Result, paths.Count == 0 ? "<empty>" : string.Join(", ", paths));
        ctx.Succeeded(paths.Count > 0);
        return paths;
    });

    static string? SaveFile(string[]? allowedExtensions)
    {
        using var _ = new AutoReleasePool();

        var NSSavePanel = GetClass("NSSavePanel");
        var panel = objc_msgSend_IntPtr(NSSavePanel, Sel("savePanel"));

        objc_msgSend_void_bool(panel, Sel("setCanCreateDirectories:"), true);

        if (allowedExtensions is { Length: > 0 })
        {
            var nsArray = BuildNSArrayOfNSStrings(allowedExtensions);
            objc_msgSend_void_IntPtr(panel, Sel("setAllowedFileTypes:"), nsArray);
        }

        var result = objc_msgSend_nint(panel, Sel("runModal"));
        const nint NSModalResponseOK = 1;
        if (result != NSModalResponseOK) return null;

        var url = objc_msgSend_IntPtr(panel, Sel("URL"));
        var pathNsString = objc_msgSend_IntPtr(url, Sel("path"));
        return NSStringToManagedString(pathNsString);
    }
}

/* =========================
 * Windows: IFileOpenDialog COM (no WinForms dependency)
 * ========================= */
internal sealed class WindowsFilePicker : IFilePicker
{
    public Task<List<string>> ShowAsync(FilePickerOptions opt)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();

        return Task.FromResult(opt.Mode == PathPickerMode.SaveFile ? ShowFileSaveDialog(opt) : ShowFileOpenDialog(opt));
    }

    static List<string> ShowFileOpenDialog(FilePickerOptions opt)
    {
        var results = new List<string>();
        IFileOpenDialog? dlg = null;
        try
        {
            dlg = (IFileOpenDialog)new FileOpenDialogRCW();
            // Options
            dlg.GetOptions(out var flags);
            flags |= FOS.FOS_FORCEFILESYSTEM;
            if (opt.Multi) flags |= FOS.FOS_ALLOWMULTISELECT;

            if (PathPickerMode.OpenExisting == opt.Mode)
            {
                flags |= FOS.FOS_FILEMUSTEXIST | FOS.FOS_PATHMUSTEXIST;
            }
            else
            {
                flags |= FOS.FOS_PATHMUSTEXIST | FOS.FOS_OVERWRITEPROMPT;
            }

            dlg.SetOptions(flags);

            ApplyFilters(opt, dlg);

            var hr = dlg.Show(IntPtr.Zero);
            if (hr == (uint)HRESULT.ERROR_CANCELLED) return results;

            if (opt.Multi)
            {
                dlg.GetResults(out var items);
                items.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    items.GetItemAt(i, out var item);
                    item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var pszName);
                    results.Add(Marshal.PtrToStringUni(pszName)!);
                    Marshal.FreeCoTaskMem(pszName);
                    Marshal.ReleaseComObject(item);
                }
                Marshal.ReleaseComObject(items);
            }
            else
            {
                dlg.GetResult(out var item);
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var pszName);
                results.Add(Marshal.PtrToStringUni(pszName)!);
                Marshal.FreeCoTaskMem(pszName);
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            if (dlg != null) Marshal.ReleaseComObject(dlg);
        }
        return results;
    }

    static List<string> ShowFileSaveDialog(FilePickerOptions opt)
    {
        var results = new List<string>();
        IFileSaveDialog? dlg = null;
        try
        {
            dlg = (IFileSaveDialog)new FileSaveDialogRCW();
            dlg.GetOptions(out var flags);
            flags |= FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST | FOS.FOS_OVERWRITEPROMPT;
            dlg.SetOptions(flags);

            ApplyFilters(opt, dlg);

            var hr = dlg.Show(IntPtr.Zero);
            if (hr == (uint)HRESULT.ERROR_CANCELLED) return results;

            dlg.GetResult(out var item);
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var pszName);
            results.Add(Marshal.PtrToStringUni(pszName)!);
            Marshal.FreeCoTaskMem(pszName);
            Marshal.ReleaseComObject(item);
        }
        finally
        {
            if (dlg != null) Marshal.ReleaseComObject(dlg);
        }

        return results;
    }

    static void ApplyFilters(FilePickerOptions opt, IFileDialog dialog)
    {
        if (opt.Filters is { Length: > 0 })
        {
            var specs = BuildFilterSpec(opt.Filters);
            dialog.SetFileTypes((uint)specs.Length, specs);
        }
    }

    static COMDLG_FILTERSPEC[] BuildFilterSpec(string[] patterns)
    {
        // Convert ["*.csv","*.txt;*.md"] -> [{ "Supported files", "*.csv;*.txt;*.md" }]
        var flat = new List<string>();
        foreach (var p in patterns)
            foreach (var part in p.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                flat.Add(part);
        var joined = string.Join(";", flat);
        return new[] { new COMDLG_FILTERSPEC { pszName = "Supported files", pszSpec = joined } };
    }

    // ===== COM interop bits =====

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    private class FileSaveDialogRCW { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        // IModalWindow
        [PreserveSig] uint Show(IntPtr parent);

        // IFileDialog
        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder(IntPtr psi);
        void SetFolder(IntPtr psi);
        void GetFolder(out IntPtr ppsi);
        void GetCurrentSelection(out IntPtr ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName(out IntPtr pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IntPtr psi, uint fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog : IFileDialog
    {
        void GetResults(out IShellItemArray ppenum);
        void GetSelectedItems(out IShellItemArray ppsai);
    }

    [ComImport, Guid("84bccd23-5fde-4cdb-aea4-af64b83d78ab"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileSaveDialog : IFileDialog
    {
        void SetSaveAsItem(IntPtr psi);
        void SetProperties(IntPtr pStore);
        void SetCollectedProperties(IntPtr pList, bool fAppendDefault);
        void GetProperties(out IntPtr ppStore);
        void ApplyProperties(IntPtr psi, IntPtr pStore, IntPtr hwnd, IntPtr pSink);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    [Flags]
    private enum FOS : uint
    {
        FOS_OVERWRITEPROMPT = 0x00000002,
        FOS_STRICTFILETYPES = 0x00000004,
        FOS_NOCHANGEDIR = 0x00000008,
        FOS_PICKFOLDERS = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040,
        FOS_ALLNONSTORAGEITEMS = 0x00000080,
        FOS_NOVALIDATE = 0x00000100,
        FOS_ALLOWMULTISELECT = 0x00000200,
        FOS_PATHMUSTEXIST = 0x00000800,
        FOS_FILEMUSTEXIST = 0x00001000,
        FOS_CREATEPROMPT = 0x00002000,
        FOS_SHAREAWARE = 0x00004000,
        FOS_NOREADONLYRETURN = 0x00008000,
        FOS_NOTESTFILECREATE = 0x00010000,
        FOS_DONTADDTORECENT = 0x02000000,
    }

    private enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid rbhid, ref Guid riid, out IntPtr ppvOut);
        void GetPropertyStore(int Flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(ref Guid keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(uint dwAttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex, out IShellItem ppsi);
    }

    private static class HRESULT
    {
        public const uint ERROR_CANCELLED = 0x800704C7;
    }
}
