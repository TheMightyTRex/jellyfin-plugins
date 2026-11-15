using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>
/// The few Linux calls .NET has no managed form of: keeping a file's owner, and running a thread at the lowest
/// processor and disk priority. Each one is optional: when a call is missing (another system, an older C library) the
/// plug-in carries on without it.
/// </summary>
internal static class Unix
{
    private const int AtFdCwd = -100;
    private const int StatxUid = 0x8;
    private const int StatxGid = 0x10;

    /// <summary>Gives <paramref name="target"/> the owner and group of <paramref name="source"/> when they differ.</summary>
    public static void CopyOwner(string source, string target)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var a = Owner(source);
            var b = Owner(target);
            if (a is null || b is null || a == b)
            {
                return;
            }

            _ = chown(target, a.Value.Uid, a.Value.Gid); // only works for a file someone else made when allowed to
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    /// <summary>The owner and group of a file, from statx, whose layout is the same on every processor.</summary>
    public static (uint Uid, uint Gid)? Owner(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var buffer = new byte[256];
        try
        {
            if (statx(AtFdCwd, path, 0, StatxUid | StatxGid, buffer) != 0)
            {
                return null;
            }
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }

        return (BitConverter.ToUInt32(buffer, 20), BitConverter.ToUInt32(buffer, 24));
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a thread of its own at nice 19 and in the idle disk class, so it gives way to
    /// everything else Jellyfin does, such as playing a film.
    /// </summary>
    public static Task<T> RunAtLowestPriority<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Lower();
            try
            {
                done.SetResult(work());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        {
            IsBackground = true,
            Name = "Picture shrinker"
        };
        thread.Start();
        return done.Task;
    }

    private static void Lower()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var tid = gettid();
            _ = setpriority(0, (uint)tid, 19); // PRIO_PROCESS with a thread id sets that thread only
            _ = syscall(IoprioSetNumber(), 1, tid, 3 << 13); // IOPRIO_WHO_PROCESS, IOPRIO_CLASS_IDLE
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    /// <summary>Hands the C library's free memory back to the system (glibc's malloc_trim; nothing where it is missing).</summary>
    public static void TrimNativeHeap()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            _ = malloc_trim(0);
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    private static long IoprioSetNumber() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => 251,
        Architecture.Arm64 => 30,
        Architecture.Arm => 314,
        _ => -1
    };

    [DllImport("libc", SetLastError = true)]
    private static extern int chown(string path, uint owner, uint group);

    [DllImport("libc", SetLastError = true)]
    private static extern int statx(int dirfd, string path, int flags, uint mask, byte[] buffer);

    [DllImport("libc", SetLastError = true)]
    private static extern int gettid();

    [DllImport("libc")]
    private static extern int malloc_trim(nuint pad);

    [DllImport("libc", SetLastError = true)]
    private static extern int setpriority(int which, uint who, int prio);

    [DllImport("libc", SetLastError = true)]
    private static extern long syscall(long number, int which, int who, int ioprio);
}
